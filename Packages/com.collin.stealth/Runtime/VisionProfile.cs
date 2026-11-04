using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 视野与察觉参数（共享 Model，ScriptableObject）。
    /// <para>可被 N 个 <see cref="VisionVolume"/> 复用；<b>实例间差异</b>（视距倍率、启用开关等）
    /// 一律放在 <see cref="VisionVolume"/> 的序列化字段上，不得写进本资产
    /// （AGENTS.md §0.1 原则 2）。</para>
    /// </summary>
    [CreateAssetMenu(fileName = "VisionProfile", menuName = "Collin/Stealth/Vision Profile", order = 1)]
    public sealed class VisionProfile : ScriptableObject
    {
        [Header("几何视野（L2）")]
        [Tooltip("水平视野角，单位度（全角，不是半角）")]
        [Range(1f, 360f)]
        [SerializeField] float _horizontalAngle = 110f;

        [Tooltip("垂直视野角，单位度（全角）。以视线水平方向为基准上下各一半")]
        [Range(1f, 180f)]
        [SerializeField] float _verticalAngle = 60f;

        [Tooltip("最大视距，单位米")]
        [Min(0.01f)]
        [SerializeField] float _maxDistance = 18f;

        [Tooltip("视线起点相对 NPC 脚底的高度，单位米")]
        [Min(0f)]
        [SerializeField] float _eyeHeight = 1.7f;

        [Header("遮挡（L3）")]
        [Tooltip("哪些层会挡住视线。留空表示无遮挡物（纯几何可见）")]
        [SerializeField] LayerMask _occluderMask = ~0;

        [Header("察觉累积（L4 + 状态）")]
        [Tooltip("完全可见时察觉度上升速率，单位每秒。察觉度取值 0~1")]
        [Min(0f)]
        [SerializeField] float _suspicionGainPerSecond = 0.5f;

        [Tooltip("不可见时察觉度回落速率，单位每秒")]
        [Min(0f)]
        [SerializeField] float _decayPerSecond = 0.25f;

        [Tooltip("越过该阈值进入「疑惑」档")]
        [Range(0f, 1f)]
        [SerializeField] float _suspicionThreshold = 0.35f;

        [Tooltip("越过该阈值进入「警觉」档")]
        [Range(0f, 1f)]
        [SerializeField] float _alertThreshold = 0.75f;

        [Tooltip("光照敏感度：0=不看光照，1=暗处几乎看不见。按目标的曝光度 0~1 缩放察觉速率")]
        [Range(0f, 1f)]
        [SerializeField] float _lightSensitivity = 0.6f;

        [Tooltip("视线检测间隔帧数：1=每帧查。>1 时其余帧复用上次的结果（性能预算，见 StealthPerformance）")]
        [Min(1)]
        [SerializeField] int _lineOfSightCheckInterval = 1;

        [Header("近身感知")]
        [Tooltip("近身半径（米）：目标进入此距离即视为<b>贴脸察觉</b>，不再受视角锥约束。\n"
                 + "没有这一档时，NPC 与玩家重叠反而『看不见』——目标几乎在视线正下方，"
                 + "超出俯仰半角就被判瞎（实机反馈到的 bug）。")]
        [SerializeField, Range(0f, 10f)] float _proximityRadius = 1.8f;

        // ── 只读面 ──────────────────────────────────────────────────

        /// <summary>水平视野角（度，全角）。</summary>
        public float HorizontalAngle => _horizontalAngle;

        /// <summary>垂直视野角（度，全角）。</summary>
        public float VerticalAngle => _verticalAngle;

        /// <summary>最大视距（米）。</summary>
        public float MaxDistance => _maxDistance;

        /// <summary>视线高度（米，相对 NPC 脚底）。</summary>
        public float EyeHeight => _eyeHeight;

        /// <summary>遮挡层掩码值。</summary>
        public int OccluderMaskValue => _occluderMask.value;

        /// <summary>完全可见时的察觉上升速率（每秒）。</summary>
        public float SuspicionGainPerSecond => _suspicionGainPerSecond;

        /// <summary>不可见时的回落速率（每秒）。</summary>
        public float DecayPerSecond => _decayPerSecond;

        /// <summary>疑惑阈值。</summary>
        public float SuspicionThreshold => _suspicionThreshold;

        /// <summary>警觉阈值。</summary>
        public float AlertThreshold => _alertThreshold;

        /// <summary>光照敏感度。</summary>
        public float LightSensitivity => _lightSensitivity;

        /// <summary>视线检测间隔帧数（至少 1）。</summary>
        public int LineOfSightCheckInterval => _lineOfSightCheckInterval < 1 ? 1 : _lineOfSightCheckInterval;

        /// <summary>近身半径（米）：目标进入此距离即贴脸察觉，不受视角锥约束。0 = 关闭。</summary>
        public float ProximityRadius => _proximityRadius;

        // ── 写入面（Controller 专用：Editor 面板与跨包命令经此写 Model，见 §0.1 原则 2）──

        /// <summary>设水平视野角（度，全角），自动钳制到 1~360。</summary>
        public void SetHorizontalAngle(float degrees)
        {
            _horizontalAngle = Mathf.Clamp(degrees, 1f, 360f);
        }

        /// <summary>设垂直视野角（度，全角），自动钳制到 1~180。</summary>
        public void SetVerticalAngle(float degrees)
        {
            _verticalAngle = Mathf.Clamp(degrees, 1f, 180f);
        }

        /// <summary>设最大视距（米，最小 0.01）。</summary>
        public void SetMaxDistance(float meters)
        {
            _maxDistance = Mathf.Max(0.01f, meters);
        }

        /// <summary>设视线高度（米，不小于 0）。</summary>
        public void SetEyeHeight(float meters)
        {
            _eyeHeight = Mathf.Max(0f, meters);
        }

        /// <summary>设遮挡层掩码。</summary>
        public void SetOccluderMask(int maskValue)
        {
            _occluderMask = new LayerMask { value = maskValue };
        }

        /// <summary>设察觉上升速率（每秒，不小于 0）。</summary>
        public void SetSuspicionGain(float perSecond)
        {
            _suspicionGainPerSecond = Mathf.Max(0f, perSecond);
        }

        /// <summary>设回落速率（每秒，不小于 0）。</summary>
        public void SetDecayPerSecond(float perSecond)
        {
            _decayPerSecond = Mathf.Max(0f, perSecond);
        }

        /// <summary>设警觉阈值并保证疑惑阈值不倒挂（倒挂会让状态机永远停在疑惑）。</summary>
        public void SetAlertThreshold(float value)
        {
            _alertThreshold = Mathf.Clamp01(value);
            _suspicionThreshold = Mathf.Min(_suspicionThreshold, _alertThreshold);
        }

        /// <summary>设疑惑阈值（自动不超过警觉阈值）。</summary>
        public void SetSuspicionThreshold(float value)
        {
            _suspicionThreshold = Mathf.Min(Mathf.Clamp01(value), _alertThreshold);
        }

        /// <summary>设光照敏感度 [0,1]。</summary>
        public void SetLightSensitivity(float value)
        {
            _lightSensitivity = Mathf.Clamp01(value);
        }

        /// <summary>设视线检测间隔帧数（至少 1）。</summary>
        public void SetLineOfSightCheckInterval(int frames)
        {
            _lineOfSightCheckInterval = frames < 1 ? 1 : frames;
        }

        /// <summary>设近身半径（米，钳制 0~10；0 = 关闭贴脸察觉）。</summary>
        public void SetProximityRadius(float meters)
        {
            _proximityRadius = Mathf.Clamp(meters, 0f, 10f);
        }

        /// <summary>打包察觉阈值与回落速率，供跨帧累加使用。</summary>
        public AwarenessTuning ToTuning()
        {
            return new AwarenessTuning
            {
                decayPerSecond = _decayPerSecond,
                suspicionThreshold = _suspicionThreshold,
                alertThreshold = _alertThreshold
            };
        }

        void OnValidate()
        {
            // Inspector 直接改序列化字段时的兜底：避免出现 0 视距或倒挂阈值这类静默错乱。
            _horizontalAngle = Mathf.Clamp(_horizontalAngle, 1f, 360f);
            _verticalAngle = Mathf.Clamp(_verticalAngle, 1f, 180f);
            _maxDistance = Mathf.Max(0.01f, _maxDistance);
            _eyeHeight = Mathf.Max(0f, _eyeHeight);
            _suspicionGainPerSecond = Mathf.Max(0f, _suspicionGainPerSecond);
            _decayPerSecond = Mathf.Max(0f, _decayPerSecond);
            _alertThreshold = Mathf.Clamp(_alertThreshold, 0f, 1f);
            _suspicionThreshold = Mathf.Min(Mathf.Clamp(_suspicionThreshold, 0f, 1f), _alertThreshold);
            _lightSensitivity = Mathf.Clamp01(_lightSensitivity);
            if (_lineOfSightCheckInterval < 1) _lineOfSightCheckInterval = 1;
        }
    }
}
