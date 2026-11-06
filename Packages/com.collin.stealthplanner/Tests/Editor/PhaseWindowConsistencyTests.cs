using System.Collections.Generic;
using Collin.StealthPlanner;
using NUnit.Framework;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 组 G 第 3 条「正确性声明可验证」的取证用例（DESIGN §6.7 / D-13 / D-02）。
    /// <para>草案原文：「同一关卡 P 取不同值，『是否存在 ≤N 相位的可行解』的布尔答案不变」，
    /// 拟验证方法「P=30/60/120 三跑，断言解存在性一致」。本类是**先实测再写断言**的产物，
    /// 结论与该文案**不完全一致**，故把成立的一半与不成立的一半都钉住，禁止只留绿灯：</para>
    /// <list type="bullet">
    /// <item><b>成立</b>：P 完全不参与可见性判定——表的公开 API 里没有任何地方能读到 P，
    /// 危险读数只由「格 + t mod 各片自身周期」决定（D-02 mod 分片）。</item>
    /// <item><b>不成立（文案少说一个前提）</b>：搜索视界 <c>horizon = N × ⌈P/Δt⌉</c> 里含 P
    /// （见 PhaseSolver.Horizon）。固定 N、只把 P 调小 ⇒ 覆盖的绝对时长变短 ⇒
    /// 确实能把有解截断成无解。所以「真正决定是否漏解的是 N」这句要改成
    /// **决定覆盖时长的是 N×P（总视界）**。这不是实现 bug：实现按 D-02/D-13 的口径做是对的，
    /// 是 §6.7 给策划的文案不精确 ⇒ 已登记 **D-29** 上报用户裁定文案改法，本用例不改文案、只钉语义。</item>
    /// </list>
    /// </summary>
    public class PhaseWindowConsistencyTests
    {
        const int Dim = 10;                       // 10×10 格
        const float Cell = 0.5f;
        const float Dt = 0.5f;
        const int Step = 1;                       // v=1m/s ⇒ floor(1×0.5/0.5)=1
        const int Start = 0;                      // 格 (0,0)
        const int Goal = 5;                       // 同行格 (5,0) ⇒ 切比雪夫距离恰 5（不依赖能否斜走）
        const int LowerBoundLayers = 5;           // 任何合法路线都不可能少于 5 层到达
        const float Cycle = 8f;                   // 哨兵周期 ⇒ 桶宽 = 8/16 = 0.5s

        /// <summary>确定性表：全格可走 + 一个 8s/16 桶的哨兵，只在**桶 0** 看见 3 号格。</summary>
        static VisibilityTable MakeTable()
        {
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, Cell, Dim, Dim, Dt);
            var all = new List<int>(Dim * Dim);
            for (var c = 0; c < Dim * Dim; c++) all.Add(c);
            t.MarkEvaluated(all);

            var slice = t.AddSlice(1, "g", Cycle, 16);
            t.SetSliceVisible(slice, 0, 3, true);
            t.RebuildPermanentSafeLayer();
            return t;
        }

        static PlannerBakeSettings MakeSettings(float p)
        {
            var s = ScriptableObject.CreateInstance<PlannerBakeSettings>();
            s.SetPhaseWindow(p);
            return s;
        }

        [Test]
        public void Horizon_Is_Derived_From_N_Times_P()
        {
            // 派生关系必须先钉住，否则下面「P 影响不漏解」没有落点。
            // Δt=0.5 ⇒ P=30/60/120 分别 60/120/240 桶；N 保持默认 4（MaxPhases 是只读属性，不由本用例改）。
            var s = MakeSettings(30f);
            try
            {
                Assert.AreEqual(4, s.MaxPhases, "默认 N，用于确认本用例只动了 P");
                Assert.AreEqual(60, s.BucketsPerPhase);
                Assert.AreEqual(240, PhaseSolver.Horizon(s, out _), "horizon = N × ⌈P/Δt⌉");

                s.SetPhaseWindow(60f);
                Assert.AreEqual(480, PhaseSolver.Horizon(s, out _));
                s.SetPhaseWindow(120f);
                Assert.AreEqual(960, PhaseSolver.Horizon(s, out _));
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void VisibilityVerdict_Ignores_P_But_Keeps_Cycle_Periodicity()
        {
            // 桶宽 = cycle/buckets = 0.5s ⇒ t=0.2 落桶 0（危险），t=0.2+P 的桶号只跟 P 对周期的余数有关。
            // ⚠️ 「跨 P 读数相等」只在 P 是周期整数倍时成立 —— 对 30/60/120 直接断言相等是**假判据**
            //    （本包 D-27/H-67 同族：判据本身错了，绿了也没验到东西）。⇒ 双向判据，两边都得是活的。
            var t = MakeTable();
            const float T = 0.2f;
            Assert.True(t.IsDangerousAt(3, T), "t=0.2 落桶 0 ⇒ 3 号格应危险");
            Assert.False(t.IsDangerousAt(3, T + 0.6f), "t=0.8 落桶 1 ⇒ 应已安全（证明桶 0 之外不危险，判据非恒真）");

            // ① P 为周期整数倍 ⇒ 同相，读数必须相同
            foreach (var p in new[] { 8f, 16f, 24f })
                Assert.AreEqual(t.IsDangerousAt(3, T), t.IsDangerousAt(3, T + p),
                    "P=" + p + " 是周期 8 的整数倍 ⇒ 跨 P 必须同相");

            // ② P 非整数倍 ⇒ 相位错开，读数必须不同
            foreach (var p in new[] { 3f, 30f, 60f })
                Assert.AreNotEqual(t.IsDangerousAt(3, T), t.IsDangerousAt(3, T + p),
                    "P=" + p + " 不是周期 8 的整数倍 ⇒ 相位应错开");

            // ③ 表侧结构断言：可见性判定的任何公开入口都读不到 P。
            //    ⚠️ 刻意**不用**「改 P 后逐位比对掩码」那种写法 —— 表压根没有 settings 引用，
            //    那种比对必然恒真（= 绿得毫无意义）。改成扫签名的结构判据才有牙齿。
            var leaks = new List<string>();
            foreach (var m in typeof(VisibilityTable).GetMethods(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                foreach (var pt in m.GetParameters())
                    if (pt.ParameterType == typeof(PlannerBakeSettings)) leaks.Add(m.Name);
            }
            Assert.IsEmpty(leaks, "VisibilityTable 竟有方法接受 PlannerBakeSettings ⇒ P 已渗入判定侧");
            Assert.False(typeof(VisibilityTable).IsAssignableFrom(typeof(PlannerBakeSettings)),
                "参数资产不得成为表的依赖");
        }

        [Test]
        public void FeasibleBool_Agrees_When_AllHorizons_Cover_The_Route()
        {
            // 草案「三跑一致」的正例：解只需 ~5 层，远小于最小档视界 240 ⇒ 三档必然一致。
            // 到达层只钉**下界**（切比雪夫距离 5 是数学下界），不把实现细节当契约硬编码。
            var t = MakeTable();
            int seen = -1;
            foreach (var p in new[] { 30f, 60f, 120f })
            {
                var s = MakeSettings(p);
                try
                {
                    var h = PhaseSolver.Horizon(s, out _);
                    var r = PhaseSolver.Solve(t, Start, Goal, h, Step, 5, out var f);
                    Assert.True(f.ok, "P=" + p + " 求解失败：" + f.reason);
                    Assert.True(r.Feasible, "P=" + p + " 视界 " + h + " 远大于 5 层，应有解");
                    Assert.GreaterOrEqual(r.ArrivalBucket, LowerBoundLayers,
                        "P=" + p + " 到达层低于切比雪夫下界 ⇒ 判据或表构造有问题");
                    if (seen < 0) seen = r.ArrivalBucket;
                    Assert.AreEqual(seen, r.ArrivalBucket, "P=" + p + " 到达层与其余档不一致");
                }
                finally { Object.DestroyImmediate(s); }
            }
        }

        [Test]
        public void FeasibleBool_Does_Change_When_TotalHorizon_TooSmall()
        {
            // §6.7 文案**不成立**的那一半，必须留成反例而不是删掉：
            // 同一张表、同一对起终点，只把总视界压到小于切比雪夫下界 ⇒ 有解被截断成无解。
            var t = MakeTable();
            var full = PhaseSolver.Solve(t, Start, Goal, 240, Step, 5, out var f1);
            Assert.True(f1.ok);
            Assert.True(full.Feasible, "240 桶视界下应有解");

            var cut = PhaseSolver.Solve(t, Start, Goal, LowerBoundLayers - 1, Step, 5, out var f2);
            Assert.True(f2.ok, "视界过小不是参数错误，应正常返回「无解」而非失败");
            Assert.False(cut.Feasible, "视界 " + (LowerBoundLayers - 1) + " 桶到不了第 "
                + LowerBoundLayers + " 层 ⇒ 必然判无解（P 单独也能造成这种截断）");
        }

        [Test]
        public void ReferenceValue_Never_Written_Back_By_Solve_Path()
        {
            // D-13 的另一半防线：走「改 P → 求解」这条路径，绝不允许把建议值写回手配 P。
            var t = MakeTable();
            var s = MakeSettings(60f);
            try
            {
                var p0 = s.PhaseWindow;
                var refVal = PhaseReference.Suggest(new[] { Cycle, Cycle }, Dt);
                Assert.True(refVal.Valid);
                Assert.AreEqual(8f, refVal.ReferenceSeconds, 1e-4f,
                    "参考值 8s 与手配 60s 不同，才有区分度");

                PhaseSolver.Solve(t, s, Vector3.zero, new Vector3(2.7f, 0f, 0.2f), 1f, out _);
                Assert.AreEqual(p0, s.PhaseWindow, 1e-4f,
                    "求解/参考值路径不得改写手配 P（只有面板按钮经 SetPhaseWindow 才能改）");
            }
            finally { Object.DestroyImmediate(s); }
        }
    }
}
