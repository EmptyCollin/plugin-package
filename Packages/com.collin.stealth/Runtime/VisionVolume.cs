using Collin.Core.Stealth;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 视野体（实例）：引用共享的 <see cref="VisionProfile"/>，持有<b>实例级差异</b>
    /// （启用开关、视距倍率、视线高度/水平角覆盖）。AGENTS.md §0.1 原则 2。
    /// <para>几何判定全部委托 <see cref="VisionGeometry"/> 纯函数；本组件只负责
    /// ①取观察原点与朝向 ②遮挡层过滤与自身排除 ③射线帧预算与错峰。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VisionVolume : MonoBehaviour
    {
        /// <summary>L3 射线复用缓冲（NonAlloc 要求数组，容量不足时丢弃多余命中即偏保守）。</summary>
        static readonly RaycastHit[] s_rayBuffer = new RaycastHit[64];

        [Tooltip("共享的视野参数资产")]
        [SerializeField] VisionProfile _profile;

        [Tooltip("实例级：是否启用本视野")]
        [SerializeField] bool _enabled = true;

        [Tooltip("实例级：视距倍率（高哨兵可 1.5×）")]
        [SerializeField, Min(0.01f)] float _distanceMultiplier = 1f;

        [Tooltip("实例级：视线高度覆盖（米）。负数表示沿用资产值")]
        [SerializeField] float _eyeHeightOverride = -1f;

        [Tooltip("实例级：水平视野角覆盖（度）。负数表示沿用资产值")]
        [SerializeField] float _horizontalAngleOverride = -1f;

        /// <summary>最近一次 L3 结论缓存；预算耗尽或未到检测帧时复用。</summary>
        bool _lastLosClear = true;
        bool _hasLosCache;

        public VisionProfile Profile
        {
            get => _profile;
            set => _profile = value;
        }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public float DistanceMultiplier
        {
            get => _distanceMultiplier;
            set => _distanceMultiplier = Mathf.Max(0.01f, value);
        }

        /// <summary>生效视线高度（实例覆盖优先，否则资产值）。</summary>
        public float EffectiveEyeHeight => _eyeHeightOverride >= 0f
            ? _eyeHeightOverride
            : (_profile != null ? _profile.EyeHeight : 1.7f);

        /// <summary>生效水平视野角（实例覆盖优先）。</summary>
        public float EffectiveHorizontalAngle => _horizontalAngleOverride >= 0f
            ? _horizontalAngleOverride
            : (_profile != null ? _profile.HorizontalAngle : 110f);

        /// <summary>生效几何参数（含视距倍率）。无资产时视距为 0 ⇒ 恒不可见。</summary>
        public VisionParams Params
        {
            get
            {
                if (_profile == null)
                {
                    return new VisionParams
                    {
                        maxDistance = 0f,
                        horizontalAngleDegrees = EffectiveHorizontalAngle,
                        verticalAngleDegrees = 60f
                    };
                }

                return new VisionParams
                {
                    maxDistance = _profile.MaxDistance * _distanceMultiplier,
                    horizontalAngleDegrees = EffectiveHorizontalAngle,
                    verticalAngleDegrees = _profile.VerticalAngle
                };
            }
        }

        /// <summary>观察原点（世界，含视线高度）。</summary>
        public Vector3 OriginWorld => transform.position + Vector3.up * EffectiveEyeHeight;

        /// <summary>观察朝向（世界，含俯仰）。</summary>
        public Vector3 ForwardWorld => transform.forward;

        /// <summary>遮挡层掩码（无资产为 0 = 无遮挡物）。</summary>
        public int OccluderMaskValue => _profile != null ? _profile.OccluderMaskValue : 0;

        /// <summary>察觉上升速率（每秒）。</summary>
        public float GainPerSecond => _profile != null ? _profile.SuspicionGainPerSecond : 0f;

        /// <summary>光照敏感度。</summary>
        public float LightSensitivity => _profile != null ? _profile.LightSensitivity : 0f;

        /// <summary>视线检测间隔帧数（错峰用）。</summary>
        public int CheckIntervalFrames => _profile != null ? _profile.LineOfSightCheckInterval : 1;

        /// <summary>跨包视野快照（辅助台绘制扇形的数据源）。</summary>
        public StealthVisionInfo ToInfo(int actorId)
        {
            return new StealthVisionInfo
            {
                actorId = actorId,
                originWorld = OriginWorld,
                forwardWorld = ForwardWorld,
                horizontalAngleDegrees = EffectiveHorizontalAngle,
                verticalAngleDegrees = _profile != null ? _profile.VerticalAngle : 60f,
                maxDistance = Params.maxDistance,
                enabled = _enabled && _profile != null
            };
        }

        /// <summary>
        /// 用<b>任意</b>观察原点/朝向判可见 —— 编辑态按 t 时刻 NPC 位姿预演的入口
        /// （<see cref="StealthWorld"/> 拿巡逻求值结果调它）。
        /// </summary>
        /// <param name="targetOwner">
        /// 判定点所属物体（可空）。<b>必须传</b>：判定点通常落在目标脚下/身体上，
        /// 目标自己的碰撞体会在射线末端被命中；不按身份排除就会「哨兵永远看不见玩家」，
        /// 而靠缩短射线绕过又会让贴身的真实掩体被跳过（两个失真互为代价，只能按身份判）。
        /// </param>
        public bool CanSeeFrom(Vector3 origin, Vector3 forward, Vector3 target,
                               QueryOptions options, int actorId, Transform targetOwner = null)
        {
            if (!_enabled || _profile == null) return false;

            // 同步写入、同步读取（VisionGeometry 内联调用委托），不存在跨调用竞态。
            _targetOwner = targetOwner;

            var p = Params;
            var needLos = (options & QueryOptions.LineOfSight) != 0;

            if (!needLos)
            {
                return VisionGeometry.Evaluate(origin, forward, target, p, options, null);
            }

            // 未到本单位的检测帧：复用上帧 L3 结论，其余三层仍按当前几何重算。
            // 分支内各自显式绑定方法组：三元里放两个方法组编译器推不出委托类型。
            if (StealthPerformance.ShouldCheckThisFrame(actorId, CheckIntervalFrames) || !_hasLosCache)
            {
                return VisionGeometry.Evaluate(origin, forward, target, p, options, RaycastClear);
            }

            return VisionGeometry.Evaluate(origin, forward, target, p, options, CachedClear);
        }

        /// <summary>用本组件<b>当前</b>位姿判可见。</summary>
        public bool CanSee(Vector3 target, QueryOptions options, int actorId, Transform targetOwner = null)
        {
            return CanSeeFrom(OriginWorld, ForwardWorld, target, options, actorId, targetOwner);
        }

        /// <summary>
        /// 本次查询的目标所属物体（用于排除其自身碰撞体）。
        /// 纯运行时状态，<b>不得序列化</b>（写进场景文件会留下无意义的持久引用）。
        /// </summary>
        Transform _targetOwner;

        /// <summary>复用上帧 L3 结论（不发射线）。</summary>
        bool CachedClear(Vector3 origin, Vector3 target) => _lastLosClear;

        /// <summary>
        /// L3 覆盖注入点（测试与设计工具用）。为 null 时走真实物理射线。
        /// <para>存在理由：帧预算耗尽「复用上帧结论」与「遮挡翻转」这两条分支，
        /// 依赖物理场景里有无可命中碰撞体，在 EditMode 里不可靠；
        /// 注入后可以让判定管线在零物理场景下做确定性断言。</para>
        /// </summary>
        public static System.Func<VisionVolume, Vector3, Vector3, bool> LineOfSightOverride;

        /// <summary>
        /// L3 视线：遮挡层内取最近命中，排除自身与其子节点；
        /// 帧预算耗尽时复用上帧结论（无缓存则按无遮挡放行，因 L1/L2 已过）。
        /// </summary>
        bool RaycastClear(Vector3 origin, Vector3 target)
        {
            var delta = target - origin;
            var length = delta.magnitude;
            if (length <= 1e-5f)
            {
                _lastLosClear = true;
                _hasLosCache = true;
                return true;
            }

            var mask = OccluderMaskValue;
            var needsRay = mask != 0 || LineOfSightOverride != null;

            // 未配置遮挡层且无注入：几何上无遮挡物，直接放行（不消耗预算）。
            if (!needsRay)
            {
                _lastLosClear = true;
                _hasLosCache = true;
                return true;
            }

            bool clear;
            if (StealthPerformance.TryAcquireRaycast())
            {
                // 只留极小的终点余量（≤0.1m）：射线必须几乎打满全长，
                // 否则「玩家紧贴掩体」时掩体会被余量跳过 ⇒ 隔着墙也看得见（玩法直接失真）。
                // 「目标自己的身体挡住自己」不由余量解决，而由 PhysicallyClear 按身份排除。
                var reach = length - Mathf.Min(0.1f, length * 0.02f);
                clear = LineOfSightOverride != null
                    ? LineOfSightOverride(this, origin, target)
                    : PhysicallyClear(origin, delta / length, reach, mask);
            }
            else
            {
                // 预算耗尽：沿用上一结论。绝不因为「没排到队」就判成看不见——
                // 那会让辅助台的热力图随帧率抖动，把性能问题伪装成玩法问题。
                clear = _hasLosCache ? _lastLosClear : true;
            }

            _lastLosClear = clear;
            _hasLosCache = true;
            return clear;
        }

        /// <summary>真实物理射线：命中且不是自身/子节点、且早于目标距离，才算遮挡。</summary>
        bool PhysicallyClear(Vector3 origin, Vector3 direction, float length, int mask)
        {
            var count = Physics.RaycastNonAlloc(origin, direction, s_rayBuffer, length, mask);
            for (var i = 0; i < count; i++)
            {
                var hit = s_rayBuffer[i];
                if (hit.distance >= length - 1e-4f) continue;   // 命中在目标处/之后，不算遮挡
                var t = hit.transform;
                if (t == null) continue;
                if (t == transform || t.IsChildOf(transform)) continue;  // 自身身体不算遮挡
                // 目标自己的碰撞体不算遮挡：判定点就打在目标身上，
                // 不排身份的话「哨兵永远看不见玩家」；缩短射线绕过又会漏掉贴身掩体。
                if (_targetOwner != null && (t == _targetOwner || t.IsChildOf(_targetOwner))) continue;
                return false;
            }
            return true;
        }

        /// <summary>复位 L3 缓存（单位被禁用/重建时调用，避免读到上一世的结论）。</summary>
        public void ResetLineOfSightCache()
        {
            _lastLosClear = true;
            _hasLosCache = false;
        }

        void OnEnable()
        {
            ResetLineOfSightCache();
            VisionRegistry.MarkDirty();
        }

        void OnDisable()
        {
            VisionRegistry.MarkDirty();
        }

        void OnValidate()
        {
            _distanceMultiplier = Mathf.Max(0.01f, _distanceMultiplier);
        }

        void OnDrawGizmosSelected()
        {
            // 选中时画：场景里几十个哨兵全画会糊成一片，选中绘制兼顾直观与性能。
            StealthGizmos.DrawVision(this);
        }
    }
}
