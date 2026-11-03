using NUnit.Framework;
using UnityEngine;

namespace Collin.CityMap.Tests
{
    /// <summary>
    /// Modules/01 数据层（S1a 基底 + S1b 平整约束）的 EditMode 断言。
    /// <para>地形最危险的失败模式是"看着能跑、性质不成立"：断言写在数据层而不是 mesh 层，
    /// 因为 mesh 出错时无法区分是几何问题还是求解问题。</para>
    /// </summary>
    public class TerrainDataTests
    {
        /// <summary>
        /// 本类每个用例都会创建 ScriptableObject / GameObject，而 <c>CityTerrain.OnEnable</c>
        /// 会把自己登记进 <c>IntentRegistry</c>。<b>不销毁就会累积</b> —— 后续用例里
        /// <c>UndoHistoryTracker</c> 首帧基线、集合哈希计数等断言会被前面泄漏的对象污染。
        /// </summary>
        readonly System.Collections.Generic.List<Object> _spawned
            = new System.Collections.Generic.List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (var i = _spawned.Count - 1; i >= 0; i--)
            {
                var o = _spawned[i];
                if (o != null) Object.DestroyImmediate(o, true);
            }
            _spawned.Clear();
        }

        T Track<T>(T o) where T : Object { _spawned.Add(o); return o; }

        // ───────────────────── 噪声内核（00 §3.2） ─────────────────────

        [Test]
        public void Noise_Is_Pure_And_Within_Range()
        {
            const ulong seed = 0xC17FEEUL;
            for (var i = 0; i < 500; i++)
            {
                var x = i * 0.137f - 30f;
                var z = i * -0.211f + 12f;
                var a = CityNoise.Noise2D(seed, x, z);
                var b = CityNoise.Noise2D(seed, x, z);
                Assert.AreEqual(a, b, $"同输入必须同值（x={x},z={z}）");
                Assert.GreaterOrEqual(a, 0f);
                Assert.LessOrEqual(a, 1f);
            }
        }

        [Test]
        public void Noise_Is_Not_Too_Periodic_Or_Degenerate()
        {
            var mean = 0f;
            var min = 2f; var max = -1f;
            for (var i = 0; i < 4000; i++)
            {
                var v = CityNoise.Noise2D(7, i * 0.37f, i * 0.113f);
                mean += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            mean /= 4000f;
            Assert.Greater(max - min, 0.5f, "取值必须真正铺开（退化常数噪声会让地形变平地而不被发现）");
            Assert.Greater(mean, 0.3f); Assert.Less(mean, 0.7f);
        }

        [Test]
        public void Fbm_Respects_Octave_And_Wavelength_Settings()
        {
            // 波长越大 → 空间变化越慢：相邻格差值应显著小于短波长情形
            float Variance(float lambda)
            {
                var acc = 0f; var prev = CityNoise.SignedFbm(3, 0f, 0f, lambda, 4, 0.5f, 2f);
                for (var i = 1; i < 400; i++)
                {
                    var v = CityNoise.SignedFbm(3, i * 1f, i * 0.5f, lambda, 4, 0.5f, 2f);
                    acc += Mathf.Abs(v - prev); prev = v;
                }
                return acc / 399f;
            }
            Assert.Less(Variance(900f), Variance(40f), "长波长必须比短波长更平滑（01 §3.1 的 λ 语义）");

            Assert.AreEqual(0f, CityNoise.Fbm(1, 5f, 5f, 100f, 0, 0.5f, 2f), "0 八度必须返回 0");
            Assert.GreaterOrEqual(CityNoise.Fbm(1, 5f, 5f, 100f, 3, 0.5f, 1f), 0f,
                "lacunarity≤1 被钳住而非死循环/NaN");
            Assert.IsTrue(float.IsFinite(CityNoise.Fbm(1, 1e6f, -1e6f, 100f, 5, 0.5f, 2f)));
        }

        [Test]
        public void DomainWarp_Changes_Sample_But_Is_Pure()
        {
            float x1 = 137f, z1 = 89f;
            CityNoise.DomainWarp(9, ref x1, ref z1, 120f, 500f);
            Assert.AreNotEqual(137f, x1, "warp 必须真的位移采样点，否则 §3.1 的破网格感不成立");

            float x2 = 137f, z2 = 89f;
            CityNoise.DomainWarp(9, ref x2, ref z2, 120f, 500f);
            Assert.AreEqual(x1, x2); Assert.AreEqual(z1, z2);

            float x3 = 5f, z3 = 5f;
            CityNoise.DomainWarp(9, ref x3, ref z3, 0f, 500f);
            Assert.AreEqual(5f, x3, "amp=0 必须短路（零开销路径）");
        }

        // ───────────────── S1a 基底（01 §3.1） ─────────────────

        [Test]
        public void A1_Same_Seed_Produces_Bitwise_Identical_Base()
        {
            var f1 = RunBase(1700, out var p1);
            var f2 = RunBase(1700, out var p2);
            Assert.AreEqual(p1.Grid.CellCount, f1.BaseHeights.Length);
            for (var i = 0; i < f1.BaseHeights.Length; i++)
                Assert.AreEqual(f1.BaseHeights[i], f2.BaseHeights[i], $"格 {i} 高度必须逐位相同（A-1）");
        }

        [Test]
        public void A1_Different_Seed_Changes_Terrain()
        {
            var a = RunBase(1700, out var pa);
            var b = RunBase(1701, out var pb);
            var diff = 0;
            for (var i = 0; i < a.BaseHeights.Length; i++)
                if (!Mathf.Approximately(a.BaseHeights[i], b.BaseHeights[i])) diff++;
            Assert.Greater(diff, a.BaseHeights.Length / 4, "换种子必须真正改变地形（否则种子形同虚设）");
        }

        [Test]
        public void A1_1_UrbanFlatness_Controls_Inner_Roughness()
        {
            // flatness=0 → 城内几乎全平；=1 → 接近裸噪声幅度
            // 对象生命周期由 [TearDown] 统一负责，用例内不得手写 DestroyImmediate
            var flat = RunBase(99, out _, urbanFlatness: 0f);
            var rough = RunBase(99, out _, urbanFlatness: 1f);

            // 取样半宽（格数）。必须与【波长量级】匹配：窗远小于 λ 时低频根本展不开，
            // 拿它断言"起山"是我的口径错（M2 实测：120m 窗 / λ=520m ⇒ 仅 0.73m）。
            var windowHalf = 30;

            float Std(TerrainField f)
            {
                float sum = 0f, sum2 = 0f;
                var n = 0;
                var cols = f.Grid.Columns;
                var half = Mathf.Min(windowHalf, Mathf.Min(cols, f.Grid.Rows) / 2 - 2);
                for (var cz = f.Grid.Rows / 2 - half; cz < f.Grid.Rows / 2 + half; cz++)
                for (var cx = cols / 2 - half; cx < cols / 2 + half; cx++)
                {
                    var h = f.BaseHeights[cz * cols + cx];
                    sum += h; sum2 += h * h; n++;
                }
                var m = sum / n;
                return Mathf.Sqrt(Mathf.Max(0f, sum2 / n - m * m));
            }

            var sFlat = Std(flat);
            var sRough = Std(rough);
            // A-1-1 文档硬数值：flatness=0 时城内标准差 ≤0.2m。
            // 上一版我把阈值写成 0.6f（凭"还剩细节噪声"的直觉放宽），结果**实现真的有缺陷**：
            // ampFactor 只乘低频层、细节层 1.2m 完全绕过旋钮 ⇒ 实测 0.236m **超出文档口径**，
            // 却被我的宽松断言放过去了（H-37）。教训：**文档给了绝对数值，断言就必须用那个数值**。
            Assert.LessOrEqual(sFlat, 0.2f,
                $"A-1-1：flatness=0 时城内标准差应 ≤0.2m（实得 {sFlat:0.000}）");

            // 旋钮语义是"城内有多平"，必须**单调且线性**：u=0 处 ampFactor=flatness ⇒ 城内标准差 ≈ flatness×裸值。
            // 只断言两端（0 与 1）会漏掉"中间段失效/非线性"这一类面板拖动不跟手的缺陷。
            Assert.Greater(sRough, 0.3f, $"flatness=1 应接近裸噪声幅度（实得 {sRough:0.000}）");
            Assert.Greater(sRough, sFlat * 2.5f, "flatness=1 必须显著更起伏，否则旋钮无效");

            // 城内平台区（u < flatnessInnerEdge）里 ampFactor 恒等于 flatness ⇒ 标准差必须**线性**跟随旋钮。
            // 只断言 0 与 1 两端会漏掉"中间段不跟手/非线性"这类面板拖动缺陷，所以补 0.5 中点。
            var mid = RunBase(99, out _, urbanFlatness: 0.5f);
            var sMid = Std(mid);
            Assert.AreEqual(sRough * 0.5f, sMid, sRough * 0.06f,
                $"flatness=0.5 的城内标准差应为裸值的 0.5 倍（期望 {sRough * 0.5f:0.000}，实得 {sMid:0.000}）");

            // 比值口径不严格：flatness=0 时城内仍含细节噪声（1.2m），它构成 sFlat 的地板，
            // 使比值天然被压到 3~4 倍。要干净量「低频起伏被压掉多少」必须锁掉细节再测
            // —— 这是修正断言口径，不是放宽阈值凑绿。
            // 图幅取 2400：城半径 = 0.45×2400 = 1080m，±200 格 = 400m 半宽 ⇒
            //   窗角 u = √(400²+400²)/1080 ≈ 0.52 < flatnessInnerEdge(0.6) ⇒ 全程在城内压幅区，
            //   不混入城外原始起伏；同时 800m 跨度 ≈ 1.5λ 足以让低频展开。
            const int lowHalf = 200;
            var h0 = Harness(99, out var s0, worldSize: 2400f, flatness: 0f);
            var h1 = Harness(99, out var s1, worldSize: 2400f, flatness: 1f);
            Assert.IsTrue(s0.Profile.SetRoughnessLock(true), "锁细节必须视为变化并 bump");
            s1.Profile.SetRoughnessLock(true);
            RunS1a(h0, s0); RunS1a(h1, s1);
            windowHalf = lowHalf;
            var lowFlat = Std(h0.Field);
            var lowRough = Std(h1.Field);

            // 先证明测试自身有效：λ 量级窗口内的裸低频必须真的铺开。
            // 阈值取 1.0m 而非 2.0m：fbm 标准差我只能手算到 ≈0.12×18m≈2.2m 的量级，
            // 拿 2.0m 断言余量仅 10%，会因估算误差随机红。1.0m 仍是"细节锁掉后确实有低频"
            // 的有效下限（裸低频若被误压会直接掉到 0.1m 以下），且不与下面的比值断言重复。
            Assert.Greater(lowRough, 1f,
                $"λ 量级窗口的裸低频标准差应达米级（实得 {lowRough:0.###}m；不足则窗口/图幅选错）");
            Assert.Less(lowFlat, 0.08f,
                $"flatness=0 且锁细节后城内低频起伏应≈0（实得 {lowFlat:0.000}m）");
            Assert.Greater(lowRough, lowFlat * 10f,
                $"低频起伏必须被 flatness 真正压掉（{lowFlat:0.000} vs {lowRough:0.000}）");
            Debug.Log($"[M2] urbanFlatness 含细节 {sFlat:0.###}→{sRough:0.###}m；" +
                      $"锁细节纯低频(800m 窗) {lowFlat:0.####}→{lowRough:0.###}m");
        }

        [Test]
        public void A0_1_Base_Is_Locally_Stable_Per_Cell_Seed()
        {
            // 01 用固定全局种子（按位置派生的 noise 本身就是空间稳定的）：
            // 验证"改世界尺寸不改变原点附近已有格的高度"——增量重算的前提
            // 局部稳定性针对【噪声与已固定意图】：城心默认取图幅中心，因此扩图幅必然改变压幅场。
            // 要验证"原有格不被新图幅改变"，必须先把城心与半径钉成同一意图（锚点），
            // 这才是 00 D-10 承诺的适用场景（图幅扩大属 World 脏 ⇒ 允许全量重算）。
            var sa = Harness(4242, out var ps, worldSize: 600f);
            Assert.IsTrue(sa.P.TrySetCenterAnchor(new Vector2(300f, 300f), 270f, "fixed"));
            var sb = Harness(4242, out var pb, worldSize: 1200f);
            Assert.IsTrue(sb.P.TrySetCenterAnchor(new Vector2(300f, 300f), 270f, "fixed"));
            RunS1a(sa, ps); RunS1a(sb, pb);
            var small = sa.Field; var big = sb.Field;
            var cols = ps.Grid.Columns;
            var mismatch = 0;
            for (var cz = 2; cz < ps.Grid.Rows - 2; cz++)
            for (var cx = 2; cx < cols - 2; cx++)
            {
                // small/big 是 TerrainField（RunBase 返回场）；ps/pb 是 Setup（持有各自格网）
                var a = small.BaseHeights[cz * cols + cx];
                var b = big.BaseHeights[cz * pb.Grid.Columns + cx];
                if (!Mathf.Approximately(a, b)) mismatch++;
            }
            Assert.Greater(cols, 4, "两档格数必须足够大，否则循环没有实际样本");
            Assert.AreNotEqual(ps.Grid.CellCount, pb.Grid.CellCount, "图幅不同 ⇒ 格数必须不同，否则断言无意义");
            Assert.AreEqual(0, mismatch, "图幅扩大不得改变原有格的高度（局部稳定性，00 D-10）");
        }

        [Test]
        public void Cancellation_Stops_S1a_Without_Partial_Garbage_Being_Reported_Ok()
        {
            var h = Harness(1700, out var p);
            h.Ctx.RequestCancel();
            var res = new TerrainBaseStage(p.Profile, p.Project).Run(h.Ctx, RunMode.Commit);
            Assert.IsTrue(res.Cancelled, "进入即取消必须报 Cancelled");
            Assert.IsFalse(res.Success);
            h.Dispose();
        }

        // ─────────────── 平面拟合（01 §3.5） ───────────────

        [Test]
        public void A1_4_Fit_Preserves_Gentle_Slope_But_Flattens_Steep_Area()
        {
            // 造两个区域：缓坡（3°）与陡坡（20°），断言目标平面分别"用拟合"与"退化为水平"
            var field = new TerrainField(new CityGrid(200f, 2f, 8f, 20f, 100f, 0f));
            Fill(field, (x, z) => 0.05f * x);                      // 约 2.9° 缓坡
            Assert.IsTrue(PlaneFitting.FitFromBase(field, new Vector2(100f, 100f), new Vector2(30f, 30f),
                              5f, 12f, 12f, 1f, FlattenKind.Parcel, out var gentle));
            Assert.Less(Vector3.Angle(Vector3.up, gentle.Normal), 5f,
                "缓坡必须保留坡向（用拟合面而非水平面，01 §3.5）");

            Fill(field, (x, z) => 0.4f * x);                       // 约 21.8° 陡坡
            Assert.IsTrue(PlaneFitting.FitFromBase(field, new Vector2(100f, 100f), new Vector2(30f, 30f),
                              5f, 12f, 12f, 1f, FlattenKind.Parcel, out var steep));
            Assert.AreEqual(0f, steep.Normal.x, 1e-4f);
            Assert.AreEqual(1f, steep.Normal.y, 1e-4f,
                "超阈值必须退化为水平面（否则地块放不下建筑）");
        }

        [Test]
        public void Fit_Plane_Passes_Through_Center_Elevation()
        {
            // 平面自洽性：d 必须由法线与该点高程算出。
            // 曾用"中位数 + 倾斜法线"混配，导致整块偏一个常数（M2 自查抓到）
            var field = new TerrainField(new CityGrid(200f, 2f, 8f, 20f, 100f, 0f));
            Fill(field, (x, z) => 0.04f * x - 0.03f * z + 11f);
            var center = new Vector2(100f, 100f);
            Assert.IsTrue(PlaneFitting.FitFromBase(field, center, new Vector2(30f, 30f),
                              20f, 12f, 12f, 1f, FlattenKind.Parcel, out var plane));

            var expected = field.GetBaseHeight(center.x, center.y);
            Assert.AreEqual(expected, plane.HeightAt(center.x, center.y), 0.35f,
                "目标平面在区域中心的高程必须≈该处基底（否则施加后整块被抬/压）");
        }

        [Test]
        public void Fit_Median_Is_Robust_To_Single_Outlier()
        {
            // 中位数而非均值：残缺多边形/角落极值会把均值拉偏（01 §3.5 的实测理由）
            var g = new CityGrid(200f, 2f, 8f, 20f, 100f, 0f);
            var clean = new TerrainField(g);
            Fill(clean, (x, z) => 5f);
            var dirty = new TerrainField(g);
            Fill(dirty, (x, z) => 5f);
            var cols = g.Columns;
            dirty.BaseHeights[50 * cols + 50] = 400f;              // 单点离群

            Assert.IsTrue(PlaneFitting.FitFromBase(clean, new Vector2(100f, 100f), new Vector2(20f, 20f),
                              5f, 12f, 12f, 1f, FlattenKind.Parcel, out var p1));
            Assert.IsTrue(PlaneFitting.FitFromBase(dirty, new Vector2(100f, 100f), new Vector2(20f, 20f),
                              5f, 12f, 12f, 1f, FlattenKind.Parcel, out var p2));
            Assert.AreEqual(p1.HeightAt(100f, 100f), p2.HeightAt(100f, 100f), 0.2f,
                "单点离群不得改变目标高程（均值口径会偏 ~7.8m）");
        }

        [Test]
        public void Fit_Rejects_Under_Sampled_Region()
        {
            var field = new TerrainField(new CityGrid(200f, 2f, 8f, 20f, 100f, 0f));
            Assert.IsFalse(PlaneFitting.FitFromBase(field, new Vector2(100f, 100f), new Vector2(0.5f, 0.5f),
                             5f, 12f, 12f, 1f, FlattenKind.Parcel, out _),
                "区域过小 ⇒ 每轴采样点不足 ⇒ 必须 false（否则最小二乘退化为过角点插值，M2 实测坡率 40°）");
        }

        // ───────────── 约束权重衰减（00 §3.3 唯一口径） ─────────────

        [Test]
        public void Constraint_Full_Weight_Inside_And_Zero_Beyond_Band()
        {
            var plane = new PlaneConstraint(Vector3.up, 10f, new Vector2(100f, 100f),
                                            new Vector2(30f, 30f), 12f, 12f, 1f, true, 1f, FlattenKind.Parcel);
            // 几何由公共算法给出（inset=12 作用在 half=30 上，上限 0.4×30=12 ⇒ 核心半尺寸 18）
            var core = plane.CoreHalfSize;
            Assert.AreEqual(18f, core.x, 1e-4f, "核心必须被内切（inset 生效），否则相邻地块边界会互相顶成陡坎");
            Assert.AreEqual(24f, plane.ExtendMeters, 1e-4f, "有效衰减长 = 内切段 + 外延段");

            Assert.AreEqual(1f, plane.WeightAt(100f, 100f), 1e-4f, "核心内必须全权重");
            Assert.AreEqual(1f, plane.WeightAt(100f + core.x - 0.2f, 100f), 1e-4f, "核心内侧全权重");
            // 半格容差：核心边界外半格内仍算核心（格中心毛刺不得掉进过渡带）
            Assert.AreEqual(1f, plane.WeightAt(100f + core.x + 0.9f, 100f), 1e-4f,
                "含半格容差，边缘格不掉进过渡带（A-1-3 前提）");

            Assert.AreEqual(0f, plane.WeightAt(100f + core.x + plane.ExtendMeters + 0.1f, 100f), 1e-4f,
                "衰减带之外必须严格为 0");

            var mid = plane.WeightAt(100f + core.x + plane.ExtendMeters * 0.5f, 100f);
            Assert.Greater(mid, 0f); Assert.Less(mid, 1f, "带中点必须严格介于两者之间");

            float prev = 2f;
            for (var t = 0f; t <= 1.0001f; t += 0.05f)
            {
                var w = plane.WeightAt(100f + core.x + t * plane.ExtendMeters, 100f);
                Assert.LessOrEqual(w, prev + 1e-5f, "过渡带必须单调衰减（不出现台阶反弹）");
                prev = w;
            }
        }

        [Test]
        public void Constraint_Affected_Bounds_Covers_Decay_Band()
        {
            var plane = new PlaneConstraint(Vector3.up, 0f, new Vector2(500f, 400f),
                                            new Vector2(30f, 20f), 12f, 12f, 1f, true, 1f, FlattenKind.Parcel);
            var b = plane.GetAffectedBounds();
            Assert.Greater(b.extents.x, 30f + 12f, "外接盒必须含过渡带，否则局部重算会漏格");
            Assert.Greater(b.extents.z, 20f + 12f);
            Assert.AreEqual(0f, plane.WeightAt(500f + b.extents.x + 1f, 400f), 1e-4f,
                "外接盒之外权重必须为 0（局部性的数学保证）");
        }

        [Test]
        public void Radial_Mode_Uses_Inner_Radius_From_HalfSize_X()
        {
            var radial = new PlaneConstraint(Vector3.up, 3f, new Vector2(200f, 200f),
                                             new Vector2(25f, 25f), 0f, 10f, 1f, false, 2f, FlattenKind.WaterBuffer);
            Assert.AreEqual(2f, radial.WeightAt(200f, 200f), 1e-4f, "核心内取 Weight");
            Assert.AreEqual(2f, radial.WeightAt(225f, 200f), 1e-4f);
            Assert.AreEqual(0f, radial.WeightAt(235.5f, 200f), 1e-4f);
        }

        // ───────────────── S1b 求解（01 §3.4） ─────────────────

        [Test]
        public void A1_3_Constrained_Parcel_Becomes_Within_Slope_Limit()
        {
            var h = Harness(1700, out var p, worldSize: 1200f, flatness: 1f);
            p.Terrain.SetAutoFlatten(true, 3);
            RunS1a(h, p);
            var before = MaxSlopeInParcelCores(p, h.Field);

            var s1b = new FlattenSolveStage(p.Profile, p.Terrain);
            var res = s1b.Run(h.Ctx, RunMode.Commit);
            Assert.IsTrue(res.Success);
            Assert.Greater(res.Outputs, 0, "必须真的生成了约束");

            var after = MaxSlopeInParcelCores(p, h.Field);
            // 打点是为了给 ACCEPTANCE 留下可核对的实测数值：H-34/H-35 的修复效果只能靠数字证明。
            Debug.Log($"[M2] A1_3 地块核心最大坡度 {before:0.##}° → {after:0.##}°" +
                      $"（红线 {p.Profile.MaxParcelSlope:0.#}°，约束 {res.Outputs} 条，改动 {res.CellsTouched} 格）");
            Assert.Less(after, before, $"约束后地块内最大坡度必须下降（{before:0.##}° → {after:0.##}°）");
            Assert.LessOrEqual(after, p.Profile.MaxParcelSlope + 1.5f,
                $"A-1-3：地块内坡度 ≤ maxParcelSlope + 容差（实得 {after:0.##}°，上限 {p.Profile.MaxParcelSlope}°）");
            h.Dispose();
        }

        [Test]
        public void A1_2_S1b_Is_Idempotent_No_Drift_On_Rerun()
        {
            // 我自己在代码注释里点出的正反馈风险：就地叠加会逐轮漂移。这条断言把它钉死。
            var h = Harness(777, out var p, worldSize: 1200f, flatness: 1f);
            p.Terrain.SetAutoFlatten(true, 3);
            RunS1a(h, p);
            var s1b = new FlattenSolveStage(p.Profile, p.Terrain);

            s1b.Run(h.Ctx, RunMode.Commit);
            var first = (float[])h.Field.Heights.Clone();
            s1b.Run(h.Ctx, RunMode.Commit);
            var second = h.Field.Heights;

            var worst = 0f;
            for (var i = 0; i < first.Length; i++)
                worst = Mathf.Max(worst, Mathf.Abs(first[i] - second[i]));
            Assert.AreEqual(0f, worst, 1e-5f,
                "S1b 跑两次结果必须完全相同（不幂等 = 每次保存场景地形都在悄悄变化）");
            h.Dispose();
        }

        [Test]
        public void A1_7_Change_Far_From_Constraint_Leaves_Remote_Cells_Bitwise_Identical()
        {
            var h = Harness(31337, out var p, worldSize: 1600f, flatness: 1f);
            RunS1a(h, p);
            var baseline = (float[])h.Field.Heights.Clone();

            // 在城心附近加一个人工平整区
            var c = new Vector2(p.Grid.WorldSize * 0.5f, p.Grid.WorldSize * 0.5f);
            p.Terrain.AddZone(c, new Vector2(120f, 120f));
            new FlattenSolveStage(p.Profile, p.Terrain).Run(h.Ctx, RunMode.Commit);

            var far = FarthestCellIndex(p.Grid, c);
            Assert.AreEqual(baseline[far], h.Field.Heights[far], 0f,
                "远端格必须逐位不变（支撑 A-2 与增量重算的合法性）");

            var near = p.Terrain.LastCells;
            Assert.Greater(near, 0);
            Assert.Less(near, baseline.Length / 2,
                "被改格数必须远小于全图，否则'只遍历外接盒'没生效");
            h.Dispose();
        }

        [Test]
        public void Two_Instances_Do_Not_Share_Scratch_Buffers()
        {
            // 静态 scratch 会让两个城市上下文串数据（M2 自查抓到）。用两个 stage 实例交叉跑验证。
            var hA = Harness(501, out var pA, worldSize: 800f, flatness: 1f);
            var hB = Harness(502, out var pB, worldSize: 800f, flatness: 1f);
            pA.Terrain.SetAutoFlatten(true, 2);
            RunS1a(hA, pA); RunS1a(hB, pB);

            var sA = new FlattenSolveStage(pA.Profile, pA.Terrain);
            var sB = new FlattenSolveStage(pB.Profile, pB.Terrain);
            sA.Run(hA.Ctx, RunMode.Commit);
            sB.Run(hB.Ctx, RunMode.Commit);
            var bAlone = (float[])hA.Field.Heights.Clone();

            // 交错执行后再跑 A，结果必须与单独跑一致（缓冲未污染）
            sB.Run(hB.Ctx, RunMode.Commit);
            sA.Run(hA.Ctx, RunMode.Commit);
            var worst = 0f;
            for (var i = 0; i < bAlone.Length; i++)
                worst = Mathf.Max(worst, Mathf.Abs(bAlone[i] - hA.Field.Heights[i]));
            Assert.AreEqual(0f, worst, 1e-5f, "两个上下文不得串用 scratch 缓冲");

            hA.Dispose(); hB.Dispose();
        }

        [Test]
        public void Absolute_Height_Zone_Holds_Target_Elevation()
        {
            var h = Harness(808, out var p, worldSize: 800f, flatness: 1f);
            RunS1a(h, p);
            var c = new Vector2(400f, 400f);
            const float target = 7.5f;
            p.Terrain.AddZone(c, new Vector2(80f, 80f), target);

            new FlattenSolveStage(p.Profile, p.Terrain).Run(h.Ctx, RunMode.Commit);
            Assert.AreEqual(target, h.Field.GetHeight(c.x, c.y), 0.25f,
                "绝对高程模式必须真的落到指定值（±半格容差）");
            Assert.Greater(p.Terrain.LastConstraints, 0);
            h.Dispose();
        }

        [Test]
        public void Zero_Weight_Zone_Is_Ignored_And_Counted()
        {
            var h = Harness(1234, out var p, worldSize: 800f, flatness: 1f);
            RunS1a(h, p);
            var id = p.Terrain.AddZone(new Vector2(400f, 400f), new Vector2(60f, 60f));
            Assert.IsTrue(p.Terrain.UpdateZone(id, new Vector2(400f, 400f), new Vector2(60f, 60f),
                                              12f, 0.05f, 0f, true), "改权重必须视为变化");
            new FlattenSolveStage(p.Profile, p.Terrain).Run(h.Ctx, RunMode.Commit);
            Assert.Greater(p.Terrain.LastConstraints, 0, "极小权重仍应参与（不是被丢弃）");
            Assert.IsTrue(p.Terrain.UpdateZone(id, new Vector2(400f, 400f), new Vector2(60f, 60f),
                                              12f, 0.05f, 0f, false), "禁用是变化");
            Assert.AreEqual(0, p.Terrain.LastRejectedConstraints, "禁用不计入跳过数");
            h.Dispose();
        }

        [Test]
        public void S1b_Without_Profile_Fails_Loudly_With_Reason()
        {
            var h = Harness(1, out var p);
            var res = new FlattenSolveStage(null, p.Terrain).Run(h.Ctx, RunMode.Commit);
            Assert.IsFalse(res.Success, "缺 Model 必须失败");
            Assert.IsFalse(res.Cancelled);
            Assert.IsNotNull(res.Message, "必须带原因，不得静默返回空结果");
            Assert.AreNotEqual(string.Empty, res.Message);
        }

        [Test]
        public void Constraint_Strength_Makes_Parcel_Core_Almost_Fully_Adherent()
        {
            // A-1-3 的根因守卫：若所有种类都用 w=1，配合 Wfree=0.6 核心只贴合 62.5%，
            // 21° 坡地残留 8° → 地块放不下建筑。强度分级必须真的起作用。
            var free = 0.6f;
            var parcel = ConstraintStrength.AdherenceAtCore(FlattenKind.Parcel, 1f, free);
            var weak = ConstraintStrength.AdherenceAtCore(FlattenKind.WaterBuffer, 1f, free);
            Assert.Greater(parcel, 0.9f, $"地块核心贴合度必须 ≥0.9（实得 {parcel:0.###}）");
            Assert.Less(weak, parcel, "弱约束贴合度必须低于地块（否则'强弱'无意义）");
            // 注意：贴合度是比值且趋于饱和，不能按强度倍率(8 vs 1)线性放大来断言。
            // 真正要钉的是强度本身的比例关系：
            Assert.AreEqual(8f, ConstraintStrength.For(FlattenKind.Parcel), 1e-4f);
            Assert.AreEqual(1f, ConstraintStrength.For(FlattenKind.WaterBuffer), 1e-4f);
            Assert.GreaterOrEqual(parcel, 0.9f * ConstraintStrength.For(FlattenKind.Parcel)
                                  / (ConstraintStrength.For(FlattenKind.Parcel) + free));

            // 用户权重是乘数：减半必须降低贴合度（面板上的 weight 滑杆有实际效果）
            Assert.Less(ConstraintStrength.AdherenceAtCore(FlattenKind.Plaza, 0.5f, free),
                        ConstraintStrength.AdherenceAtCore(FlattenKind.Plaza, 1f, free));

            // 拟合结果必须已乘强度：与直接构造 w=1 的约束相比，等效权重更大
            var fitted = FitOnFlat(out _);
            Assert.Greater(fitted.Weight, ConstraintStrength.Parcel * 0.99f,
                "FitFromBase 的 weight 必须已含种类强度");
            // 绝对值核对：用户乘数 1 × 强度 8 = 8
            Assert.AreEqual(8f, fitted.Weight, 1e-3f);
        }

        static PlaneConstraint FitOnFlat(out bool ok)
        {
            var field = new TerrainField(new CityGrid(200f, 2f, 8f, 20f, 100f, 0f));
            ok = PlaneFitting.FitFromBase(field, new Vector2(100f, 100f), new Vector2(30f, 30f),
                                          5f, 12f, 12f, 1f, FlattenKind.Parcel, out var p);
            return p;
        }

        // ───────────────────── 采样与差分（§2.4/§3.3） ─────────────────────

        [Test]
        public void Sample_Is_Continuous_Between_Cell_Centers()
        {
            var field = new TerrainField(new CityGrid(100f, 2f, 8f, 20f, 100f, 0f));
            for (var i = 0; i < field.Heights.Length; i++) field.Heights[i] = i * 0.5f;
            var a = field.GetHeight(1f, 1f);
            var b = field.GetHeight(1f + 1f, 1f);
            Assert.AreNotEqual(a, b, "格中心之间必须可插值（否则双线性无效）");
            // 恰在格中心 → 必须等于该格值
            var g = field.Grid;
            Assert.AreEqual(field.Heights[10 * g.Columns + 5], g.CellCenterWorld(5, 10).x > 0f
                ? field.GetHeight(g.CellCenterWorld(5, 10).x, g.CellCenterWorld(5, 10).y) : 0f, 1e-4f,
                "格中心采样必须回到该格值");
        }

        [Test]
        public void Normal_And_Slope_Agree_With_Known_Plane()
        {
            var field = new TerrainField(new CityGrid(200f, 2f, 8f, 20f, 100f, 0f));
            var g = field.Grid;
            for (var z = 0; z < g.Rows; z++)
            for (var x = 0; x < g.Columns; x++)
                field.Heights[z * g.Columns + x] = 0.1f * (g.CellCenterWorld(x, z).x);   // 沿 X 斜率 0.1

            Assert.AreEqual(Mathf.Atan(0.1f) * Mathf.Rad2Deg, field.GetSlopeDeg(100f, 100f), 0.15f,
                "差分坡度必须匹配解析值（显示 mesh 与此同口径，01 §3.3）");
            // 上坡方向法线向后倾（约定 n = (-dh/dx, 1, -dh/dz)），断言符号而非绝对值
            Assert.Less(field.GetNormal(100f, 100f).x, -0.09f);
            Assert.Less(field.GetNormal(100f, 100f).z, 1e-4f, "Z 方向无坡度则法线 Z 分量应为 0");
        }

        [Test]
        public void Quant_Roundtrip_Losses_Stay_Within_One_Step()
        {
            foreach (var v in new[] { 0f, 12.345f, -7.006f, 300f, -300f })
                Assert.AreEqual(v, TerrainField.DecodeQuant(TerrainField.EncodeQuant(v)), 0.011f,
                    $"int16 量化往返误差必须 ≤1 步（{v}）");
            Assert.AreEqual(short.MaxValue, TerrainField.EncodeQuant(99999f), "超量程必须钳位而非环绕");
            Assert.AreEqual(short.MinValue, TerrainField.EncodeQuant(-99999f));
        }

        [Test]
        public void EnsureField_Rebuilds_When_Grid_Size_Changes()
        {
            var p = MakeSet(600f, 4242, 0.25f, out var profile);

            var ctx = new PipelineContext(p);
            var f1 = TerrainBaseStage.EnsureField(ctx);
            Assert.AreEqual(f1, TerrainBaseStage.EnsureField(ctx), "同尺寸必须复用（避免每帧重分配）");

            Assert.IsTrue(p.SetWorldParams(800f, WorldScale.Small2km, 60f, 20f, 100f, 100f));
            var ctx2 = new PipelineContext(p);
            var f2 = TerrainBaseStage.EnsureField(ctx2);
            Assert.AreNotSame(f1, f2, "尺寸变化必须重建数组");
            Assert.AreEqual(ctx2.Grid.CellCount, f2.Heights.Length);

            // 新格网的数组长度必须等于新格数（旧数组若被复用会在这里暴露）
            Assert.AreNotEqual(f1.Heights.Length, f2.Heights.Length, "两档格数必须不同，否则断言无意义");
        }

        // ───────────────────────── 测试工具 ─────────────────────────
        //
        // 生命周期约定：本类所有 Object（ScriptableObject / GameObject）一律经 Track() 登记，
        // 由 [TearDown] 统一 DestroyImmediate。用例内不再手写销毁 ——
        // 手写版曾漏掉 CityTerrain 的 GameObject，导致其残留在 IntentRegistry 中，
        // 让 UndoHistoryTracker 的基线/计数断言受前序用例污染。

        struct Handle
        {
            public PipelineContext Ctx;
            public TerrainField Field;
            public GameObject Go;
            public CityProject P;
            public TerrainProfile Profile;
            public void Dispose()
            {
                // 幂等：TearDown 也会销毁；这里只处理用例中途主动释放的情形
                if (P != null) Object.DestroyImmediate(P, true);
                if (Profile != null) Object.DestroyImmediate(Profile, true);
                if (Go != null) Object.DestroyImmediate(Go, true);
            }
        }

        Handle Harness(ulong seed, out Setup p, float worldSize = 600f, float flatness = 0.25f)
        {
            var proj = MakeSet(worldSize, seed, flatness, out var profile);
            var go = Track(new GameObject("terrain_" + seed));
            var terr = go.AddComponent<CityTerrain>();
            terr.SetProfile(profile);

            var ctx = new PipelineContext(proj);
            p = new Setup { Project = proj, Profile = profile, Terrain = terr, Grid = ctx.Grid, Go = go };
            return new Handle
            {
                Ctx = ctx,
                Field = TerrainBaseStage.EnsureField(ctx),
                Go = go, P = proj, Profile = profile,
            };
        }

        struct Setup
        {
            public CityProject Project;
            public TerrainProfile Profile;
            public CityTerrain Terrain;
            public CityGrid Grid;
            public GameObject Go;
        }

        CityProject MakeSet(float worldSize, ulong seed, float flatness, out TerrainProfile profile)
        {
            var proj = Track(ScriptableObject.CreateInstance<CityProject>());
            proj.EnsureRegistered();
            proj.SetWorldParams(worldSize, WorldScale.Small2km, 60f, 20f, 100f, 100f);
            proj.SetSeed(seed);

            profile = Track(ScriptableObject.CreateInstance<TerrainProfile>());
            profile.SetBaseShape(18f, 520f, 4, 0.5f, 2f, 0.35f);
            profile.SetUrbanFlatness(flatness);
            return proj;
        }

        static void RunS1a(Handle h, Setup p)
            => Assert.IsTrue(new TerrainBaseStage(p.Profile, p.Project).Run(h.Ctx, RunMode.Commit).Success);

        TerrainField RunBase(ulong seed, out Setup p, float worldSize = 600f, float urbanFlatness = 0.25f)
        {
            var h = Harness(seed, out p, worldSize, urbanFlatness);
            RunS1a(h, p);
            return h.Field;         // 数组本身无生命周期；其宿主已被 Track
        }

        static void Fill(TerrainField f, System.Func<float, float, float> h)
        {
            var g = f.Grid;
            for (var z = 0; z < g.Rows; z++)
            for (var x = 0; x < g.Columns; x++)
            {
                var c = g.CellCenterWorld(x, z);
                var v = h(c.x, c.y);
                f.BaseHeights[z * g.Columns + x] = v;
                f.Heights[z * g.Columns + x] = v;
            }
        }

        /// <summary>
        /// 地块<b>核心</b>内的最大格间坡度。
        /// <para>布局必须走 <see cref="CityTerrain.EnumerateAutoTiles"/>（求解器用的同一入口）：
        /// 若测试自己按"距城心 1.15×半径"取圆域，就会量到实现并未施加约束的过渡带与 tile 间隙，
        /// 那里坡度仍是原始值 → 断言与实现看的不是同一块地。</para>
        /// <para>只量核心（不含过渡带）：A-1-3 的"地块内坡度"语义正是核心区域。</para>
        /// </summary>
        static float MaxSlopeInParcelCores(Setup p, TerrainField f)
        {
            var g = p.Grid;
            var worst = 0f;
            var cellTol = g.CellSize * 0.5f;

            CityTerrain.EnumerateAutoTiles(p.Terrain, p.Profile, p.Project, g, (center, half) =>
            {
                // A-1-3 断言的是「地块内」坡度 = 核心区。必须用实现的公共核心算法：
                // 用整块 half 会把过渡带（权重已衰减、坡度仍是原始值）也量进来 → 假失败。
                var core = PlaneConstraint.ComputeCoreHalf(half, p.Profile.ConstraintInner);
                var x0 = Mathf.Max(1, Mathf.FloorToInt((center.x - core.x - g.Origin.x) / g.CellSize));
                var x1 = Mathf.Min(g.Columns - 2, Mathf.CeilToInt((center.x + core.x - g.Origin.x) / g.CellSize));
                var z0 = Mathf.Max(1, Mathf.FloorToInt((center.y - core.y - g.Origin.y) / g.CellSize));
                var z1 = Mathf.Min(g.Rows - 2, Mathf.CeilToInt((center.y + core.y - g.Origin.y) / g.CellSize));

                for (var cz = z0; cz <= z1; cz++)
                for (var cx = x0; cx <= x1; cx++)
                {
                    var c = g.CellCenterWorld(cx, cz);
                    if (Mathf.Abs(c.x - center.x) > core.x + cellTol) continue;
                    if (Mathf.Abs(c.y - center.y) > core.y + cellTol) continue;
                    worst = Mathf.Max(worst, f.GetSlopeDeg(c.x, c.y));
                }
            });
            return worst;
        }

        /// <summary>距给定点最远的格索引。</summary>
        static int FarthestCellIndex(CityGrid g, Vector2 from)
        {
            var best = 0; var bestD = -1f;
            for (var z = 0; z < g.Rows; z++)
            for (var x = 0; x < g.Columns; x++)
            {
                var c = g.CellCenterWorld(x, z);
                var d = (c.x - from.x) * (c.x - from.x) + (c.y - from.y) * (c.y - from.y);
                if (d > bestD) { bestD = d; best = z * g.Columns + x; }
            }
            return best;
        }
    }
}
