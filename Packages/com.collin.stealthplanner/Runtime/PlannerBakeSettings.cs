using UnityEngine;

namespace Collin.StealthPlanner
{
    /// <summary>
    /// 烘焙与求解的<b>可配置参数集</b>（DESIGN D-13 / D-16 的落地载体）。
    /// <para>为什么本包骨架阶段就要有这个类：决议 5 明确「格长 / Δt / 相位窗三项必须面板可配」，
    /// 参数归属一经拍板就成了对外契约的一部分；M1 的分帧烘焙器与 M3 的相位图求解都以本资产为输入。
    /// 空程序集会被 Unity 判为「无脚本关联」而持续打 warning（实测），故骨架不允许只有 asmdef。</para>
    /// <para><b>纪律</b>：这是 Model（共享配置，进 ScriptableObject），
    /// 本类<b>不含任何可见性判定或求解逻辑</b>——判定唯一来源是玩法包经 core 注册的
    /// <c>IVisionWorld</c>（DESIGN §4 红线），复制第二套判定必然漂移。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/Stealth Planner/烘焙设置", fileName = "NewStealthPlannerBakeSettings")]
    public sealed class PlannerBakeSettings : ScriptableObject
    {
        [Header("空间离散化（D-16：默认 0.5m，可配）")]
        [Tooltip("网格边长（米）。体积按 1/格长² 增长，缩小前先看看板上的估算（DESIGN §6.8）")]
        [SerializeField, Min(0.05f)] float _cellSize = 0.5f;

        [Header("时间离散化（D-16：默认 0.5s，可配）")]
        [Tooltip("时间桶 Δt（秒）。烘焙射线次数按 1/Δt 线性增长")]
        [SerializeField, Min(0.05f)] float _timeStep = 0.5f;

        [Header("相位窗（D-13：用户手配，参考值只读展示、不回写本字段）")]
        [Tooltip("宏观相位窗 P（秒）。由策划手填；面板另给「建议参考时长」，但绝不自动改写此值")]
        [SerializeField, Min(1f)] float _phaseWindow = 60f;

        [Tooltip("相位轮数上限 N（静态设计 2 的可行性封顶）。真正决定是否漏解的是本值，不是 P")]
        [SerializeField, Range(1, 64)] int _maxPhases = 4;

        [Header("输出规模")]
        [Tooltip("有解时最多输出前 M 条短解（k-shortest，DESIGN §6.3）")]
        [SerializeField, Range(1, 16)] int _maxSolutions = 5;

        [Tooltip("无解时输出最近撤离点的 Top-K 可藏点（DESIGN §6.4 / 静态设计 3）")]
        [SerializeField, Range(1, 16)] int _fallbackTopK = 5;

        /// <summary>网格边长（米）。</summary>
        public float CellSize => _cellSize;
        /// <summary>时间桶 Δt（秒）。</summary>
        public float TimeStep => _timeStep;
        /// <summary>宏观相位窗 P（秒）。</summary>
        public float PhaseWindow => _phaseWindow;
        /// <summary>相位轮数上限 N。</summary>
        public int MaxPhases => _maxPhases;
        /// <summary>短解输出上限 M。</summary>
        public int MaxSolutions => _maxSolutions;
        /// <summary>无解回退的 Top-K。</summary>
        public int FallbackTopK => _fallbackTopK;

        /// <summary>
        /// 相位窗内的时间桶数（供估算与越界检查用）。
        /// 用 <c>CeilToInt</c> 而非取整：最后一个不完整桶也要参与查询，否则末尾会漏一段时间。
        /// </summary>
        public int BucketsPerPhase => Mathf.Max(1, Mathf.CeilToInt(_phaseWindow / _timeStep));

        /// <summary>
        /// 参考相位时长的桶数上限（DESIGN §6.7 的兜底阈值）。
        /// 7200 桶在默认 Δt=0.5s 下即 1 小时；超过则说明各 NPC 周期互质、严格对齐无实用价值。
        /// </summary>
        public const int MaxAlignedBuckets = 7200;

        /// <summary>
        /// 相位窗 P 的**唯一公开写入口**（§0.1 原则2：Controller 只写 Model）。
        /// <para>存在的理由：D-13 要求面板「一键填入」必须由**用户主动点击**触发，
        /// 而 Editor 程序集碰不到私有的 <c>_phaseWindow</c>。留一条反射/SO 字符串写路径
        /// 等于把字段名变成跨程序集契约（改名即静默失效，本包已因此吃过 JsonUtility 补零的亏）。</para>
        /// <para>纪律同 <see cref="OnValidate"/>：**只钳自身、不做隐式联动** —— 填 P 不会顺手改 Δt 或格长。</para>
        /// </summary>
        public void SetPhaseWindow(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return;   // 非法输入直接忽略，不写脏值
            _phaseWindow = Mathf.Max(1f, seconds);
        }

        void OnValidate()
        {
            // 只在越界时钳制，不做隐式联动（所见即所得：改了 Δt 不该偷偷改 P）
            _cellSize = Mathf.Max(0.05f, _cellSize);
            _timeStep = Mathf.Max(0.05f, _timeStep);
            _phaseWindow = Mathf.Max(1f, _phaseWindow);
            _maxPhases = Mathf.Clamp(_maxPhases, 1, 64);
            _maxSolutions = Mathf.Clamp(_maxSolutions, 1, 16);
            _fallbackTopK = Mathf.Clamp(_fallbackTopK, 1, 16);
        }
    }
}
