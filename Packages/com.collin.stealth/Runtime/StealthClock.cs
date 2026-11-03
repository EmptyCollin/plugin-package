using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>时间索引的来源模式。</summary>
    public enum StealthClockMode
    {
        /// <summary>每帧按 deltaTime 自增（Play 态默认）。</summary>
        Auto = 0,
        /// <summary>由外部写入 t（B 包的编辑态 T 周期回溯、以及 EditMode 测试走这条）。</summary>
        Manual = 1
    }

    /// <summary>
    /// 潜行系统的全局时间索引。<see cref="Current"/> 是喂给
    /// <see cref="PatrolRoute.EvaluatePose"/> 的唯一时间源，
    /// 使「编辑态预览」与「Play 态运行」可以共用同一条求值路径（DESIGN D-01）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StealthClock : MonoBehaviour
    {
        [SerializeField] StealthClockMode _mode = StealthClockMode.Auto;
        [SerializeField, Min(0f)] float _timeScale = 1f;
        [SerializeField] float _manualTime;

        static StealthClock _instance;

        /// <summary>
        /// 场景中唯一的时钟（无场景时钟时为 null）。
        /// <para>编辑态（非 Play）下 <c>Awake/OnEnable</c> 不会被调用，因此这里带一次兜底查找：
        /// B 包的「T 周期滑动条回溯」必须在编辑态拿到时钟，否则整条预览链路失效（DESIGN D-01）。</para>
        /// </summary>
        public static StealthClock Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = UnityEngine.Object.FindFirstObjectByType<StealthClock>();
                return _instance;
            }
        }

        /// <summary>当前时间索引（秒）。Manual 模式返回外部写入值。</summary>
        public float Current => _mode == StealthClockMode.Manual ? _manualTime : _autoTime;

        /// <summary>时钟模式。</summary>
        public StealthClockMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        /// <summary>全局时间缩放（暂停/慢放调试用）。</summary>
        public float TimeScale
        {
            get => _timeScale;
            set => _timeScale = Mathf.Max(0f, value);
        }

        /// <summary>Manual 模式下的写入面：外部（B 包滑动条 / 测试）驱动 t。</summary>
        public float ManualTime
        {
            get => _manualTime;
            set => _manualTime = Mathf.Max(0f, value);
        }

        /// <summary>把 Auto 模式的累计时间设为指定值（状态机重置与测试用）。</summary>
        public void SetAutoTime(float t)
        {
            _autoTime = Mathf.Max(0f, t);
        }

        float _autoTime;

        void Awake()
        {
            _instance = this;
        }

        void OnEnable()
        {
            _instance = this;
        }

        void OnDisable()
        {
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            // 射线预算必须随帧复位：StealthPerformance 刻意不自己读 Time.frameCount
            //（编辑态也有编辑器帧在递增，会让「这帧还能不能发射线」变成不可控的隐式状态）。
            // 时钟是本包唯一的每帧驱动点，所以由它报帧号；否则预算只增不减，
            // 累积到上限后所有视线检测会永久复用上帧结论 —— 判定实质失效。
            StealthPerformance.BeginFrame(Time.frameCount);

            if (_mode != StealthClockMode.Auto) return;
            _autoTime += Time.deltaTime * _timeScale;
        }
    }
}
