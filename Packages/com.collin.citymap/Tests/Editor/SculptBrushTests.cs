using NUnit.Framework;
using UnityEngine;

namespace Collin.CityMap.Tests
{
    /// <summary>
    /// 01 §5 笔刷数学内核（SculptBrush / SculptStroke）的 EditMode 断言。
    /// <para>本文件存在的唯一理由：<b>A-1-5 说"单一笔划在 60Hz 与 144Hz 机器上产生相同 delta"，
    /// 而这个性质只有把积分写成可独立调用的纯函数才测得了</b>。
    /// 若把积分塞进 EditorTool 的 OnSceneGUI，就只能人肉试手感 —— 而帧率相关的 bug
    /// 恰好是"我这台机器上没问题的"典型，人肉试必然漏。</para>
    /// </summary>
    public class SculptBrushTests
    {
        // ─────────────── A-1-5：帧率无关 ───────────────

        /// <summary>
        /// 同一段位移按 60Hz 与 144Hz 切帧，累计增量必须相同（固定步长积分，D-01-3）。
        /// 若实现是 `delta *= Time.deltaTime`，这里会立刻分叉 —— 正是本用例的判别对象。
        /// </summary>
        [Test]
        public void Same_World_Distance_Produces_Same_Delta_Regardless_Of_Frame_Rate()
        {
            const float moved = 13.7f;         // 任意非整步距离，逼出余量处理
            const float strength = 2.5f;

            float Integrate(int frames)
            {
                var carry = 0f;
                var sum = 0f;
                var perFrame = moved / frames;
                for (var f = 0; f < frames; f++)
                {
                    var steps = SculptBrush.AccumulateSteps(perFrame, ref carry);
                    for (var s = 0; s < steps; s++)
                        sum += SculptBrush.DeltaFor(SculptBrush.StepDistanceM, strength, 1f);
                }
                return sum;
            }

            var at60 = Integrate(60);
            var at144 = Integrate(144);
            var at30 = Integrate(30);

            Assert.AreEqual(at60, at144, at60 * 0.02f,
                $"60Hz 与 144Hz 的笔划强度必须一致（{at60:0.####} vs {at144:0.####}）");
            Assert.AreEqual(at60, at30, at60 * 0.05f,
                "30Hz 亦须一致（容差稍宽：步数取整误差与帧数成正比）");
            Assert.Greater(at60, 0f, "自校验：积分必须真的产生了量，否则上面两条断言恒真");
        }

        /// <summary>
        /// 余量必须留存，否则**慢速精细雕刻完全不出效果**：手移得慢时每次都不足一个固定步长，
        /// 丢弃余量就等于"每帧都是 0 步"。这是"细节笔刷没反应"的典型成因，肉眼难归因。
        /// </summary>
        [Test]
        public void Sub_Step_Movement_Accumulates_Across_Calls_Not_Dropped()
        {
            var carry = 0f;
            var steps = 0;
            // 每帧只挪 0.01m（不足 0.5m 步长），共挪满 2m
            // 严格场景用二进制精确步长（0.25f = 步长 0.5f 的一半）验证余量机制本身：
            // 每次不足一步，但跨调用累积必须凑出整数步——这是"丢了余量就得 0"的判别核心。
            carry = 0f; steps = 0;
            for (var f = 0; f < 200; f++) steps += SculptBrush.AccumulateSteps(0.25f, ref carry);
            Assert.AreEqual(100, steps, "200×0.25m=50m 必须精确产生 100 个 0.5m 步（丢余量会得 0）");
            Assert.AreEqual(0f, carry, 1e-5f, "整步用完，余量应归零");

            // 慢速场景（每帧仅挪 0.01m）验真实手感：单次不足步长，累计必须仍能出步。
            // 只断"显著 > 0"而非钉死精确步数——0.01f 二进制不精确，累加到 2m 会落在 3~4 步，
            // 这是浮点固有噪声，钉死精确值会让用例脆弱且掩盖真实回归。
            carry = 0f; steps = 0;
            for (var f = 0; f < 200; f++) steps += SculptBrush.AccumulateSteps(0.01f, ref carry);
            Assert.GreaterOrEqual(steps, 3, "约 2m 位移须产生约 4 步；若丢余量这里会是 0");

            // 方向反转（来回蹭）不得让余量变负导致少算步
            carry = 0.3f;
            var back = SculptBrush.AccumulateSteps(-0.1f, ref carry);
            Assert.GreaterOrEqual(back, 0, "位移取绝对值参与积分，来回蹭也应累计");
        }

        // ─────────────── 衰减核 ───────────────

        [Test]
        public void Falloff_Is_Full_In_Core_Zero_Beyond_Radius_And_Monotonic()
        {
            const float r = 10f;
            const float core = 4f;

            Assert.AreEqual(1f, SculptBrush.FalloffAt(0f, r, core), 1e-5f, "中心必须全强");
            Assert.AreEqual(1f, SculptBrush.FalloffAt(core, r, core), 1e-5f, "核心边界仍全强");
            Assert.AreEqual(0f, SculptBrush.FalloffAt(r + 1f, r, core), 1e-5f, "半径外必须为 0");
            Assert.AreEqual(0f, SculptBrush.FalloffAt(r, r, core), 1e-4f, "恰在半径处衰减到 0");

            var prev = 1f;
            for (var d = core; d <= r; d += 0.5f)
            {
                var v = SculptBrush.FalloffAt(d, r, core);
                Assert.LessOrEqual(v, prev + 1e-5f, $"衰减必须单调不升（d={d:0.0}）");
                prev = v;
            }
        }

        /// <summary>退化输入不得崩或 NaN：面板滑条能拖到 0，core 也可能被拖到 = radius。</summary>
        [Test]
        public void Degenerate_Radii_Return_Zero_Not_Nan()
        {
            Assert.AreEqual(0f, SculptBrush.FalloffAt(1f, 0f));
            Assert.AreEqual(0f, SculptBrush.FalloffAt(1f, -5f));
            // core==radius：核心内仍全强，核心外硬截止（不得除零）
            Assert.AreEqual(1f, SculptBrush.FalloffAt(3f, 5f, 5f), 1e-5f, "dist(3)<核心(5)：全强");
            Assert.AreEqual(0f, SculptBrush.FalloffAt(6f, 5f, 5f), 1e-5f, "核心外：截止，不除零");
            // core>radius 属非法配置：实现须把 core 钳进 [0,radius]，半径外一律 0、不得为负
            Assert.AreEqual(0f, SculptBrush.FalloffAt(9f, 4f, 6f), 1e-5f,
                "core(6)>radius(4) 钳制后半径外为 0");
            Assert.GreaterOrEqual(SculptBrush.FalloffAt(1f, 4f, 6f), 0f, "钳制后取值不得为负");
            Assert.IsFalse(float.IsNaN(SculptBrush.FalloffAt(0f, 0f)));
        }

        [Test]
        public void SmoothDelta_Moves_Toward_Neighbor_Mean_By_Strength()
        {
            // 必须是**增量**（写绝对值会让平滑与基底耦合，换噪声参数后效果丢，D-01-1）
            var d = SculptBrush.SmoothDelta(self: 5f, neighborMean: 7f, strength: 0.5f, falloff: 1f);
            Assert.AreEqual(1f, d, 1e-5f, "(7-5)×0.5 = 1：向邻域均值靠拢一半强度");
            Assert.AreEqual(0f, SculptBrush.SmoothDelta(5f, 5f, 1f, 1f), 1e-5f, "已平则无增量");
            // 强度 1 时一步到位 = 邻域均值；增量语义下 self + delta == mean
            Assert.AreEqual(7f, 5f + SculptBrush.SmoothDelta(5f, 7f, 1f, 1f), 1e-5f);
        }

        // ─────────────── 笔划状态机（A-1-6 的 Runtime 半边） ───────────────

        [Test]
        public void Stroke_Collects_Unique_Cells_And_Truncates_At_Limit_Not_Throws()
        {
            var s = new SculptStroke { MaxCellsPerStroke = 100 };
            s.Begin(Vector2.zero);
            Assert.IsTrue(s.Active);

            var added = 0;
            for (var i = 0; i < 500; i++)
                if (s.Collect(i)) added++;

            Assert.AreEqual(100, added, "超过上限必须停止收集（01 §5.4：截断而非静默吃性能）");
            Assert.IsTrue(s.Truncated, "截断必须可被工具读出并提示用户，不能无声");
            Assert.AreEqual(100, s.CellCount);

            // 截断后继续喂点不得恢复收集（否则上限形同虚设）
            Assert.IsFalse(s.Collect(9999));
            s.End();
            Assert.IsFalse(s.Active);
            Assert.IsFalse(s.Collect(1));
        }

        [Test]
        public void Stroke_Begin_Resets_Truncation_And_Cells()
        {
            var s = new SculptStroke { MaxCellsPerStroke = 10 };
            s.Begin(Vector2.zero);
            for (var i = 0; i < 50; i++) s.Collect(i);
            Assert.IsTrue(s.Truncated);

            s.Begin(new Vector2(3f, 4f));        // 新笔划
            Assert.IsFalse(s.Truncated, "上一笔的截断不得残留：否则第二笔永远没效果");
            Assert.AreEqual(0, s.CellCount);
            Assert.AreEqual(new Vector2(3f, 4f), s.LastCenter);
        }

        /// <summary>重复点同一格只算一次（ HashSet 语义）：否则截断上限会被同一格反复登记吃掉。</summary>
        [Test]
        public void Stroke_Does_Not_Double_Count_Same_Cell()
        {
            var s = new SculptStroke { MaxCellsPerStroke = 5 };
            s.Begin(Vector2.zero);
            for (var i = 0; i < 50; i++) s.Collect(7);
            Assert.AreEqual(1, s.CellCount);
            Assert.IsFalse(s.Truncated);
        }

        /// <summary>步长常量是跨机器一致的地基，改动即改行为 ⇒ 钉死并留出处。</summary>
        [Test]
        public void Step_Constants_Are_As_Documented()
        {
            Assert.AreEqual(0.5f, SculptBrush.StepDistanceM, 1e-6f, "01 §5.1 固定步长");
            Assert.AreEqual(1f, SculptBrush.StrengthPerMeter, 1e-6f);
        }
    }
}
