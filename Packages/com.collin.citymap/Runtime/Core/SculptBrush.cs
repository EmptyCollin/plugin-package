using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>雕刻笔刷的动作（01 §5.1 操作表的"输入"列）。</summary>
    public enum SculptAction { Raise = 0, Lower = 1, FlattenToCenter = 2, FlattenToValue = 3, Smooth = 4, Invert = 5, Clear = 6 }

    /// <summary>
    /// 山水笔刷的<b>数学内核</b>（Modules/01 §5）。放在 Runtime 且为纯函数/无 UnityEditor 引用，
    /// 目的是让 A-1-5（60Hz 与 144Hz 机器产生相同 delta）能被 EditMode 直接断言——
    /// 若把积分写进 EditorTool 的 OnSceneGUI，帧率无关性就只能靠人肉试。
    /// <para>三条硬约定：</para>
    /// <list type="number">
    /// <item><b>固定步长积分</b>（D-01-3）：增量 = <c>strength · 笔速 · 固定步数</c>，
    /// <b>绝不乘帧时</b>。否则"高帧率机器抬得慢、低帧率抬得快"，跨机器不可复现。</item>
    /// <item><b>笔速用世界位移而非屏幕位移</b>：屏幕 DPI/缩放会改变"同样的手速产生的强度"。</item>
    /// <item><b>单笔划影响格数上限</b>（§5.4）：超出即截断并回报，不静默吃掉性能
    /// （超大笔刷一次改全城会让 Preview 掉帧，用户反而以为卡死）。</item>
    /// </list>
    /// </summary>
    public static class SculptBrush
    {
        /// <summary>固定积分步长（米）：每前进这么远算一格增量。与帧率无关的唯一时间尺度。</summary>
        public const float StepDistanceM = 0.5f;

        /// <summary>每步的强度系数（1m 位移 = 一个单位）。</summary>
        public const float StrengthPerMeter = 1f;

        /// <summary>
        /// 衰减核（00 §3.3 唯一求值器，与平整约束 <c>PlaneConstraint.WeightAt</c> 同口径）。
        /// <para>语义：<c>dist ≤ core</c> 全强 → 核心到 <c>radius</c> 之间按 EaseOut 衰减到 0 → 之外为 0。
        /// <c>core</c> 传 0 即"从中心就开始衰减"（软笔刷）。<b>半径 ≤0 返回 0 而不是除零</b>
        /// ——面板滑条允许拖到 0。</para>
        /// </summary>
        public static float FalloffAt(float distMeters, float radiusMeters, float coreMeters = 0f)
        {
            if (radiusMeters <= 1e-4f) return 0f;
            // 非法配置（core<0 或 core>radius，手柄拖拽期间会出现）先钳进合法域：
            // 不钳制会让"核心内全强"分支吞掉半径截止，画出比笔刷还大的影响区。
            var core = Mathf.Min(Mathf.Max(0f, coreMeters), radiusMeters);
            if (distMeters <= core) return 1f;
            // core == radius 时不做除零冒险：核心外即硬截止
            if (radiusMeters - core <= 1e-4f) return 0f;
            var t = (distMeters - core) / (radiusMeters - core);
            // Falloff.Evaluate 内部已 Clamp01 ⇒ 半径外 t>1 也归到 0，不需要额外分支
            return 1f - Falloff.Evaluate(Falloff.Preset.EaseOut, t);
        }

        /// <summary>
        /// 一次积分步的增量（Raise/Lower 共用，符号由调用方按 action 决定）。
        /// <paramref name="stepDistanceM"/> 必须是<b>笔刷世界位移</b>（不是屏幕位移、不是帧时），
        /// 且取自 <see cref="AccumulateSteps"/> 的固定步长——这是 A-1-5 帧率无关的唯一保证。
        /// </summary>
        public static float DeltaFor(float stepDistanceM, float strength, float falloff)
            => stepDistanceM * StrengthPerMeter * strength * falloff;

        /// <summary>
        /// 平滑一格的增量：邻域加权平均与自身之差（01 §5.1「邻域加权平均的<b>增量</b>」）。
        /// <b>必须是增量</b>：写绝对值会让 delta 层与基底耦合，换噪声参数后平滑效果无法保留（D-01-1）。
        /// </summary>
        public static float SmoothDelta(float self, float neighborMean, float strength, float falloff)
            => (neighborMean - self) * Mathf.Clamp01(strength) * falloff;

        /// <summary>
        /// 把位移沿固定步长切成积分步进（D-01-3 的唯一实现处）。
        /// 返回步数（≥0），余数留给下一次调用（<paramref name="carryMeters"/> 传入传出）。
        /// <para>为何要留余量：若每次丢弃不足一步的位移，慢速精细雕刻会**完全不出效果**
        /// （手不动得快就永远凑不满 0.5m），是"细节笔刷没反应"的典型成因。</para>
        /// </summary>
        public static int AccumulateSteps(float movedMeters, ref float carryMeters)
        {
            var total = Mathf.Abs(movedMeters) + carryMeters;
            var steps = Mathf.FloorToInt(total / StepDistanceM);
            carryMeters = total - steps * StepDistanceM;
            return Mathf.Max(0, steps);
        }
    }

    /// <summary>
    /// 一次笔划的状态机（01 §5.2「一次连续笔划 = 一个 undo 记录」的证据面）。
    /// <para>由 Editor 侧工具驱动（OnEnable/OnDisable 由工具负责），本类只累计"这一笔的影响格集合"
    /// 与截断标志，供工具决定写 delta 的范围与提示文案。<b>不直接写资产</b>：
    /// 写资产要走 <see cref="SculptDeltaAsset"/> 且撤销登记在 Editor 侧（<c>Undo.RecordObject</c>），
    /// 二者分离才能让 EditMode 断言不依赖 Unity undo 系统。</para>
    /// </summary>
    public sealed class SculptStroke
    {
        readonly System.Collections.Generic.HashSet<int> _cells = new System.Collections.Generic.HashSet<int>();

        public bool Active { get; private set; }
        /// <summary>被 maxCells 截断过：工具须提示"笔刷过大，建议分次编辑"（01 §5.4）。</summary>
        public bool Truncated { get; private set; }
        public int CellCount => _cells.Count;
        public Vector2 LastCenter { get; private set; }
        public int MaxCellsPerStroke { get; set; } = 8000;

        public void Begin(Vector2 centerWorld)
        {
            Active = true;
            Truncated = false;
            _cells.Clear();
            LastCenter = centerWorld;
        }

        /// <summary>登记一笔触及的格。超出上限后停止收集并置 Truncated（不抛、不静默）。</summary>
        public bool Collect(int cellIndex)
        {
            if (!Active || Truncated) return false;
            if (_cells.Count >= MaxCellsPerStroke) { Truncated = true; return false; }
            return _cells.Add(cellIndex);
        }

        public void End() { Active = false; }

        public System.Collections.Generic.IEnumerable<int> Cells => _cells;
    }
}
