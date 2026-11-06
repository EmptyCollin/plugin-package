using System;
using System.Collections.Generic;

namespace Collin.StealthPlanner
{
    /// <summary>
    /// 建议参考相位时长（DESIGN §6.7 / 决议 D-13 的实现体，U-2 收口项）。
    /// <para>用途：给策划一个<b>填 P 的参考值</b>——只读展示 + 用户主动「一键填入」，
    /// 算法本体<b>绝不改写</b>手配的相位窗（面板侧同样禁止自动回写）。</para>
    /// <para>为什么放 Runtime 而非 Editor：§6.7 的量化 → 整数 LCM → 互质爆炸兜底是**纯数学**，
    /// 放进 Runtime 才能被 EditMode 用例逐条断言（边界 7200、兜底、退化输入）；
    /// 留在 Editor 就只能靠肉眼看面板文案，正是本包 D-23 反复踩的「防线没有可观测出口」。</para>
    /// <para>单一真源纪律：兜底阈值一律读 <see cref="PlannerBakeSettings.MaxAlignedBuckets"/>，
    /// 本文件<b>不得</b>出现第二个 7200（守卫用例按赋值形态统计，全包仅一处）。</para>
    /// </summary>
    public static class PhaseReference
    {
        /// <summary>一次建议计算的结果（值类型，纯函数：同输入同输出）。</summary>
        public struct Result
        {
            /// <summary>输入可用（有周期数据且 Δt 合法）。false 时 ReferenceSeconds 无意义。</summary>
            public bool Valid;
            /// <summary>true = LCM 超过 <see cref="PlannerBakeSettings.MaxAlignedBuckets"/>，
            /// 走了「max(c_i)×2」的近似兜底；此时 AlignedBuckets = -1。</summary>
            public bool FellBack;
            /// <summary>建议参考时长（秒）。未兜底 = L×Δt；兜底 = max(c_i)×2。</summary>
            public float ReferenceSeconds;
            /// <summary>量化后的整数最小公倍数 L（单位=桶）；兜底时为 -1。</summary>
            public long AlignedBuckets;
            /// <summary>给面板直接展示的中文文案（兜底时含 §6.7 规定的 Warning 与正确性声明）。</summary>
            public string Note;
        }

        static long Gcd(long a, long b)
        {
            while (b != 0) { var t = a % b; a = b; b = t; }
            return a;
        }

        /// <summary>随面板文案出现的正确性说明。
        /// <para>⚠️ 措辞与 DESIGN §6.7 原句**有意不同**：§6.7 写「P 取多大不改变是否存在 ≤N 相位的可行解」，
        /// 但实测 <c>horizon = N × ⌈P/Δt⌉</c> 里含 P ⇒ 固定 N 只调小 P **确实会截断解**（见 D-29，
        /// 反例由 PhaseWindowConsistencyTests 钉住）。§6.7 原句待用户裁定后再改，
        /// 但**给策划看的文案必须先讲对的话**：准确口径是「不漏解由总视界 N×P 决定」。
        /// 前半句（P 不参与可见性判定）确实成立，予以保留。</para></summary>
        public const string CorrectnessDeclaration =
            "（正确性声明）P 不参与可见性判定——各哨兵按自身周期取模查询（D-02 mod 分片），"
            + "改 P 不改变任何格的危险读数。但**会不会漏解取决于总视界 N×⌈P/Δt⌉**："
            + "P 调小（N 不动）会缩短覆盖时长、可能把有解判成无解。"
            + "若只是想看得更久，优先加 P；若担心漏解，请同时确认 N。";

        /// <summary>
        /// 按 §6.7 计算建议参考相位时长。
        /// <para>做法：周期量化到 Δt 栅格 <c>k_i = max(1, round(c_i/Δt))</c> →
        /// <c>L = lcm(k_1..k_n)</c> → 参考时长 = <c>L × Δt</c>；
        /// L 超 <see cref="PlannerBakeSettings.MaxAlignedBuckets"/>（周期互质爆炸）时退回
        /// <c>max(c_i) × 2</c> 并在 Note 里给 Warning。</para>
        /// </summary>
        /// <param name="cyclesSeconds">各 NPC 巡逻周期（秒）。真实来源 = 烘焙表
        /// <c>VisibilityTable.Slices[i].cycleSeconds</c>（其上游是 core 契约的 cycleTime）。</param>
        /// <param name="timeStep">时间桶 Δt（秒）。</param>
        public static Result Suggest(IReadOnlyList<float> cyclesSeconds, float timeStep)
        {
            if (float.IsNaN(timeStep) || float.IsInfinity(timeStep) || timeStep <= 0f)
                return new Result
                {
                    Valid = false,
                    AlignedBuckets = -1,
                    Note = "时间步长 Δt 非法（≤0 或非有限值），无法给出参考相位时长。"
                };

            if (cyclesSeconds == null || cyclesSeconds.Count == 0)
                return new Result
                {
                    Valid = false,
                    AlignedBuckets = -1,
                    Note = "没有哨兵周期数据：场景内无巡逻单位，或尚未烘焙可见性表。"
                };

            var cap = (long)PlannerBakeSettings.MaxAlignedBuckets;
            long lcm = 1;
            float maxCycle = 0f;
            bool exploded = false;

            for (var i = 0; i < cyclesSeconds.Count; i++)
            {
                var c = cyclesSeconds[i];
                // 退化周期（NaN / ≤0）按「常数周期」保守处理：量化成 1 桶，不崩、不谎报更大周期。
                long k;
                if (float.IsNaN(c) || float.IsInfinity(c) || c <= 0f) k = 1;
                else
                {
                    var r = Math.Round(c / timeStep, MidpointRounding.AwayFromZero);
                    if (double.IsNaN(r) || double.IsInfinity(r)) k = 1;
                    else { k = (long)r; if (k < 1) k = 1; }
                }
                if (k > 0 && c > maxCycle) maxCycle = c;

                var g = Gcd(lcm, k);
                var next = lcm / g * k;                       // 先除后乘，尽量推迟溢出
                if (next <= 0 || next > cap) { exploded = true; break; }
                lcm = next;
            }

            if (exploded)
            {
                var approx = maxCycle > 0f ? maxCycle * 2f : timeStep * 2f;
                return new Result
                {
                    Valid = true,
                    FellBack = true,
                    AlignedBuckets = -1,
                    ReferenceSeconds = approx,
                    Note = "严格相位对齐周期过大（LCM 超 " + cap + " 桶），此为近似值：max(c_i)×2 = "
                        + approx.ToString("0.##") + "s。本工具按 mod 分片工作，P 不取 LCM 不影响结果正确性，"
                        + "只影响「重复模式」的展示长度与总视界覆盖时长。" + CorrectnessDeclaration
                };
            }

            return new Result
            {
                Valid = true,
                FellBack = false,
                AlignedBuckets = lcm,
                ReferenceSeconds = lcm * timeStep,
                Note = "建议参考时长 = LCM(" + cyclesSeconds.Count + " 个量化周期) × Δt = "
                    + lcm + " × " + timeStep.ToString("0.###") + " = "
                    + (lcm * timeStep).ToString("0.##") + "s。" + CorrectnessDeclaration
            };
        }
    }
}
