using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 巡逻 NPC：引用一个 <see cref="PatrolRouteRoot"/>，每帧按
    /// <see cref="StealthClock"/> 的时间索引求值<b>基线位姿</b>，再叠加
    /// <see cref="IPatrolModifier"/> 的反应偏移后写 transform。
    /// <para>本组件持有<b>实例级差异数据</b>（相位偏移、速度倍数、朝向跟随速度），
    /// 共享结构一律留在 <see cref="PatrolRoute"/> 资产里（AGENTS.md §0.1 原则 2：
    /// 同一配置资产被 N 个实例复用时，实例间差异禁止存在配置里）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PatrolAgent : MonoBehaviour
    {
        [Tooltip("路线根（提供资产与局部→世界换算）")]
        [SerializeField] PatrolRouteRoot _routeRoot;

        [Tooltip("相位偏移（秒）：让复用同一路线的多个 NPC 错开位置")]
        [SerializeField] float _phaseOffset;

        [Tooltip("速度倍数：>1 更快走完同一条路线（等效于时间轴缩放，T 周期随之缩短）")]
        [SerializeField, Min(0.01f)] float _speedMultiplier = 1f;

        [Tooltip("是否由本组件自动把求值结果写到自身 transform（关闭后仅做求值，供预览/工具消费）")]
        [SerializeField] bool _applyToTransform = true;

        [Tooltip("朝向跟随路线朝向的缓动速度（度/秒）。位置保持精确折线，仅朝向缓动（DESIGN D-11）")]
        [SerializeField, Min(1f)] float _yawFollowSpeed = 240f;

        readonly List<IPatrolModifier> _modifiers = new List<IPatrolModifier>(2);
        readonly List<PatrolRoute.TrajectorySample> _sampleBuffer = new List<PatrolRoute.TrajectorySample>();

        float _currentYaw;
        bool _yawInitialized;

        /// <summary>所引用的路线根。</summary>
        public PatrolRouteRoot RouteRoot
        {
            get => _routeRoot;
            set => _routeRoot = value;
        }

        /// <summary>相位偏移（秒）。</summary>
        public float PhaseOffset
        {
            get => _phaseOffset;
            set => _phaseOffset = value;
        }

        /// <summary>速度倍数。</summary>
        public float SpeedMultiplier
        {
            get => _speedMultiplier;
            set => _speedMultiplier = Mathf.Max(0.01f, value);
        }

        /// <summary>是否自动写 transform。</summary>
        public bool ApplyToTransform
        {
            get => _applyToTransform;
            set => _applyToTransform = value;
        }

        /// <summary>实例有效 T 周期（考虑速度倍数）。</summary>
        public float EffectiveCycle
        {
            get
            {
                if (_routeRoot == null || _routeRoot.Route == null) return 0f;
                return _routeRoot.Route.TotalCycle / Mathf.Max(0.01f, _speedMultiplier);
            }
        }

        /// <summary>
        /// 求值指定**全局时间**下的局部位姿（纯函数：同一 (路线, 时间, 实例参数) 必得同一结果）。
        /// </summary>
        public PatrolPose EvaluateLocal(float globalTime)
        {
            if (_routeRoot == null || _routeRoot.Route == null)
            {
                return new PatrolPose { legIndex = -1 };
            }

            var t = (globalTime + _phaseOffset) * _speedMultiplier;
            return _routeRoot.Route.EvaluatePose(t);
        }

        /// <summary>求值指定全局时间下的世界位姿。</summary>
        public PatrolPoseWorld EvaluateWorld(float globalTime)
        {
            var local = EvaluateLocal(globalTime);
            if (_routeRoot == null)
            {
                return new PatrolPoseWorld
                {
                    position = transform.position,
                    rotation = transform.rotation,
                    legIndex = local.legIndex,
                    cycleTime = local.cycleTime
                };
            }
            return _routeRoot.ToWorld(local);
        }

        /// <summary>
        /// 把位姿应用到自身 transform：位置**硬贴**（保证轨迹精确），朝向单独缓动。
        /// 用 <paramref name="dt"/> 为 0 时（编辑态单帧预览）朝向直接对齐，避免缓动残留。
        /// </summary>
        public void ApplyPose(in PatrolPoseWorld pose, float dt)
        {
            transform.position = pose.position;

            var targetYaw = pose.rotation.eulerAngles.y;
            if (!_yawInitialized || dt <= 0f)
            {
                _currentYaw = targetYaw;
                _yawInitialized = true;
            }
            else
            {
                _currentYaw = Mathf.Repeat(Approach(_currentYaw, targetYaw, _yawFollowSpeed * dt), 360f);
            }

            transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
        }

        static float Approach(float current, float target, float maxDelta)
        {
            var delta = Mathf.DeltaAngle(current, target);
            if (Mathf.Abs(delta) <= maxDelta) return target;
            return current + Mathf.Sign(delta) * maxDelta;
        }

        /// <summary>当前求值到的世界位姿（上一帧结果，供 Gizmos 与外部工具读取）。</summary>
        public PatrolPoseWorld LastWorldPose { get; private set; }

        /// <summary>等弧长取整条路线的世界采样（复用路线口径，不重复实现求值）。</summary>
        public IReadOnlyList<PatrolRoute.TrajectorySample> BuildWorldTrajectory(float spacingMeters)
        {
            if (_routeRoot == null) return _sampleBuffer;
            var src = _routeRoot.BuildWorldTrajectory(spacingMeters);
            _sampleBuffer.Clear();
            _sampleBuffer.AddRange(src);
            return _sampleBuffer;
        }

        /// <summary>
        /// 统一的位姿推进入口（本组件唯一的 transform 写入点）。
        /// <para><c>dt &gt; 0</c>：先让叠加器决策，再把叠加偏移合成进基线并应用；</para>
        /// <para><c>dt &lt;= 0</c>（编辑态预览 / scrub）：叠加器只刷新感知读数，
        /// <b>位姿取纯基线、不含任何叠加</b>——预览显示的是巡逻本身，而不是「上一局玩过的痕迹」。</para>
        /// </summary>
        public void ApplyBaselineAt(float globalTime, float dt)
        {
            var pose = EvaluateWorld(globalTime);
            LastWorldPose = pose;

            if (dt > 0f && _modifiers != null && _modifiers.Count > 0)
            {
                for (var i = 0; i < _modifiers.Count; i++)
                {
                    var m = _modifiers[i];
                    if (m == null) continue;
                    m.Tick(dt, pose.position, globalTime);
                }

                // 多个叠加器的偏移求和；注视点取最后一个要求者（本包目前只有一个决策器）。
                var total = Vector3.zero;
                Vector3? look = null;
                for (var i = 0; i < _modifiers.Count; i++)
                {
                    var m = _modifiers[i];
                    if (m == null) continue;
                    m.GetOverlay(out var off, out var lk);
                    total += off;
                    if (lk.HasValue) look = lk;
                }

                if (total.sqrMagnitude > 1e-8f)
                {
                    pose.position += total;
                    pose.rotation = look.HasValue
                        ? LookAtRotation(pose.position, look.Value)
                        : pose.rotation;
                }
            }

            if (_applyToTransform) ApplyPose(pose, dt);
        }

        static Quaternion LookAtRotation(Vector3 from, Vector3 to)
        {
            var dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude <= 1e-8f) return Quaternion.Euler(0f, 0f, 0f);
            return Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
        }

        /// <summary>注册叠加器（由决策组件在 OnEnable 调用）。幂等。</summary>
        public void AddModifier(IPatrolModifier modifier)
        {
            if (modifier == null || _modifiers.Contains(modifier)) return;
            _modifiers.Add(modifier);
        }

        /// <summary>注销叠加器。移除后偏移立即失效（决策组件禁用时不应残留拉扯）。</summary>
        public void RemoveModifier(IPatrolModifier modifier)
        {
            if (modifier == null) return;
            _modifiers.Remove(modifier);
        }

        void Awake()
        {
            if (_routeRoot == null) _routeRoot = GetComponent<PatrolRouteRoot>();
        }

        void LateUpdate()
        {
            var clock = StealthClock.Instance;
            if (clock == null) return;
            ApplyBaselineAt(clock.Current, Time.deltaTime);
        }

        void OnValidate()
        {
            _speedMultiplier = Mathf.Max(0.01f, _speedMultiplier);
            _yawFollowSpeed = Mathf.Max(1f, _yawFollowSpeed);
        }
    }
}
