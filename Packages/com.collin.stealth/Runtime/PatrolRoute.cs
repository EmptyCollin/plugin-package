using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>巡逻路线的循环方式。</summary>
    public enum PatrolLoopMode
    {
        /// <summary>首尾相连循环。</summary>
        Loop = 0,
        /// <summary>走到端点后原路折返（周期 = 去程 + 返程）。</summary>
        PingPong = 1
    }

    /// <summary>
    /// 巡逻路线（共享 Model）。路点存**相对路线根的局部坐标**（DESIGN D-09），
    /// 因此同一资产可跨场景复用，搬移路线只需移动 <see cref="PatrolRouteRoot"/>。
    /// <para>核心承诺：<see cref="EvaluatePose"/> 是**时间的纯函数**——同一 (路线, t) 必得同一位姿，
    /// 不做逐帧积分、不依赖物理与导航。编辑态预览与 Play 态共用这一条求值路径（DESIGN D-01）。</para>
    /// </summary>
    [CreateAssetMenu(fileName = "PatrolRoute", menuName = "Collin/Stealth/Patrol Route", order = 0)]
    public sealed class PatrolRoute : ScriptableObject
    {
        /// <summary>单个路点。<see cref="position"/> 为相对路线根的局部坐标。</summary>
        [Serializable]
        public struct Waypoint
        {
            [Tooltip("相对路线根的局部坐标（不是世界坐标）")]
            public Vector3 position;

            [Tooltip("到达本点后原地停留的秒数")]
            [Min(0f)]
            public float stopDuration;

            [Tooltip("从本点走向下一点的段速度；填 0 或负数表示使用路线默认速度")]
            public float moveSpeed;
        }

        /// <summary>轨迹采样点：等弧长采样，且相邻样本之间的位置随时间**线性**变化，
        /// 故折线插值与精确求值等价（供 B 包按帧预算取用，避免逐点调用求值）。</summary>
        [Serializable]
        public struct TrajectorySample
        {
            public Vector3 localPosition;
            public float localYawDegrees;
            public float time;
        }

        /// <summary>一段行程（含到点停留）。按 <see cref="Leg.start"/> 升序排列，可二分查找。</summary>
        struct Leg
        {
            public int from;
            public int to;
            public float dwell;
            public float travel;
            public float start;
            public float yaw;
        }

        const float Eps = 1e-6f;

        [SerializeField] List<Waypoint> _waypoints = new List<Waypoint>();
        [SerializeField] PatrolLoopMode _loopMode = PatrolLoopMode.Loop;
        [SerializeField, Min(0.01f)] float _defaultSpeed = 1.5f;

        readonly List<Leg> _legs = new List<Leg>();

        float _totalCycle;
        int _layoutHash;
        bool _tableValid;

        // ── 只读访问面 ────────────────────────────────────────────────

        /// <summary>路点数量。</summary>
        public int WaypointCount => _waypoints.Count;

        /// <summary>循环方式。</summary>
        public PatrolLoopMode LoopMode => _loopMode;

        /// <summary>默认段速度（路点未单独指定速度时使用）。</summary>
        public float DefaultSpeed => _defaultSpeed;

        /// <summary>T 周期：从初始状态到再次复原初始状态的最小时间（验收 2/5 的量程真源）。</summary>
        public float TotalCycle
        {
            get
            {
                EnsureTable();
                return _totalCycle;
            }
        }

        /// <summary>按索引取路点局部坐标。</summary>
        public Vector3 GetWaypointPosition(int index)
        {
            return _waypoints[index].position;
        }

        /// <summary>按索引取到点停留秒数。</summary>
        public float GetStopDuration(int index)
        {
            return _waypoints[index].stopDuration;
        }

        /// <summary>按索引取段速度（可能为 0，表示走默认速度）。</summary>
        public float GetSegmentSpeed(int index)
        {
            return _waypoints[index].moveSpeed;
        }

        // ── 编辑写入面（所见即所得：改完即失效表，下一次求值自动重建）──

        /// <summary>设置路点局部坐标。</summary>
        public void SetWaypointPosition(int index, Vector3 localPosition)
        {
            var w = _waypoints[index];
            w.position = localPosition;
            _waypoints[index] = w;
        }

        /// <summary>设置到点停留秒数（负数按 0 处理）。</summary>
        public void SetStopDuration(int index, float seconds)
        {
            var w = _waypoints[index];
            w.stopDuration = Mathf.Max(0f, seconds);
            _waypoints[index] = w;
        }

        /// <summary>设置段速度（填 0 表示沿用默认速度）。</summary>
        public void SetSegmentSpeed(int index, float speed)
        {
            var w = _waypoints[index];
            w.moveSpeed = speed;
            _waypoints[index] = w;
        }

        /// <summary>追加路点，返回其索引。</summary>
        public int AddWaypoint(Vector3 localPosition, float stopDuration = 0f, float moveSpeed = 0f)
        {
            _waypoints.Add(new Waypoint
            {
                position = localPosition,
                stopDuration = Mathf.Max(0f, stopDuration),
                moveSpeed = moveSpeed
            });
            return _waypoints.Count - 1;
        }

        /// <summary>在指定位置插入路点。</summary>
        public void InsertWaypoint(int index, Vector3 localPosition)
        {
            _waypoints.Insert(index, new Waypoint { position = localPosition });
        }

        /// <summary>删除路点；剩余不足 2 个时仍合法（NPC 原地驻守）。</summary>
        public bool RemoveWaypointAt(int index)
        {
            if (index < 0 || index >= _waypoints.Count) return false;
            _waypoints.RemoveAt(index);
            return true;
        }

        /// <summary>设置循环方式。</summary>
        public void SetLoopMode(PatrolLoopMode mode)
        {
            _loopMode = mode;
        }

        /// <summary>设置默认段速度。</summary>
        public void SetDefaultSpeed(float speed)
        {
            _defaultSpeed = Mathf.Max(0.01f, speed);
        }

        // ── 求值 ─────────────────────────────────────────────────────

        /// <summary>
        /// 求 t 时刻的局部位姿。纯函数、确定性、无副作用（表已就绪时不写任何状态）。
        /// </summary>
        /// <param name="t">时间（秒）。允许负数与超出周期的值，内部按周期取模。</param>
        public PatrolPose EvaluatePose(float t)
        {
            EnsureTable();

            var pose = new PatrolPose { cycleTime = _totalCycle, legIndex = -1 };
            if (_legs.Count == 0)
            {
                // 空路线：停在原点。
                return pose;
            }

            var u = Wrap(t);
            var li = FindLegIndex(u);
            var leg = _legs[li];
            var a = _waypoints[leg.from].position;
            var b = _waypoints[leg.to].position;

            pose.legIndex = li;
            pose.localYawDegrees = leg.yaw;

            var dwellEnd = leg.start + leg.dwell;
            if (u < dwellEnd)
            {
                // 处于到点停留阶段。
                pose.localPosition = a;
                pose.isDwelling = true;
                return pose;
            }

            if (leg.travel <= Eps)
            {
                // 零长段（重复路点或单点路线）：直接落在终点。
                pose.localPosition = b;
                pose.legProgress01 = 1f;
                return pose;
            }

            var f = (u - dwellEnd) / leg.travel;
            pose.localPosition = Vector3.Lerp(a, b, f);
            pose.legProgress01 = f;
            return pose;
        }

        /// <summary>
        /// 按等弧长采样整条轨迹（局部坐标）。供分层查看/轨迹绘制使用，
        /// 不必逐帧调用 <see cref="EvaluatePose"/>。
        /// </summary>
        /// <param name="spacingMeters">相邻样本间的弧长间隔（米），最小 0.05。</param>
        /// <param name="output">复用容器，调用前会被清空。</param>
        public void BuildTrajectorySamples(float spacingMeters, List<TrajectorySample> output)
        {
            if (output == null) return;
            output.Clear();
            EnsureTable();
            if (_legs.Count == 0) return;

            var step = Mathf.Max(0.05f, spacingMeters);

            for (var i = 0; i < _legs.Count; i++)
            {
                var leg = _legs[i];
                var a = _waypoints[leg.from].position;
                var b = _waypoints[leg.to].position;
                var entryTime = leg.start + leg.dwell;

                // 段起点（也是上一段终点，故只在首段或折返接缝处去重）。
                if (i == 0 || output.Count == 0 ||
                    (output[output.Count - 1].localPosition - a).sqrMagnitude > Eps)
                {
                    output.Add(new TrajectorySample
                    {
                        localPosition = a,
                        localYawDegrees = leg.yaw,
                        time = entryTime
                    });
                }

                var length = (b - a).magnitude;
                if (length <= Eps) continue;

                var inner = Mathf.FloorToInt(length / step);
                for (var s = 1; s <= inner; s++)
                {
                    var d = step * s;
                    if (d >= length - Eps) break;
                    var f = d / length;
                    output.Add(new TrajectorySample
                    {
                        localPosition = Vector3.Lerp(a, b, f),
                        localYawDegrees = leg.yaw,
                        time = entryTime + leg.travel * f
                    });
                }
            }

            // 末段终点补齐，形成闭环可见的完整折线。
            var lastLeg = _legs[_legs.Count - 1];
            var tail = _waypoints[lastLeg.to].position;
            if ((output[output.Count - 1].localPosition - tail).sqrMagnitude > Eps)
            {
                output.Add(new TrajectorySample
                {
                    localPosition = tail,
                    localYawDegrees = lastLeg.yaw,
                    time = _totalCycle
                });
            }
        }

        // ── 周期换算（公开给编辑面板与 B 包）────────────────────────

        /// <summary>把任意时间折算到 [0, T) 区间。</summary>
        public float WrapTime(float t)
        {
            EnsureTable();
            return Wrap(t);
        }

        float Wrap(float t)
        {
            if (_totalCycle <= Eps) return 0f;
            var u = t % _totalCycle;
            if (u < 0f) u += _totalCycle;
            return u;
        }

        // ── 累计长度表 ───────────────────────────────────────────────

        /// <summary>若布局变化则重建累计表，返回是否发生重建。</summary>
        public bool EnsureTable()
        {
            var h = ComputeLayoutHash();
            if (_tableValid && h == _layoutHash) return false;
            Rebuild(h);
            return true;
        }

        void Rebuild(int hash)
        {
            _legs.Clear();
            _totalCycle = 0f;
            _layoutHash = hash;
            _tableValid = true;

            var n = _waypoints.Count;
            if (n == 0) return;

            var legCount = n == 1 ? 1 : (_loopMode == PatrolLoopMode.Loop ? n : (n - 1) * 2);

            for (var li = 0; li < legCount; li++)
            {
                int from, to;
                ResolveEnds(n, li, out from, out to);

                var pa = _waypoints[from].position;
                var pb = _waypoints[to].position;

                var dwell = Mathf.Max(0f, _waypoints[from].stopDuration);
                var segSpeed = _waypoints[from].moveSpeed;
                var speed = segSpeed > Eps ? segSpeed : Mathf.Max(0.01f, _defaultSpeed);
                var length = (pb - pa).magnitude;
                var travel = length / speed;

                var yaw = float.NaN;
                var dx = pb.x - pa.x;
                var dz = pb.z - pa.z;
                if (dx * dx + dz * dz > Eps)
                {
                    // Atan2 返回弧度，须乘 Rad2Deg 转度数。
                    yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                }

                _legs.Add(new Leg
                {
                    from = from,
                    to = to,
                    dwell = dwell,
                    travel = travel,
                    start = _totalCycle,
                    yaw = yaw
                });

                _totalCycle += dwell + travel;
            }

            // 零水平位移的段（垂直升降/重复点）没有天然朝向，继承邻近段的朝向，避免朝向突变。
            ResolveMissingYaws();
        }

        /// <summary>把段索引换算成 (from, to) 路点索引。</summary>
        void ResolveEnds(int n, int legIndex, out int from, out int to)
        {
            if (n == 1)
            {
                from = 0;
                to = 0;
                return;
            }

            if (_loopMode == PatrolLoopMode.Loop)
            {
                from = legIndex % n;
                to = (legIndex + 1) % n;
                return;
            }

            // PingPong：去程 0→n-1，返程 n-1→0。
            var forwardLegs = n - 1;
            if (legIndex < forwardLegs)
            {
                from = legIndex;
                to = legIndex + 1;
            }
            else
            {
                var back = legIndex - forwardLegs; // 0..n-2
                from = n - 1 - back;
                to = from - 1;
            }
        }

        void ResolveMissingYaws()
        {
            var count = _legs.Count;
            for (var i = 0; i < count; i++)
            {
                if (!float.IsNaN(_legs[i].yaw)) continue;

                var resolved = float.NaN;
                for (var k = 1; k < count; k++)
                {
                    var j = (i + k) % count;
                    if (float.IsNaN(_legs[j].yaw)) continue;
                    resolved = _legs[j].yaw;
                    break;
                }

                if (float.IsNaN(resolved)) resolved = 0f;

                var leg = _legs[i];
                leg.yaw = resolved;
                _legs[i] = leg;
            }
        }

        int FindLegIndex(float u)
        {
            // _legs 按 start 升序，二分定位「start <= u 的最后一段」。
            var lo = 0;
            var hi = _legs.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) >> 1;
                if (_legs[mid].start <= u) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        int ComputeLayoutHash()
        {
            unchecked
            {
                var h = 17;
                h = h * 31 + (int)_loopMode;
                h = h * 31 + Bits(_defaultSpeed);
                for (var i = 0; i < _waypoints.Count; i++)
                {
                    var w = _waypoints[i];
                    h = h * 31 + Bits(w.position.x);
                    h = h * 31 + Bits(w.position.y);
                    h = h * 31 + Bits(w.position.z);
                    h = h * 31 + Bits(w.stopDuration);
                    h = h * 31 + Bits(w.moveSpeed);
                }
                return h;
            }
        }

        static int Bits(float v)
        {
            return v.GetHashCode();
        }

        void OnValidate()
        {
            // Inspector 直接改了序列化字段：作废缓存，下一次求值自动重建。
            _tableValid = false;
        }
    }
}
