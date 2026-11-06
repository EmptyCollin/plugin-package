using System.Collections.Generic;
using Collin.StealthPlanner;
using NUnit.Framework;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 建议参考相位时长的守卫用例（DESIGN §6.7 / 决议 D-13 / U-2 收口）。
    /// <para>四条主判据：① 量化到 Δt 栅格再求整数 LCM（不是实数 LCM）；
    /// ② LCM 超 <see cref="PlannerBakeSettings.MaxAlignedBuckets"/> 必须走 max(c_i)×2 兜底并给 Warning；
    /// ③ **绝不自动改写手配 P** —— 算法本身不持有也不触碰任何设置资产；
    /// ④ 正确性声明必须随文案出现，且口径是「**不漏解由总视界 N×⌈P/Δt⌉ 决定**」（§6.7 v0.3 / D-29）；
    ///    旧句「P 取多大不改变是否存在 ≤N 相位的可行解」已由用例**反向断言禁止**回流。</para>
    /// </summary>
    public class PhaseReferenceTests
    {
        static readonly float[] Empty = new float[0];

        [Test]
        public void QuantizeToGrid_Then_IntegerLcm()
        {
            // 20s 与 30s，Δt=0.5 ⇒ k=40 与 60 ⇒ lcm=120 桶 ⇒ 60s
            var r = PhaseReference.Suggest(new float[] { 20f, 30f }, 0.5f);
            Assert.True(r.Valid, "两个合法周期应当可算");
            Assert.False(r.FellBack, "120 桶远未超 7200，不该兜底");
            Assert.AreEqual(120L, r.AlignedBuckets);
            Assert.AreEqual(60f, r.ReferenceSeconds, 1e-4f);
        }

        [Test]
        public void Coprime_Cycles_Explode_To_Fallback()
        {
            // ⚠️ 期望值现算（不照抄 §6.7 的举例）：文档写「23 与 29 → 667 个桶」是 Δt=1 的口径，
            // 667 桶 < 7200 **并不会兜底** —— 照那个例子断言必然假红。
            // 真会爆炸的档：Δt=0.5、周期 44.5s 与 48.5s ⇒ k=89 与 97（互质）⇒ LCM=8633 > 7200 ⇒ 兜底。
            var r = PhaseReference.Suggest(new float[] { 44.5f, 48.5f }, 0.5f);
            Assert.True(r.Valid);
            Assert.True(r.FellBack, "互质周期 LCM=8633 超 7200，本用例就是钉这条防线");
            Assert.AreEqual(-1L, r.AlignedBuckets);
            Assert.AreEqual(97f, r.ReferenceSeconds, 1e-4f);      // max(44.5,48.5)*2
            Assert.IsNotEmpty(r.Note);
            Assert.True(r.Note.Contains("近似"), "兜底必须在文案里说清是近似值，不能装作严格解");

            // 对照组：文档原例 23/29 在 Δt=0.5 下 LCM=1334，属**严格**出口
            var strict = PhaseReference.Suggest(new float[] { 23f, 29f }, 0.5f);
            Assert.False(strict.FellBack, "1334 桶未超上限，不该兜底（防把「互质」等同于「爆炸」）");
            Assert.AreEqual(1334L, strict.AlignedBuckets);
            Assert.AreEqual(667f, strict.ReferenceSeconds, 1e-4f);
        }

        [Test]
        public void Fallback_Threshold_Is_Exactly_One_Below_And_At_Cap()
        {
            // 边界必须可钉：让 LCM 恰为 cap 与 cap+1 两档对照
            // Δt=1 ⇒ k_i = c_i。cap=7200=2^5·3^2·5^2。
            //   3600 与 7200      ⇒ lcm=7200   = cap     ⇒ 不兜底
            //   7199 与 1438      ⇒ lcm=1438×7199=10,352,162 > cap ⇒ 兜底
            var atCap = PhaseReference.Suggest(new float[] { 3600f, 7200f }, 1f);
            Assert.False(atCap.FellBack, "LCM 恰等于上限属于「刚好够用」，不该兜底");
            Assert.AreEqual((long)PlannerBakeSettings.MaxAlignedBuckets, atCap.AlignedBuckets);
            Assert.AreEqual(7200f, atCap.ReferenceSeconds, 1e-4f);

            var overCap = PhaseReference.Suggest(new float[] { 7199f, 1438f }, 1f);
            Assert.True(overCap.FellBack, "LCM 超上限一位也必须兜底，否则等于放任内存与展示爆炸");
            Assert.AreEqual(7199f * 2f, overCap.ReferenceSeconds, 1e-4f);
        }

        [Test]
        public void Correctness_Declaration_Present_In_Both_Outcomes()
        {
            // §6.7 末段要求「须写进面板文案」⇒ 严格与兜底**两条出口**都得带上，
            // 否则用户看到兜底值时恰好丢掉唯一一条「P 不影响正确性」的说明。
            var strict = PhaseReference.Suggest(new float[] { 20f, 30f }, 0.5f);      // 严格 LCM 出口
            var fallback = PhaseReference.Suggest(new float[] { 44.5f, 48.5f }, 0.5f); // 兜底出口
            Assert.False(strict.FellBack); Assert.True(fallback.FellBack);
            // 两条出口都必须带正确性声明，且必须是 **D-29 的新口径**（不漏解取决于总视界 N×P）。
            // ⚠️ 反向断言：§6.7 原句那句「不改变『是否存在 ≤N 相位的可行解』」**不得回到文案里**——
            // 它与 horizon=N×⌈P/Δt⌉ 矛盾，属于会给策划错误安全感的不准确表述（改法待用户裁定原文，
            // 但面板文案必须先讲对的话）。
            StringAssert.Contains("总视界", strict.Note);
            StringAssert.Contains("总视界", fallback.Note);
            StringAssert.DoesNotContain("不改变「是否存在", strict.Note);
            StringAssert.DoesNotContain("不改变「是否存在", fallback.Note);
            Assert.That(strict.Note, Does.Contain(PhaseReference.CorrectnessDeclaration),
                        "文案必须由同一个常量拼出，禁止两处各写一份（会漂移）");
            Assert.That(fallback.Note, Does.Contain(PhaseReference.CorrectnessDeclaration),
                        "兜底出口同样必须带正确性声明");
        }

        [Test]
        public void Degenerate_Inputs_Fail_With_Reason_Not_Exception()
        {
            Assert.False(PhaseReference.Suggest(null, 0.5f).Valid, "周期名单为 null 必须判不可用");
            Assert.False(PhaseReference.Suggest(Empty, 0.5f).Valid, "无哨兵时必须判不可用并给原因");
            Assert.False(PhaseReference.Suggest(new float[] { 20f }, 0f).Valid, "Δt≤0 时量化无意义");
            Assert.False(PhaseReference.Suggest(new float[] { 20f }, -1f).Valid);
            // 不可用出口必须带可读原因（静默返回 0 秒会被面板当成「建议值是 0」）
            Assert.IsNotEmpty(PhaseReference.Suggest(Empty, 0.5f).Note);
            Assert.IsNotEmpty(PhaseReference.Suggest(new float[] { 20f }, 0f).Note);
        }

        [Test]
        public void NonPositive_And_Nan_Cycle_Treated_As_One_Bucket_Not_Crash()
        {
            // 哨兵周期缺失/为 0（常数周期）时按 1 桶保守量化：结果必须仍是「可用且合法」，
            // 而不是抛异常或给出荒谬的巨大 LCM。
            var r = PhaseReference.Suggest(new float[] { 0f, 10f }, 0.5f);
            Assert.True(r.Valid);
            Assert.False(r.FellBack);
            Assert.AreEqual(20L, r.AlignedBuckets);        // lcm(1, 20) = 20
            Assert.AreEqual(10f, r.ReferenceSeconds, 1e-4f);

            var nan = PhaseReference.Suggest(new float[] { float.NaN, 10f }, 0.5f);
            Assert.True(nan.Valid);
            Assert.AreEqual(20L, nan.AlignedBuckets, "NaN 周期按 1 桶处理，不得污染 LCM");
        }

        [Test]
        public void Rounding_Half_Up_And_Min_One()
        {
            // k_i = max(1, round(c_i/Δt))：0.49/0.5=0.98 ⇒ round=1 ⇒ 1 桶；
            // 0.2/0.5=0.4 ⇒ round=0 ⇒ 钳到 1 桶（不得为 0，否则 LCM 变 0、参考时长谎报为 0 秒）
            Assert.AreEqual(1L, PhaseReference.Suggest(new float[] { 0.2f }, 0.5f).AlignedBuckets);
            Assert.AreEqual(1L, PhaseReference.Suggest(new float[] { 0.49f }, 0.5f).AlignedBuckets);
            // 恰好 .5 的中点：AwayFromZero ⇒ 0.75/0.5=1.5 → 2
            Assert.AreEqual(2L, PhaseReference.Suggest(new float[] { 0.75f }, 0.5f).AlignedBuckets);
        }

        [Test]
        public void Pure_Function_Same_Input_Same_Output()
        {
            // 求解/展示侧都要求确定性（本包反复用「逐位相同」当验收口径）
            var cycles = new float[] { 20f, 30f, 45f };
            var a = PhaseReference.Suggest(cycles, 0.5f);
            var b = PhaseReference.Suggest(cycles, 0.5f);
            Assert.AreEqual(a.AlignedBuckets, b.AlignedBuckets);
            Assert.AreEqual(a.ReferenceSeconds, b.ReferenceSeconds);
            Assert.AreEqual(a.Note, b.Note);
        }

        [Test]
        public void Algorithm_Touches_No_Settings_Asset()
        {
            // ③ 的核心防线：算法不持有设置资产、不可能自动改写 P。
            // 判据用「构造出的实例字段没变」来钉：填入只能经 SetPhaseWindow，且只由按钮触发。
            var s = ScriptableObject.CreateInstance<PlannerBakeSettings>();
            try
            {
                var before = s.PhaseWindow;
                PhaseReference.Suggest(new float[] { 44.5f, 48.5f }, s.TimeStep);  // 真会爆炸的兜底档（LCM=8633>7200）
                Assert.AreEqual((double)before, (double)s.PhaseWindow, 1e-4, "算法不得有任何副作用改写手配 P");

                // 反向对照：显式写入口**确实**能改（否则上一条断言可能是「方法根本没跑」的假绿）
                s.SetPhaseWindow(58f);
                Assert.AreEqual(58f, s.PhaseWindow, 1e-4f);
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void SetPhaseWindow_Clamps_Itself_And_No_Implicit_Linkage()
        {
            // 与 OnValidate 同纪律：只钳自身。填 P 不得牵连 Δt / 格长（§0.1 所见即所得）。
            var s = ScriptableObject.CreateInstance<PlannerBakeSettings>();
            try
            {
                var dt0 = s.TimeStep; var cell0 = s.CellSize; var n0 = s.MaxPhases;

                s.SetPhaseWindow(0.3f);                       // 低于下限 1s
                Assert.AreEqual(1f, s.PhaseWindow, 1e-4f, "P 有 Min(1f) 下限，须钳住");
                Assert.AreEqual(dt0, s.TimeStep, 1e-4f, "填 P 不得改 Δt");
                Assert.AreEqual(cell0, s.CellSize, 1e-4f, "填 P 不得改格长");
                Assert.AreEqual(n0, s.MaxPhases, "填 P 不得改 N");

                s.SetPhaseWindow(float.NaN);                  // 非法输入必须整个忽略
                Assert.AreEqual(1f, s.PhaseWindow, 1e-4f, "NaN 不得写进序列化字段");
                s.SetPhaseWindow(float.PositiveInfinity);
                Assert.AreEqual(1f, s.PhaseWindow, 1e-4f, "无穷不得写进序列化字段");

                s.SetPhaseWindow(58f);
                Assert.AreEqual(116, s.BucketsPerPhase, "58/0.5 向上进位，桶数随 P 联动（派生量）");
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void Fallback_Cap_Comes_From_Single_Source_Not_A_Literal()
        {
            // 兜底阈值必须读 PlannerBakeSettings.MaxAlignedBuckets（§6.7 单一真源，同条目 3 口径）。
            // 判据：本文件源码里不得出现「= 7200」赋值形态，也不得出现裸 7200 参与运算。
            // 包根解析复用 PlannerBakeSettingsTests 里**已实测可用**的写法
            // （Application.dataPath 的父目录 + Packages/包名），不用 Assembly.Location 猜相对层级。
            var pkgRoot = System.IO.Path.Combine(
                System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName,
                "Packages", "com.collin.stealthplanner");
            Assert.True(System.IO.Directory.Exists(pkgRoot), "包目录应存在：" + pkgRoot);
            var fp = System.IO.Path.Combine(pkgRoot, "Runtime", "PhaseReference.cs");
            if (!System.IO.File.Exists(fp))
                Assert.Fail("没扫到 PhaseReference.cs（路径拼错会让本用例恒真）：" + fp);

            var text = System.IO.File.ReadAllLines(fp);
            var offenders = new List<string>();
            foreach (var line in text)
            {
                var t = line.TrimStart();
                // 注释里的说明性数字（如「默认 7200 桶」「不得出现第二个 7200」）不算重复定义；
                // 只有代码行出现 7200 才意味着把阈值抄成了字面量。
                if (t.StartsWith("//") || t.StartsWith("///")) continue;
                if (t.Contains("7200")) offenders.Add(t);
            }
            Assert.IsEmpty(offenders,
                "PhaseReference 的代码行里出现字面量 7200 ⇒ 第二处阈值定义，改一处漏一处");
            Assert.True(System.IO.File.ReadAllText(fp).Contains("MaxAlignedBuckets"),
                "必须显式引用单一真源常量");
        }
    }
}
