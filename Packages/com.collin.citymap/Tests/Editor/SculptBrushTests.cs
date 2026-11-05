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

        // ─────────────── H-63：长笔划的影响面必须覆盖整条轨迹 ───────────────

        /// <summary>
        /// 状态机必须累积<b>整条轨迹</b>的范围，而不是只记最后一步。
        /// <para>用户反馈：点下后拖较长距离，"最后落在编辑器上的只有最后一截拖拽操作生效"。
        /// 隔离探针实测：写入侧覆盖格号 x[238,411]（中段最大值 53.6m 也写进去了），
        /// 而松手时提交给脏区的盒只有以<b>落点</b>为圆心的 202m ⇒ 中段那些格落在脏盒外
        /// ⇒ S1b 与显示层都不重算它们 ⇒ 数据改了、画面没动。根因在提交侧，不在笔刷数学侧。</para>
        /// </summary>
        [Test]
        public void Stroke_Accumulates_Track_Extent_Across_Steps()
        {
            var s = new SculptStroke();
            const float x0 = 1000f, x1 = 1600f, z = 4000f, r = 50.5f;
            s.Begin(new Vector2(x0, z));

            // 与真实流程一致：BeginStroke 会先刷一次起笔点，再逐个 MouseDrag 事件刷（工具里都是走 StampRadius）
            s.AccumulateExtent(new Vector2(x0 - r, z - r), new Vector2(x0 + r, z + r));
            for (var px = x0 + 20f; px <= x1; px += 20f)
                s.AccumulateExtent(new Vector2(px - r, z - r), new Vector2(px + r, z + r));

            Vector2 min, max;
            Assert.IsTrue(s.TryGetExtentWorld(out min, out max), "累积过范围必须可取回");
            Assert.LessOrEqual(min.x, x0 - r, "起笔侧（含半径外扩）必须在范围内");
            Assert.GreaterOrEqual(max.x, x1 + r, "落笔侧必须在范围内");
            Assert.Greater(max.x - min.x, 600f,
                $"长笔划范围必须远大于笔刷直径（实得 {max.x - min.x:0.#}）：只记最后一步 = 用户说的「只有最后一截生效」");

            // 新笔不得残留上一笔范围：否则第二笔会白白重建上一大片的 chunk
            s.Begin(new Vector2(10f, 20f));
            Assert.IsFalse(s.TryGetExtentWorld(out min, out max),
                "Begin 必须清空轨迹范围，否则脏区会越拖越大");
        }

        /// <summary>
        /// 端到端口径：600m 长笔划提交后，脏区必须完整包住整条轨迹；
        /// 同时保留**逐轴尺寸**（不得把长方形折成正方形取长轴——那会漏掉长轴末端以外的形状信息，
        /// 且横向笔划会被虚报成 600×600 的大盒，白白多刷一片 chunk）。
        /// <para>防恒真对照：圆心口径仍给出小盒 ⇒ 证明两条路径各自有效，不是同一个盒子。</para>
        /// </summary>
        [Test]
        public void Long_Stroke_Dirty_Bounds_Covers_Track_Without_Becoming_A_Square()
        {
            var go = new GameObject("h63_stroke");
            var terr = go.AddComponent<CityTerrain>();
            try
            {
                const float x0 = 1000f, x1 = 1600f, z = 4000f, r = 50.5f;
                var track = new Bounds(new Vector3((x0 + x1) * 0.5f, 0f, z),
                                       new Vector3(x1 - x0 + 2f * r, 1f, 2f * r));
                terr.MarkDeltaChanged(track);
                var b = terr.Ledger.BoundsOf(IntentKind.DeltaStroke);

                Assert.LessOrEqual(b.center.x - b.size.x * 0.5f, x0, "脏区必须覆盖起笔侧");
                Assert.GreaterOrEqual(b.center.x + b.size.x * 0.5f, x1, "脏区必须覆盖落笔侧（旧口径正是漏在这里）");
                Assert.LessOrEqual(b.center.z - b.size.z * 0.5f, z - r, "z 向须覆盖笔刷半径边缘");
                Assert.GreaterOrEqual(b.center.z + b.size.z * 0.5f, z + r);

                Assert.Greater(b.size.x, 600f, $"长轴应≈笔划长度（实得 {b.size.x:0.#}）");
                Assert.Less(b.size.z, b.size.x * 0.5f,
                    $"短轴不得被拉成方形（x={b.size.x:0.#} z={b.size.z:0.#}）：" +
                    "取长轴当正方形边长会把 600×101 的笔划虚报成 600×600");

                // 对照组：圆心口径仍是小盒（两条路径各守各的语义，不是同一实现）
                terr.MarkDeltaChanged(new Vector2(x1, z), r);
                var c = terr.Ledger.BoundsOf(IntentKind.DeltaStroke);
                Assert.Less(c.size.x, 300f,
                    $"圆心口径仍应是笔刷量级（实得 {c.size.x:0.#}）——否则上面两条断言是恒真的");
                Assert.Less(Mathf.Abs(c.size.x - c.size.z), 1f, "圆心口径本应是正方形（长宽相等）");

                Debug.Log($"[M4k] 长笔划脏区 {b.size.x:0.#}×{b.size.z:0.#}m（笔划 600m）；" +
                          $"对照圆心口径 {c.size.x:0.#}×{c.size.z:0.#}m");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>退化输入不得产出"零尺寸脏盒"（会被 AddDirtyBound 当无脏区丢掉 ⇒ 静默不重建）。</summary>
        [Test]
        public void Degenerate_Stroke_Extent_Still_Produces_Usable_Bounds()
        {
            var go = new GameObject("h63_degenerate");
            var terr = go.AddComponent<CityTerrain>();
            try
            {
                // 每个用例各自的 CityTerrain 实例自带独立账本，无需额外隔离
                terr.MarkDeltaChanged(default(Bounds));           // 全零包围盒（被阻断/未落笔的极端路径）
                var b = terr.Ledger.BoundsOf(IntentKind.DeltaStroke);
                Assert.Greater(b.size.x, 0f, "零尺寸盒会被下游当『无脏区』丢掉 ⇒ 必须兜底成最小盒");
                Assert.Greater(b.size.z, 0f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ─────────────── H-65：同一笔内反复施加平滑必须有界 ───────────────

        /// <summary>
        /// 同一笔内对同一区域反复落事件，平滑的累积增量必须<b>有界收敛</b>，不得随事件数线性增长。
        /// <para>用户反馈「平滑的算法有问题？噪音反而变大了」。隔离探针实测根因：
        /// 场（<c>View.BoundField</c>）在<b>整个笔划期间不更新</b>（刷新要等松手提交后走 S1b→显示层），
        /// 于是每个 MouseDrag 事件读到的 <c>field.GetHeight</c> 都是同一份旧值，
        /// 平滑增量被<b>重复算出同一个数并 AddDelta 累加</b> ⇒ ±1m 棋盘磨 20 个事件 →
        /// 累积 delta = <b>−21 / +21 m</b>（单次上限本应 ≈2m）= 噪音放大 20 倍。
        /// 修复后同一场景实测收敛到 <b>−1.003 / +0.997</b>。</para>
        /// <para>抬升/下压<b>不受此约束</b>：其增量语义本就是"位移积分"，累加是正确行为 ——
        /// 故本用例带一个抬升对照组，证明"平滑有界"不是恒真断言。</para>
        /// </summary>
        [Test]
        public void Smooth_Accumulates_Bounded_Within_One_Stroke()
        {
            var tool = System.Type.GetType("Collin.CityMap.EditorTooling.SculptBrushTool, com.collin.citymap.Editor");
            Assert.IsNotNull(tool, "取不到 SculptBrushTool（Editor 程序集未引用？）");
            var NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var IN = System.Reflection.BindingFlags.Instance;
            var ST = System.Reflection.BindingFlags.Static;

            var keys = new[] { "_terrain", "_grid", "_hasGrid", "_view", "_lastCenter", "_carry",
                               "_stroking", "_blocked", "RadiusMeters", "Strength", "Action" };
            var save = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var k in keys) save[k] = tool.GetField(k, NP | ST).GetValue(null);

            var proj = ScriptableObject.CreateInstance<CityProject>();
            var prof = ScriptableObject.CreateInstance<TerrainProfile>();
            var go = new GameObject("h65_smooth");
            SculptDeltaAsset delta = null, delta2 = null;
            try
            {
                proj.EnsureRegistered();
                proj.SetWorldParams(600f, WorldScale.Small2km, 60f, 20f, 100f, 100f);
                proj.SetSeed(6501);
                prof.SetBaseShape(18f, 520f, 4, 0.5f, 2f, 0.35f);
                prof.SetUrbanFlatness(0.5f);

                var terr = go.AddComponent<CityTerrain>();
                terr.SetProfile(prof);
                var view = go.AddComponent<CityTerrainView>();
                delta = ScriptableObject.CreateInstance<SculptDeltaAsset>();
                terr.SetDelta(delta);

                var ctx = new PipelineContext(proj);
                var grid = ctx.Grid;
                var field = new TerrainField(grid);
                // 高频棋盘 ±1m，范围须盖住笔刷半径 + 一步 span，否则边缘格读到"没棋盘"的 0
                int gcx = grid.Columns / 2, gcz = grid.Rows / 2;
                const int seed = 34;
                for (var z = -seed; z <= seed; z++)
                    for (var x = -seed; x <= seed; x++)
                    {
                        var i = (gcz + z) * grid.Columns + (gcx + x);
                        if (i < 0 || i >= field.Heights.Length) continue;
                        var v = ((x + z) % 2 == 0 ? 1f : -1f);
                        field.BaseHeights[i] = v; field.Heights[i] = v;
                    }

                // 把场喂给显示层（工具经 View.BoundField 读它，与画面同源）
                var vt = typeof(CityTerrainView);
                vt.GetField("_field", NP | IN).SetValue(view, field);
                vt.GetField("_grid", NP | IN).SetValue(view, grid);
                vt.GetField("_hasGrid", NP | IN).SetValue(view, true);

                tool.GetField("_terrain", NP | ST).SetValue(null, terr);
                tool.GetField("_grid", NP | ST).SetValue(null, grid);
                tool.GetField("_hasGrid", NP | ST).SetValue(null, true);
                tool.GetField("_view", NP | ST).SetValue(null, view);
                tool.GetField("RadiusMeters", NP | ST).SetValue(null, 20f);
                tool.GetField("Strength", NP | ST).SetValue(null, 1f);

                var cc = grid.CellCenterWorld(gcx, gcz);
                var center = new Vector3(cc.x, 0f, cc.y);      // Vector2 第二分量承载世界 Z（本工程口径）

                float AccumAt(SculptDeltaAsset d, int cx, int cz)
                {
                    var zero = new TerrainField(grid);
                    d.ApplyTo(zero);
                    return zero.Heights[cz * grid.Columns + cx];
                }

                // ── 平滑：原地磨 20 个事件 ──
                tool.GetField("Action", NP | ST).SetValue(null, SculptAction.Smooth);
                var begin = tool.GetMethod("BeginStroke", NP | ST);
                var drag = tool.GetMethod("ApplyDrag", NP | ST);
                var end = tool.GetMethod("EndStroke", NP | ST);
                begin.Invoke(null, new object[] { center });
                for (var i = 1; i <= 20; i++)
                {
                    var off = (i % 2 == 0 ? -0.6f : 0.6f);
                    drag.Invoke(null, new object[] { new Vector3(center.x + off, 0f, center.z) });
                }
                end.Invoke(null, null);

                var smoothAcc = AccumAt(delta, gcx, gcz);
                var smoothNei = AccumAt(delta, gcx, gcz + 1);
                Assert.Less(Mathf.Abs(smoothAcc), 3f,
                    $"同一笔内磨 20 次，中心格累积增量必须收敛（实得 {smoothAcc:0.###}m）：" +
                    "旧实现读到笔划期间不更新的旧场、每事件重复累加同一增量 ⇒ 实测 −21m = 用户说的「噪音反而变大」");
                Assert.Greater(Mathf.Abs(smoothAcc), 0.2f,
                    "累积增量近乎为 0 ⇒ 笔划根本没写入，上面那条断言是空过的");
                // 棋盘相邻格初值相反（+1 / −1）、邻居均值≈0 ⇒ 两格的累积增量必须**异号**
                //（各自朝 0 回归）。我第一版把判据写成"同号=收敛"，方向恰好反了，被自己的用例抓出（H-67）。
                Assert.Less(smoothAcc * smoothNei, 0f,
                    $"相邻格增量应异号（各自朝均值靠拢）：实得 {smoothAcc:0.###} × {smoothNei:0.###} > 0");
                Assert.Less(smoothAcc, 0f, "中心初值 +1 ⇒ 收敛增量必须为负（朝 0 回归）");
                Assert.Greater(smoothNei, 0f, "邻格初值 −1 ⇒ 收敛增量必须为正（朝 0 回归）");

                // ── 对照组：抬升同样磨 20 次，累积必须随位移增长（证明环境真的在写） ──
                delta2 = ScriptableObject.CreateInstance<SculptDeltaAsset>();
                terr.SetDelta(delta2);
                tool.GetField("Action", NP | ST).SetValue(null, SculptAction.Raise);
                begin.Invoke(null, new object[] { center });
                for (var i = 1; i <= 20; i++)
                {
                    var off = (i % 2 == 0 ? -0.6f : 0.6f);
                    drag.Invoke(null, new object[] { new Vector3(center.x + off, 0f, center.z) });
                }
                end.Invoke(null, null);
                var raiseAcc = AccumAt(delta2, gcx, gcz);
                Assert.Greater(raiseAcc, 5f,
                    $"对照组抬升应累出可观高度（实得 {raiseAcc:0.##}m）；若不成立说明平滑那组的『小值』是没写入的假绿");

                Debug.Log($"[M4l] 平滑 20 事件累积 {smoothAcc:0.###}m（收敛，邻格 {smoothNei:0.###}）；" +
                          $"对照抬升 {raiseAcc:0.##}m（正常累加）");
            }
            finally
            {
                foreach (var k in keys) tool.GetField(k, NP | ST).SetValue(null, save[k]);
                Object.DestroyImmediate(go);
                if (delta != null) Object.DestroyImmediate(delta, true);
                if (delta2 != null) Object.DestroyImmediate(delta2, true);
                Object.DestroyImmediate(proj, true);
                Object.DestroyImmediate(prof, true);
            }
        }
    }
}
