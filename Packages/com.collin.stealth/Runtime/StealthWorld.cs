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
    /// 因此<b>这里注册得越少越安全</b>——M2 视野层、M3 察觉层未完成前，
    /// <see cref="IVisionWorld"/> 与 <see cref="IDetectionWorld"/> 故意不注册。</para>
    ///
    /// <para><b>actorId 的口径</b>：取 <see cref="PatrolAgent"/> 所在 GameObject 的
    /// <c>instanceId</c>。它在同一场景同一时刻内唯一、且路线重排不会改变，
    /// 但域重载（进出 Play、脚本重编译）后会变。契约注释已声明消费方不得持久化它。</para>
    ///
    /// <para><b>编辑态回溯的实现方式</b>：编辑器非 Play 状态下不存在帧循环，
    /// <see cref="PatrolAgent"/> 的 LateUpdate 不会跑，所以 <see cref="ScrubTo"/> 必须
    /// <b>显式遍历单位并推送位姿</b>，而不是等帧驱动。这是 B 包「T 周期滑动条实时查看
    /// NPC 位置」能成立的关键（DESIGN D-01 / 验收 15 同源）。</para>
    /// </summary>
    public sealed class StealthWorld : IPatrolWorld, IStealthScrubDriver
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

                // Unity 6：GetInstanceID() 已废弃为 error（CS0619），EntityId 隐式转 int 同样废弃，
                // 故取哈希作字典键；域内稳定，跨域重载会变（契约已声明消费方不得持久化）。
                var id = agent.gameObject.GetEntityId().GetHashCode();
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
    }
}
