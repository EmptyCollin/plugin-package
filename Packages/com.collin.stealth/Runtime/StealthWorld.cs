using System;
using System.Collections.Generic;
using Collin.Core;
using Collin.Core.Stealth;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 跨包适配层：把本包的玩法能力以 core 的契约接口暴露出去（stealth DESIGN §4.4 方案 P）。
    /// <para>消费方（设计辅助台包）只引用 <c>com.collin.core</c>，经
    /// <see cref="Services.TryGet{T}"/> 取用这里的实现；本包未加载时消费方自动降级，
    /// 因此<b>这里注册得越少越安全</b>——只注册已经实现完的能力。
    /// 当前已注册：<see cref="IPatrolWorld"/>、<see cref="IStealthScrubDriver"/>、
    /// <see cref="IVisionWorld"/>、<see cref="IDetectionWorld"/>（M1+M2）。
    /// 尚未注册的契约接口（消费方取用时走降级）：无——M3 状态机沿用 <see cref="IDetectionWorld"/>
    /// 的档位语义，不新增接口。</para>
    ///
    /// <para><b>actorId 的口径</b>：统一由 <see cref="StealthIds"/> 计算（实体 id 哈希）。
    /// 巡逻单位与其视野体<b>必须挂在同一 GameObject</b>（预制件 <c>StealthGuard</c> 保证这一点），
    /// 这样「谁在走」与「谁在看」才是同一个 id，辅助台不必做两套映射。</para>
    ///
    /// <para><b>编辑态回溯的实现方式</b>：编辑器非 Play 状态下不存在帧循环，
    /// <see cref="PatrolAgent"/> 的 LateUpdate 不会跑，所以 <see cref="ScrubTo"/> 必须
    /// <b>显式遍历单位并推送位姿</b>，而不是等帧驱动。这是 B 包「T 周期滑动条实时查看
    /// NPC 位置」能成立的关键（DESIGN D-01 / 验收 15 同源）。</para>
    /// </summary>
    public sealed class StealthWorld : IPatrolWorld, IStealthScrubDriver, IVisionWorld, IDetectionWorld
    {
        static StealthWorld _instance;

        /// <summary>当前注册实例；尚未注册时为 null。</summary>
        public static StealthWorld Instance => _instance;

        readonly Dictionary<int, PatrolAgent> _agentById = new Dictionary<int, PatrolAgent>();
        readonly List<PatrolAgent> _agentScratch = new List<PatrolAgent>();

        /// <summary>注册用单例。重复调用安全（先查 TryGet 再 Register，避免撞 core 的冲突报错）。</summary>
        public static void EnsureRegistered()
        {
            if (_instance == null) _instance = new StealthWorld();

            if (!Services.TryGet<IPatrolWorld>(out _))
            {
                Services.Register<IPatrolWorld>(_instance);
            }
            if (!Services.TryGet<IStealthScrubDriver>(out _))
            {
                Services.Register<IStealthScrubDriver>(_instance);
            }
            if (!Services.TryGet<IVisionWorld>(out _))
            {
                Services.Register<IVisionWorld>(_instance);
            }
            if (!Services.TryGet<IDetectionWorld>(out _))
            {
                Services.Register<IDetectionWorld>(_instance);
            }
        }

        /// <summary>Play 态装配点。域重载后 Services 会被清空，故每次重载都重跑。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterOnPlay()
        {
            EnsureRegistered();
        }

        // ── IPatrolWorld ────────────────────────────────────────────

        /// <summary>场景内全部巡逻单位（每次访问重新枚举并返回新列表，消费方可放心持有）。</summary>
        public IReadOnlyList<StealthActorInfo> Actors => RebuildActors();

        /// <summary>全部单位里最大的 T 周期（秒），供消费方做全局回溯量程。</summary>
        public float MaxCycleTime
        {
            get
            {
                var max = 0f;
                CollectAgents();
                for (var i = 0; i < _agentScratch.Count; i++)
                {
                    var a = _agentScratch[i];
                    if (a == null) continue;
                    var c = a.EffectiveCycle;
                    if (c > max) max = c;
                }
                return max;
            }
        }

        /// <summary>当前时钟状态。场景无时钟时返回 Auto/0。</summary>
        public StealthClockDto ClockState
        {
            get
            {
                var clock = StealthClock.Instance;
                if (clock == null) return new StealthClockDto { mode = 0, time = 0f };
                return new StealthClockDto
                {
                    mode = clock.Mode == StealthClockMode.Manual ? 1 : 0,
                    time = clock.Current
                };
            }
        }

        /// <summary>取某单位在 t 时刻的局部位姿（纯函数，编辑态可用）。</summary>
        public StealthPoseDto PoseAt(int actorId, float t)
        {
            var agent = FindAgent(actorId);
            if (agent == null) return default;
            return ToDto(agent.EvaluateLocal(t));
        }

        /// <summary>取某单位在 t 时刻的世界位姿。</summary>
        public bool TryPoseAtWorld(int actorId, float t, out Vector3 worldPosition, out float worldYawDegrees)
        {
            worldPosition = Vector3.zero;
            worldYawDegrees = 0f;

            var agent = FindAgent(actorId);
            if (agent == null) return false;

            var pose = agent.EvaluateWorld(t);
            worldPosition = pose.position;
            worldYawDegrees = pose.rotation.eulerAngles.y;
            return true;
        }

        /// <summary>按近似等弧长输出某单位整条周期的轨迹采样（局部坐标），与 NPC 实际走位同一求值路径。</summary>
        public void BuildTrajectory(int actorId, float approxSpacingMeters, List<StealthTrajectorySampleDto> output)
        {
            if (output == null) return;
            output.Clear();

            var agent = FindAgent(actorId);
            if (agent == null) return;

            var local = LocalSamples(agent, approxSpacingMeters);
            for (var i = 0; i < local.Count; i++)
            {
                var s = local[i];
                output.Add(new StealthTrajectorySampleDto
                {
                    localPosition = s.localPosition,
                    localYawDegrees = s.localYawDegrees,
                    time = s.time
                });
            }
        }

        /// <summary>读某单位路线的路点、循环方式与默认速度。</summary>
        public bool TryGetWaypoints(int actorId, out IReadOnlyList<StealthWaypointDto> waypoints, out StealthLoopMode loopMode, out float defaultSpeed)
        {
            waypoints = null;
            loopMode = StealthLoopMode.Loop;
            defaultSpeed = 0f;

            var route = RouteOf(FindAgent(actorId));
            if (route == null) return false;

            var list = new List<StealthWaypointDto>(route.WaypointCount);
            var n = route.WaypointCount;
            for (var i = 0; i < n; i++)
            {
                list.Add(new StealthWaypointDto
                {
                    localPosition = route.GetWaypointPosition(i),
                    stopDuration = route.GetStopDuration(i),
                    moveSpeed = route.GetSegmentSpeed(i)
                });
            }

            waypoints = list;
            loopMode = route.LoopMode == PatrolLoopMode.PingPong ? StealthLoopMode.PingPong : StealthLoopMode.Loop;
            defaultSpeed = route.DefaultSpeed;
            return true;
        }

        /// <summary>写回单个路点（特性 3「按玩家线路反推 NPC 巡逻路点」的落点）。</summary>
        public bool TrySetWaypoint(int actorId, int index, StealthWaypointDto waypoint)
        {
            var route = RouteOf(FindAgent(actorId));
            if (route == null) return false;
            if (index < 0 || index >= route.WaypointCount) return false;

            // 走既有写入面而非反射改序列化字段：改完 hash 变化会自动作废累计表。
            route.SetWaypointPosition(index, waypoint.localPosition);
            route.SetStopDuration(index, waypoint.stopDuration);
            route.SetSegmentSpeed(index, waypoint.moveSpeed);
            NotifyChanged();
            return true;
        }

        /// <summary>写循环方式与默认速度。</summary>
        public bool TrySetRouteTuning(int actorId, StealthLoopMode loopMode, float defaultSpeed)
        {
            var route = RouteOf(FindAgent(actorId));
            if (route == null) return false;

            route.SetLoopMode(loopMode == StealthLoopMode.PingPong ? PatrolLoopMode.PingPong : PatrolLoopMode.Loop);
            route.SetDefaultSpeed(defaultSpeed);
            NotifyChanged();
            return true;
        }

        // ── IStealthScrubDriver ─────────────────────────────────────

        /// <summary>结构变更通知（消费方据此作废轨迹/热区缓存）。同帧内多次变更会合并为一次通知。</summary>
        public event Action Changed;

        /// <summary>当前是否手动（回溯）模式。</summary>
        public bool IsManual
        {
            get
            {
                var clock = StealthClock.Instance;
                return clock != null && clock.Mode == StealthClockMode.Manual;
            }
        }

        /// <summary>结构/参数变更广播（消费方据此作废轨迹与热区缓存）。</summary>
        public void NotifyChanged()
        {
            Changed?.Invoke();
        }

        /// <summary>
        /// 切手动模式、把时间索引设为 t，并立即推送全部位姿。
        /// 编辑态没有帧循环，推送必须显式完成，否则拖动滑动条时 NPC 不会动。
        /// </summary>
        public void ScrubTo(float t)
        {
            var clock = StealthClock.Instance;
            if (clock == null)
            {
                Debug.LogWarning("[StealthWorld] 场景内没有 StealthClock，回溯无法生效（请在关卡根挂一个）");
                return;
            }

            clock.Mode = StealthClockMode.Manual;
            clock.ManualTime = Mathf.Max(0f, t);
            PushAllPoses(clock.Current);
            NotifyChanged();
        }

        /// <summary>恢复自动模式（每帧推进）。</summary>
        public void ResumeAuto()
        {
            var clock = StealthClock.Instance;
            if (clock != null) clock.Mode = StealthClockMode.Auto;
            NotifyChanged();
        }

        /// <summary>
        /// 把全部单位在<b>当前</b>时间索引下的位姿推入场景：不改时钟模式、不改 t。
        /// 编辑态没有帧循环，Inspector 改完路点必须靠这里显式刷新，才符合
        /// 「所见即所得」（AGENTS.md §0.1 原则 1）。关闭了自动应用的单位不会被移动。
        /// </summary>
        public void RefreshPreview()
        {
            var clock = StealthClock.Instance;
            PushAllPoses(clock != null ? clock.Current : 0f);
            NotifyChanged();
        }

        void PushAllPoses(float t)
        {
            CollectAgents();
            for (var i = 0; i < _agentScratch.Count; i++)
            {
                var a = _agentScratch[i];
                // 与 Play 态 LateUpdate 同口径：关掉自动应用的单位只供 API 读取，不被写 transform。
                if (a == null || !a.ApplyToTransform) continue;
                // dt=0 → ApplyPose 内朝向直接对齐，不留缓动残差（编辑态单帧预览语义）。
                a.ApplyPose(a.EvaluateWorld(t), 0f);
            }
        }

        // ── 内部 ────────────────────────────────────────────────────

        static PatrolRoute RouteOf(PatrolAgent agent)
        {
            if (agent == null) return null;
            var root = agent.RouteRoot;
            return root != null ? root.Route : null;
        }

        static StealthPoseDto ToDto(PatrolPose pose)
        {
            return new StealthPoseDto
            {
                valid = pose.HasLeg,
                localPosition = pose.localPosition,
                localYawDegrees = pose.localYawDegrees,
                legIndex = pose.legIndex,
                legProgress01 = pose.legProgress01,
                isDwelling = pose.isDwelling,
                cycleTime = pose.cycleTime
            };
        }

        /// <summary>取某单位的局部轨迹采样（与 BuildTrajectory 同一求值口径）。</summary>
        static List<PatrolRoute.TrajectorySample> LocalSamples(PatrolAgent agent, float spacingMeters)
        {
            var buffer = new List<PatrolRoute.TrajectorySample>();
            var route = RouteOf(agent);
            if (route == null) return buffer;
            route.BuildTrajectorySamples(spacingMeters, buffer);
            return buffer;
        }

        void CollectAgents()
        {
            _agentScratch.Clear();
            // Unity 6：FindObjectsOfType 已废弃且会打出 warning，破 §1.6b 归零门禁，必须用新 API。
            _agentScratch.AddRange(UnityEngine.Object.FindObjectsByType<PatrolAgent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
        }

        PatrolAgent FindAgent(int actorId)
        {
            RebuildActors();
            return _agentById.TryGetValue(actorId, out var agent) ? agent : null;
        }

        IReadOnlyList<StealthActorInfo> RebuildActors()
        {
            CollectAgents();
            // 每次重建都换新列表：契约把 Actors 交给消费方持有，
            // 复用同一容器会让消费方上一次的引用被下一次访问悄悄改写。
            var cache = new List<StealthActorInfo>(_agentScratch.Count);
            _agentById.Clear();

            for (var i = 0; i < _agentScratch.Count; i++)
            {
                var agent = _agentScratch[i];
                if (agent == null) continue;

                // id 必须与视野侧同源（同一个 StealthIds.Of）：辅助台按 actorId 问「谁在看这里」时，
                // 巡逻单位与其视野体要落到同一个 id。裸写 GetEntityId().GetHashCode() 会为负，
                // 既撞上契约的 -1「不限定」哨兵，也会与本包其它取值点分叉。
                var id = StealthIds.Of(agent.gameObject);
                if (_agentById.ContainsKey(id)) continue;
                _agentById[id] = agent;

                var root = agent.RouteRoot;
                cache.Add(new StealthActorInfo
                {
                    actorId = id,
                    displayName = agent.gameObject.name,
                    cycleTime = agent.EffectiveCycle,
                    hasRoot = root != null,
                    rootPosition = root != null ? root.transform.position : Vector3.zero,
                    rootRotation = root != null ? root.transform.rotation : Quaternion.identity,
                    rootScale = root != null ? root.transform.localScale : Vector3.one
                });
            }

            return cache;
        }

        // ── IVisionWorld ────────────────────────────────────────────

        /// <summary>core 的层级掩码 → 包内判定层级。两侧值序由用例守卫（DistanceOnly/Cone/LineOfSight = 1/2/4）。</summary>
        static QueryOptions MapLayers(StealthQueryLayers layers)
        {
            var o = QueryOptions.None;
            if ((layers & StealthQueryLayers.DistanceOnly) != 0) o |= QueryOptions.DistanceOnly;
            if ((layers & StealthQueryLayers.Cone) != 0) o |= QueryOptions.ConeOnly;
            if ((layers & StealthQueryLayers.LineOfSight) != 0) o |= QueryOptions.LineOfSight;
            return o;
        }

        /// <summary>场景内全部视野体快照（含被禁用者，由 enabled 标记）。每次返回新列表，消费方可持有。</summary>
        public IReadOnlyList<StealthVisionInfo> Volumes
        {
            get
            {
                var src = VisionRegistry.Volumes;
                var list = new List<StealthVisionInfo>(src.Count);
                for (var i = 0; i < src.Count; i++)
                {
                    var v = src[i];
                    if (v == null) continue;
                    list.Add(v.ToInfo(StealthIds.Of(v.gameObject)));
                }
                return list;
            }
        }

        /// <summary>某单位<b>此刻</b>（其当前位姿）能否看到世界点 p。</summary>
        public bool CanSee(int actorId, Vector3 worldPoint, StealthQueryLayers layers)
        {
            var volume = FindVolume(actorId);
            if (volume == null) return false;
            return volume.CanSee(worldPoint, MapLayers(layers), actorId);
        }

        /// <summary>
        /// 某单位在<b>时刻 t</b> 的位姿下能否看到世界点 p。
        /// 编辑态回溯的关键入口：t 时刻的 NPC 位姿由 M1 纯函数算出，
        /// 辅助台不必进入 Play 态就能问「那个瞬间他看不看得见这里」。
        /// 单位没有巡逻组件时退化为用当前位姿判定。
        /// </summary>
        public bool CanSeeAt(int actorId, float t, Vector3 worldPoint, StealthQueryLayers layers)
        {
            var volume = FindVolume(actorId);
            if (volume == null) return false;

            Vector3 origin;
            Vector3 forward;
            if (!TryObservationAt(actorId, t, volume, out origin, out forward))
            {
                origin = volume.OriginWorld;
                forward = volume.ForwardWorld;
            }

            return volume.CanSeeFrom(origin, forward, worldPoint, MapLayers(layers), actorId);
        }

        /// <summary>
        /// 点 p 在时刻 t 被多少个视野看到。<paramref name="actorIdFilter"/> 为 -1 表示不限定单位；
        /// <paramref name="t"/> 为负表示用各单位<b>当前</b>位姿（不做时间求值，省一次换算）。
        /// </summary>
        public int CountSeers(int actorIdFilter, Vector3 worldPoint, float t, StealthQueryLayers layers)
        {
            var src = VisionRegistry.Volumes;
            var options = MapLayers(layers);
            var count = 0;

            for (var i = 0; i < src.Count; i++)
            {
                var v = src[i];
                if (v == null || !v.Enabled) continue;

                var id = StealthIds.Of(v.gameObject);
                if (actorIdFilter >= 0 && id != actorIdFilter) continue;

                Vector3 origin;
                Vector3 forward;
                if (t >= 0f && TryObservationAt(id, t, v, out origin, out forward))
                {
                    // 用 t 时刻位姿
                }
                else
                {
                    origin = v.OriginWorld;
                    forward = v.ForwardWorld;
                }

                if (v.CanSeeFrom(origin, forward, worldPoint, options, id)) count++;
            }

            return count;
        }

        /// <summary>取某单位观察原点与朝向；单位有巡逻组件时用 t 时刻位姿，否则返回 false（调用方退回当前值）。</summary>
        bool TryObservationAt(int actorId, float t, VisionVolume volume, out Vector3 origin, out Vector3 forward)
        {
            origin = Vector3.zero;
            forward = Vector3.zero;

            var agent = FindAgentCached(actorId);
            if (agent == null) return false;

            var pose = agent.EvaluateWorld(t);
            origin = pose.position + Vector3.up * volume.EffectiveEyeHeight;

            var yawRad = pose.rotation.eulerAngles.y * Mathf.Deg2Rad;
            forward = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
            return true;
        }

        /// <summary>
        /// 带缓存的单位查找：批量路径（<see cref="CountSeers"/> 会对每个视野调用）必须走这里，
        /// 否则每次都要全场景重建，50 单位规模退化成 O(n²)。
        /// 未命中才重建，命中后仍校验对象存活（销毁后 Unity 判 == null 为真）。
        /// </summary>
        PatrolAgent FindAgentCached(int actorId)
        {
            if (_agentById.TryGetValue(actorId, out var cached) && cached != null) return cached;
            RebuildActors();
            return _agentById.TryGetValue(actorId, out var agent) ? agent : null;
        }

        static VisionVolume FindVolume(int actorId)
        {
            var src = VisionRegistry.Volumes;
            for (var i = 0; i < src.Count; i++)
            {
                var v = src[i];
                if (v != null && StealthIds.Of(v.gameObject) == actorId) return v;
            }
            return null;
        }

        // ── IDetectionWorld ─────────────────────────────────────────

        /// <summary>某察觉目标当前的累积察觉度 [0,1]。未知目标返回 0（不抛异常，辅助台可放心遍历）。</summary>
        public float SuspicionOf(int targetId)
        {
            var state = DetectionRegistry.Find(targetId);
            return state != null ? state.Suspicion : 0f;
        }

        /// <summary>某察觉目标当前档位。未知目标返回 Patrol。</summary>
        public StealthAlertStage StageOf(int targetId)
        {
            var state = DetectionRegistry.Find(targetId);
            return state != null ? (StealthAlertStage)state.Stage : StealthAlertStage.Patrol;
        }

        /// <summary>
        /// 复原初始状态：清空全部察觉累积，并复位视野的 L3 缓存
        /// （否则辅助台会把「回溯预览期间的结论」当成上一帧真实结果复用）。
        /// </summary>
        public void ResetAll()
        {
            var targets = DetectionRegistry.Targets;
            for (var i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null) targets[i].Reset();
            }

            var volumes = VisionRegistry.Volumes;
            for (var i = 0; i < volumes.Count; i++)
            {
                if (volumes[i] != null) volumes[i].ResetLineOfSightCache();
            }

            NotifyChanged();
        }
    }
}
