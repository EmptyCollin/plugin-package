using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Collin.CityMap.Tests
{
    /// <summary>
    /// Modules/00 基础设施 + Modules/11 冻结结构的 EditMode 断言。
    /// 每条对应设计文档的验收编号；证据由 `unity test run` 产出后回填 Docs/ACCEPTANCE.md。
    /// <para>纪律：预期内的 error/exception 日志必须用 <see cref="LogAssert"/> 消费，
    /// 否则测试跑完 consoleErrors&gt;0，直接违反 AGENTS §1.6b 交锁门禁。</para>
    /// </summary>
    public class FoundationTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            // ScriptableObject 实例必须显式销毁：OnDisable 会把自己从 IntentRegistry 摘掉，
            // 否则登记残留会污染后续测试的集合哈希断言。
            for (var i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i], true);
            _spawned.Clear();
        }

        private T Spawn<T>(T o) where T : Object { _spawned.Add(o); return o; }

        // ───────────────── A-45：布局自证（冻结的静态部分） ─────────────────

        [Test]
        public void A45_Record_Layout_Is_Exactly_32_Bytes_With_Frozen_Offsets()
        {
            Assert.AreEqual(BuildingRecord.LayoutSize, BuildingRecord.SizeOf, "sizeof 必须为 32");
            Assert.IsTrue(BuildingRecord.ValidateLayout(), "逐字段偏移必须与 11 §2 表一致");
            Assert.AreEqual(1, BuildingRecord.SchemaVersion);
        }

        [Test]
        public void A45_Flags_Use_Six_Bits_Of_One_Byte_As_Registered()
        {
            Assert.AreEqual(1, (int)BuildingFlags.Adopted);
            Assert.AreEqual(2, (int)BuildingFlags.UserModified);
            Assert.AreEqual(4, (int)BuildingFlags.Orphan);
            Assert.AreEqual(8, (int)BuildingFlags.Approximated);
            Assert.AreEqual(16, (int)BuildingFlags.InLockRegion);
            Assert.AreEqual(32, (int)BuildingFlags.ManualSource);

            var used = (byte)((int)BuildingFlags.Adopted | (int)BuildingFlags.UserModified
                            | (int)BuildingFlags.Orphan | (int)BuildingFlags.Approximated
                            | (int)BuildingFlags.InLockRegion | (int)BuildingFlags.ManualSource);
            Assert.AreEqual(63, used, "6 位 = 0b00111111");
            Assert.AreEqual(192, (byte)~used, "bit6..7 必须仍为保留位");
        }

        [Test]
        public void A50_Dominant_And_Threshold_Are_Purely_Derived()
        {
            var r = new BuildingRecord { LandF = 10, LandI = 70, LandR = 30, LandA = 5 };
            Assert.AreEqual(LandUseAxis.Industrial, r.Dominant);
            Assert.AreEqual((byte)70, r.ThresholdUsed, "Th = 主导维通道值（11 §2 因此删掉 thresholdUsed 字段）");

            r.LandI = 0; r.LandR = 90;
            Assert.AreEqual(LandUseAxis.Residential, r.Dominant);
            Assert.AreEqual((byte)90, r.ThresholdUsed);

            // 平手按 F>I>R>A 的稳定次序取胜：不留随机性（A-46 逐位一致的前提）
            Assert.AreEqual(LandUseAxis.Finance,
                new BuildingRecord { LandF = 50, LandI = 50, LandR = 10, LandA = 10 }.Dominant);
            Assert.AreEqual(LandUseAxis.Residential,
                new BuildingRecord { LandF = 0, LandI = 0, LandR = 50, LandA = 50 }.Dominant);
        }

        // ───────────────── A-0-1 / D-10：确定性随机 ─────────────────

        [Test]
        public void A0_1_CellSeed_Is_Pure_Position_Sensitive_And_Stage_Isolated()
        {
            const ulong seed = 1700;
            var a = CityRng.CellSeed(seed, StageId.S6_BuildingFill, 128, 77);
            Assert.AreEqual(a, CityRng.CellSeed(seed, StageId.S6_BuildingFill, 128, 77), "同输入必须同值");

            Assert.AreNotEqual(a, CityRng.CellSeed(seed, StageId.S6_BuildingFill, 129, 77),
                "改一格必须换流（局部稳定性）");
            Assert.AreNotEqual(a, CityRng.CellSeed(seed, StageId.S5_LandUse, 128, 77),
                "阶段必须隔离，否则某阶段消费会扰动另一阶段（A-0-1 的核心）");
            Assert.AreNotEqual(a, CityRng.CellSeed(seed + 1, StageId.S6_BuildingFill, 128, 77));
            Assert.AreNotEqual(a, CityRng.CellSeed(seed, StageId.S6_BuildingFill, 77, 128),
                "x/z 交换不得同值");
        }

        [Test]
        public void A0_1_Stream_Replayable_No_Global_State()
        {
            var s1 = RngStream.ForCell(7, StageId.S3_Road, 5, 9, salt: 3);
            var s2 = RngStream.ForCell(7, StageId.S3_Road, 5, 9, salt: 3);
            for (var i = 0; i < 128; i++)
                Assert.AreEqual(s1.NextU64(), s2.NextU64(), $"第 {i} 个值必须一致（无隐藏全局态）");

            Assert.AreNotEqual(
                RngStream.ForCell(7, StageId.S3_Road, 5, 9, salt: 3).NextU64(),
                RngStream.ForCell(7, StageId.S3_Road, 5, 9, salt: 4).NextU64(), "salt 必须分流");
        }

        [Test]
        public void Rng_Range_And_Double_Match_Contract()
        {
            var s = RngStream.ForObject(99, StageId.S6_BuildingFill, 4242);
            var first = s.NextDouble();
            Assert.GreaterOrEqual(first, 0.0);
            Assert.Less(first, 1.0);
            Assert.AreEqual(first, RngStream.ForObject(99, StageId.S6_BuildingFill, 4242).NextDouble());

            var r = RngStream.ForCell(1, StageId.S4_Parcel, 0, 0);
            for (var i = 0; i < 300; i++)
            {
                var v = r.NextRange(-7, 13);
                Assert.GreaterOrEqual(v, -7);
                Assert.LessOrEqual(v, 13);
                var n = r.NextInt(4);
                Assert.GreaterOrEqual(n, 0); Assert.Less(n, 4);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => RngStream.ForCell(1, StageId.S4_Parcel, 0, 0).NextInt(0));
            Assert.Throws<ArgumentException>(() => RngStream.ForCell(1, StageId.S4_Parcel, 0, 0).NextRange(5, 1));
        }

        [Test]
        public void A0_2_IntentHash_Rejects_Unordered_Input_But_Is_Stable_When_Ordered()
        {
            // 00 §3.1 铁律：乱序会静默产出不同哈希 → 主动抛而非静默
            Assert.Throws<InvalidOperationException>(() =>
                IntentHash.Fold(new[] { ("b", 1L), ("a", 1L) }));

            var h1 = IntentHash.Fold(new[] { ("a", 1L), ("b", 2L) });
            Assert.AreEqual(h1, IntentHash.Fold(new[] { ("a", 1L), ("b", 2L) }));
            Assert.AreNotEqual(h1, IntentHash.Fold(new[] { ("a", 1L), ("b", 3L) }), "版本差必须改变哈希");
            Assert.AreNotEqual(h1, IntentHash.Fold(new[] { ("a", 1L), ("c", 2L) }), "身份差必须改变哈希");
            Assert.AreNotEqual(IntentHash.Fold(new (string, long)[0]), h1, "空集合与单元素必须可区分");
            Assert.Throws<ArgumentException>(() => IntentHash.Fold(new[] { ("", 1L) }), "空 stableId 必须拒");
        }

        [Test]
        public void StableStringHash_Is_Deterministic()
        {
            Assert.AreEqual(IntentHash.StableStringHash("field-cbd"),
                            IntentHash.StableStringHash("field-cbd"));
            Assert.AreNotEqual(IntentHash.StableStringHash("a"), IntentHash.StableStringHash("b"));
        }

        // ─────────── A-0-7 / D-14：分辨率档位与参数校验 ───────────

        [Test]
        public void A0_7_Grid_Rejects_Invalid_Resolution_Ratios()
        {
            Assert.Throws<ArgumentException>(() => new CityGrid(2000f, 2f, 7f, 128f, 1024f, 60f),
                "displayStep 非 cellSize 整数倍必须拒");
            Assert.Throws<ArgumentException>(() => new CityGrid(2000f, 2f, 8f, 127f, 1024f, 60f),
                "chunkSize 非整数倍必须拒");
            Assert.Throws<ArgumentException>(() => new CityGrid(2000f, 2f, 8f, 128f, 1000f, 60f),
                "region 非 chunk 整数倍必须拒");
            Assert.Throws<ArgumentException>(() => new CityGrid(0f, 2f, 8f, 128f, 1024f, 60f));
            Assert.Throws<ArgumentException>(() => new CityGrid(2000f, 0f, 8f, 128f, 1024f, 60f));
        }

        [Test]
        public void Scale_Profile_Dimensions_Match_Frozen_Memory_Statement()
        {
            var small = new CityGrid(2000f, 2f, 8f, 128f, 1024f, 60f);
            var large = new CityGrid(8000f, 4f, 16f, 128f, 1024f, 60f);
            Assert.AreEqual(1000, small.Columns);
            Assert.AreEqual(1_000_000, small.CellCount);
            Assert.AreEqual(2000, large.Columns);
            Assert.AreEqual(4_000_000, large.CellCount, "8km/4m = 4M 格（11 §3.1 实算口径）");
            Assert.AreEqual(63, large.ChunkCols, "8000/128 = 62.5 → 上取整 63（尾块不足一整块，符合分块语义）");
            Assert.AreEqual(8, large.RegionCols);
            Assert.AreEqual(501, large.DisplayColumns, "8km/16m 显示网格单边顶点数");
        }

        [Test]
        public void Cell_Index_Is_Floor_And_Bounded()
        {
            var g = new CityGrid(200f, 2f, 8f, 20f, 100f, 0f);
            Assert.IsTrue(g.TryCellIndex(1.9f, 1.9f, out var i0));
            g.CellCoord(i0, out var cx, out var cz);
            Assert.AreEqual(0, cx); Assert.AreEqual(0, cz);

            Assert.IsTrue(g.TryCellIndex(2.0f, 2.0f, out var i1));
            g.CellCoord(i1, out cx, out cz);
            Assert.AreEqual(1, cx, "floor：恰在边界算后一格");

            Assert.IsFalse(g.TryCellIndex(-0.1f, 5f, out var bad), "越界返回 false");
            Assert.AreEqual(-1, bad);
            Assert.IsFalse(g.TryCellIndex(5f, 200.1f, out _));
            Assert.AreEqual(0, g.CellIndex(1.0f, 1.0f));
        }

        [Test]
        public void Circle_Rect_Clamps_Into_Grid()
        {
            var g = new CityGrid(200f, 2f, 8f, 20f, 100f, 0f);
            g.CellRectForCircle(new Vector2(1f, 1f), 1000f, out var minCx, out var minCz, out var maxCx, out var maxCz);
            Assert.GreaterOrEqual(minCx, 0);
            Assert.GreaterOrEqual(minCz, 0);
            Assert.LessOrEqual(maxCx, g.Columns - 1);
            Assert.LessOrEqual(maxCz, g.Rows - 1);
            Assert.GreaterOrEqual(maxCx, minCx);
            Assert.GreaterOrEqual(maxCz, minCz);

            // 圆外接盒必须覆盖真实圆内格（不欠采样）：半径 10m、格 2m
            g.CellRectForCircle(new Vector2(100f, 100f), 10f, out minCx, out minCz, out maxCx, out maxCz);
            Assert.AreEqual(45, minCx); Assert.AreEqual(54, maxCx);
        }

        [Test]
        public void Region_And_Chunk_Indexing_Agree_With_Cell_Indices()
        {
            var g = new CityGrid(8000f, 4f, 16f, 128f, 1024f, 60f);
            Assert.AreEqual(0, g.RegionIndex(0f, 0f));
            Assert.AreEqual(1, g.RegionIndex(1024f, 1f));
            Assert.AreEqual(1, g.ChunkIndex(128f, 0f));
            g.TryCellIndex(5000f, 3000f, out var cell);
            var byCell = g.RegionIndex(cell);
            var byWorld = g.RegionIndex(5000f, 3000f);
            Assert.AreEqual(byWorld, byCell, "同一位置两种入口必须得到同一 region（工作集取整的依据）");
        }

        // ─────────── 11 §2：定点量化往返（A-1 口径） ───────────

        [Test]
        public void Record_Quant_Roundtrip_Within_One_Step()
        {
            var g = new CityGrid(8000f, 4f, 16f, 128f, 1024f, 60f);
            for (float x = 0f; x < 7999f; x += 137.31f)
            {
                var back = g.DecodeXQ(g.EncodeXQ(x));
                Assert.LessOrEqual(Mathf.Abs(back - x), RecordQuant.Length + 1e-3f,
                    $"量化往返误差必须 ≤1 步：x={x} → {back}");
            }
            Assert.AreEqual(337f, g.DecodeXQ(g.EncodeXQ(337.004f)), 0.01f);
        }

        [Test]
        public void Yaw_Quant_Is_Quarter_Degree_And_Wraps()
        {
            // 量化步 0.25°：227.37 × 4 = 909.48 → 909 → 227.25（就近取步，不得凭空挪到 227.5）
            Assert.AreEqual(227.25f, RecordQuant.DecodeYaw(RecordQuant.EncodeYaw(227.37f)), 1e-5f);
            Assert.AreEqual(227.5f, RecordQuant.DecodeYaw(RecordQuant.EncodeYaw(227.6f)), 1e-5f);
            Assert.AreEqual(RecordQuant.EncodeYaw(-30f), RecordQuant.EncodeYaw(330f), "负角与正角同值");
            Assert.AreEqual(RecordQuant.EncodeYaw(360f), RecordQuant.EncodeYaw(0f));
            Assert.AreEqual(1439, RecordQuant.YawMax);
        }

        // ─────── A-27 / H-29 / H-2 / H-7：脏传播规则表（纯函数） ───────

        [Test]
        public void A27_Field_Change_Never_Dirties_Terrain_Or_Flatten()
        {
            var d = IntentChangePlanner.Expand(IntentChangePlanner.DirtyOf(IntentKind.Field));
            Assert.IsTrue((d & DirtyBits.Building) != 0);
            Assert.IsTrue((d & DirtyBits.Field) != 0);
            Assert.AreEqual(0, (int)(d & DirtyBits.Terrain), "拖场域绝不得触发 S1a（H-7 立论）");
            Assert.AreEqual(0, (int)(d & DirtyBits.Flatten));
            Assert.AreEqual(0, (int)(d & DirtyBits.River));
            Assert.AreEqual(0, (int)(d & DirtyBits.Center));
        }

        [Test]
        public void H29_River_Change_Writes_Back_Flatten_But_Not_Noise()
        {
            var d = IntentChangePlanner.Expand(IntentChangePlanner.DirtyOf(IntentKind.RiverStroke));
            Assert.AreEqual(0, (int)(d & DirtyBits.Terrain), "改河不得重跑噪声基底（拆位的意义）");
            Assert.IsTrue((d & DirtyBits.Flatten) != 0, "但必须回写平整约束（下切改变了地面高）");
            Assert.IsTrue((d & DirtyBits.Road) != 0, "桥位必须复检");
            Assert.IsTrue((d & DirtyBits.Parcel) != 0);
            Assert.IsTrue((d & DirtyBits.Field) != 0);
            Assert.IsTrue((d & DirtyBits.Building) != 0);
        }

        [Test]
        public void H29_OccupiedReloc_Is_Terminal_Bit_And_Propagates_Nothing()
        {
            // OccupiedReloc 是终端位：Expand 不得从它扩散出任何其它位。
            // 复检由 S0/S3/S6 的 DependsOn 直接认领；一旦在 Next() 并上 Road/Building，
            // Road 的下游会把 Parcel/Field/Flatten 全拖进来 → 挪一栋楼=重算地块（08 §5.1 表末行禁止）。
            var raw = IntentChangePlanner.DirtyOf(IntentKind.OccupiedReloc);
            Assert.AreEqual(DirtyBits.OccupiedReloc, raw);
            var d = IntentChangePlanner.Expand(raw);
            Assert.AreEqual(DirtyBits.OccupiedReloc, d, "终端位展开必须等于自身");
            Assert.AreEqual(0, (int)(d & DirtyBits.Road), "不得借 Road 位表达复检（会拖出 Parcel/Field/Flatten）");
            Assert.AreEqual(0, (int)(d & DirtyBits.River));
            Assert.AreEqual(0, (int)(d & DirtyBits.Parcel));
            Assert.AreEqual(0, (int)(d & DirtyBits.Terrain));
            Assert.AreEqual(0, (int)(d & DirtyBits.Flatten));

            // 认领检查：S0/S3/S6 的 DependsOn 必须各自含 OccupiedReloc（面板与真实阶段同源）
            Assert.IsTrue((StageClaims.Of(StageId.S0_Assemble)     & DirtyBits.OccupiedReloc) != 0, "S0 必须重烘焙掩码");
            Assert.IsTrue((StageClaims.Of(StageId.S3_Road)         & DirtyBits.OccupiedReloc) != 0, "S3 必须复检是否压到路");
            Assert.IsTrue((StageClaims.Of(StageId.S6_BuildingFill) & DirtyBits.OccupiedReloc) != 0, "S6 必须复检叠楼（A-8）");
            Assert.AreEqual(0, (int)(StageClaims.Of(StageId.S4_Parcel) & DirtyBits.OccupiedReloc), "S4 地块不得被挪动触发");
        }

        [Test]
        public void H2_Occupancy_Change_Makes_Frozen_Objects_Into_Walls_Everywhere()
        {
            var d = IntentChangePlanner.Expand(IntentChangePlanner.DirtyOf(IntentKind.Occupancy));
            Assert.IsTrue((d & DirtyBits.River) != 0, "adopt 是硬墙，河道必须复检（09 H-2）");
            Assert.IsTrue((d & DirtyBits.Road) != 0);
            Assert.IsTrue((d & DirtyBits.Parcel) != 0);
            Assert.IsTrue((d & DirtyBits.Building) != 0);
        }

        [Test]
        public void H7_Center_Change_Is_Near_Full_Chain_And_High_Risk()
        {
            var d = IntentChangePlanner.Expand(IntentChangePlanner.DirtyOf(IntentKind.CityCenter));
            Assert.IsTrue((d & DirtyBits.Terrain) != 0, "城心动 ⇒ S1a 压幅中心变（H-7 的代价，不隐藏）");
            Assert.IsTrue((d & DirtyBits.Flatten) != 0);
            Assert.IsTrue((d & DirtyBits.River) != 0);
            Assert.IsTrue((d & DirtyBits.Building) != 0);
            Assert.IsTrue(IntentChangePlanner.RequiresCommit(d), "必须是 Commit 档（不可能只是 Preview）");
        }

        [Test]
        public void Seed_Or_World_Change_Expands_To_All()
        {
            Assert.AreEqual(DirtyBits.All,
                IntentChangePlanner.Expand(IntentChangePlanner.DirtyOf(IntentKind.WorldParam)));
            Assert.IsTrue(IntentChangePlanner.RequiresCommit(DirtyBits.Seed));
        }

        [Test]
        public void A0_3_Expand_Is_Idempotent_For_Every_Intent()
        {
            // A-0-3：求解必须是不动点，否则调度会反复重算、无法收敛
            foreach (IntentKind k in Enum.GetValues(typeof(IntentKind)))
            {
                var one = IntentChangePlanner.Expand(IntentChangePlanner.DirtyOf(k));
                Assert.AreEqual(one, IntentChangePlanner.Expand(one), $"Expand 必须幂等（{k}）");
            }
            var combo = IntentChangePlanner.Expand(DirtyBits.Field | DirtyBits.River);
            Assert.AreEqual(combo, IntentChangePlanner.Expand(combo));
            Assert.AreEqual(DirtyBits.None, IntentChangePlanner.Expand(DirtyBits.None));
            // 组合必须包含各自单独展开的结果（并集性）
            Assert.IsTrue((combo & IntentChangePlanner.Expand(DirtyBits.Field)) ==
                          IntentChangePlanner.Expand(DirtyBits.Field));
        }

        [Test]
        public void Profile_Change_Only_Dirties_Building()
        {
            Assert.AreEqual(DirtyBits.Profile | DirtyBits.Building,
                IntentChangePlanner.Expand(IntentChangePlanner.DirtyOf(IntentKind.BuildingRow)),
                "改表只影响 S6（总纲 §3.2）");
        }

        [Test]
        public void Budget_And_Parcel_Changes_Reach_Flatten_But_Not_Noise()
        {
            var budget = IntentChangePlanner.Expand(IntentChangePlanner.DirtyOf(IntentKind.RoadBudget));
            Assert.IsTrue((budget & DirtyBits.Flatten) != 0, "道路带状压平是约束来源（01 §3.4）");
            Assert.AreEqual(0, (int)(budget & DirtyBits.Terrain));

            var parcel = IntentChangePlanner.Expand(IntentChangePlanner.DirtyOf(IntentKind.ParcelModel));
            Assert.IsTrue((parcel & DirtyBits.Flatten) != 0, "地块目标平面变了必须回写 S1b");
            Assert.IsTrue((parcel & DirtyBits.Building) != 0);
            Assert.AreEqual(0, (int)(parcel & DirtyBits.Terrain));
        }

        // ─────────── D-00-7：衰减口径唯一（05/01 共用） ───────────

        [Test]
        public void A5_2_Falloff_Presets_Monotonic_Clamped_At_Endpoints()
        {
            foreach (Falloff.Preset p in Enum.GetValues(typeof(Falloff.Preset)))
            {
                Assert.AreEqual(0f, Falloff.Evaluate(p, -5f), $"{p} 越界须钳 0");
                Assert.AreEqual(1f, Falloff.Evaluate(p, 7f), $"{p} 越界须钳 1");
                Assert.AreEqual(0f, Falloff.Evaluate(p, 0f));
                Assert.AreEqual(1f, Falloff.Evaluate(p, 1f));

                float prev = -1f;
                for (float t = 0f; t <= 1.0001f; t += 0.02f)
                {
                    var v = Falloff.Evaluate(p, t);
                    Assert.GreaterOrEqual(v, prev - 1e-5f, $"{p} 必须单调不减（A-5-2）");
                    Assert.GreaterOrEqual(v, 0f);
                    Assert.LessOrEqual(v, 1f);
                    prev = v;
                }
            }
            Assert.AreEqual(0f, Falloff.Evaluate(Falloff.Preset.Step, 0.999f), "Step 只有到外边界才跳变");
            Assert.AreEqual(1f, Falloff.Evaluate(Falloff.Preset.Step, 1f));
        }

        [Test]
        public void A5_1_Field_Sample_Plateau_Transition_Cutoff_And_Two_Parameters()
        {
            const int innerValue = 85;
            for (var d = 0f; d <= 59.5f; d += 7f)
                Assert.AreEqual(innerValue,
                    Falloff.SampleField(d, 60f, 380f, innerValue, 0, null, null, Falloff.Preset.Linear),
                    "r ≤ innerR ⇒ 恒等于内径值（05 A-5-1）");

            Assert.AreEqual(0, Falloff.SampleField(380.1f, 60f, 380f, innerValue, 0, null, null, Falloff.Preset.Linear));

            var mid = Falloff.SampleField(220f, 60f, 380f, innerValue, 0, null, null, Falloff.Preset.Linear);
            Assert.Greater(mid, 0);
            Assert.Less(mid, innerValue, "过渡带必须严格介于两者之间");

            // Q-1 核心：内径（米）与内径值（0~100）互不干涉
            Assert.AreEqual(10, Falloff.SampleField(9f, 10f, 200f, 10, 0, null, null, Falloff.Preset.Linear),
                "用户示例（半径 10m、值 10）：平台值 10 即门槛");
            Assert.AreEqual(10, Falloff.SampleField(9f, 10f, 900f, 10, 0, null, null, Falloff.Preset.Linear),
                "改外径不影响内平台值");

            Assert.Throws<ArgumentException>(() =>
                Falloff.SampleField(5f, 100f, 50f, 80, 0, null, null, Falloff.Preset.Linear),
                "outer < inner 必须抛（05 §4.2 的兜底）");
        }

        [Test]
        public void Falloff_Edge_Cases_Divide_By_Zero_And_Clamp()
        {
            // inner == outer：`r ≤ innerR` 先命中 ⇒ 取 innerValue；外径再往外立即截止。
            // （05 §1 的平台定义包含边界，实现里的 span≈0 分支因此是防御性死支，保留不删）
            Assert.AreEqual(80, Falloff.SampleField(10f, 10f, 10f, 80, 50, null, null, Falloff.Preset.Linear));
            Assert.AreEqual(0, Falloff.SampleField(10.01f, 10f, 10f, 80, 50, null, null, Falloff.Preset.Linear));

            // D-02：值域钳到 0~100
            Assert.AreEqual(100, Falloff.SampleField(5f, 10f, 50f, 250, 0, null, null, Falloff.Preset.Linear));
            Assert.AreEqual(0, Falloff.SampleField(5f, 10f, 50f, -30, 0, null, null, Falloff.Preset.Linear));
        }

        // ─────────── 00 §2.4：掩码与位预算 ───────────

        [Test]
        public void Mask_Registers_Thirteen_Single_Bits_As_Documented()
        {
            // 与 00 §2.4 位预算表互为守卫：实现私自加/减位时这条会红
            var singles = 0;
            foreach (var raw in System.Enum.GetValues(typeof(CellFlag)))
            {
                // CellFlag 底层是 ushort，GetValues 元素装箱后强转 (int) 会抛 InvalidCastException
                var iv = System.Convert.ToUInt16(raw);
                if (iv == 0 || (iv & (iv - 1)) != 0) continue;   // 跳过 None 与组合值
                singles++;
            }
            Assert.AreEqual(13, singles);
        }

        [Test]
        public void Mask_Writer_Is_Bounds_Safe()
        {
            var g = new CityGrid(100f, 2f, 8f, 20f, 100f, 0f);
            var mask = new ushort[g.CellCount];
            var w = new MaskWriter(mask, g.Columns);

            Assert.IsFalse(w.TrySet(-1, CellFlag.Road));
            Assert.IsFalse(w.TrySet(mask.Length, CellFlag.Road));
            Assert.IsFalse(w.Has(mask.Length, CellFlag.Road));
            Assert.IsFalse(w.TrySet(g.Columns, 0, CellFlag.Road), "列坐标越界必须拒");
            Assert.IsFalse(w.TrySet(0, g.Rows, CellFlag.Road), "行坐标越界必须拒");

            Assert.IsTrue(w.TrySet(0, CellFlag.Water));
            Assert.IsTrue(w.Has(0, CellFlag.Water));
            Assert.IsTrue(w.BlocksBuilding(0), "Water 属于禁建合集");
            Assert.IsTrue(w.TrySet(0, CellFlag.None, CellFlag.Water));
            Assert.IsFalse(w.BlocksBuilding(0));
        }

        [Test]
        public void Frozen_Objects_And_Constraints_All_Block_Building()
        {
            var g = new CityGrid(100f, 2f, 8f, 20f, 100f, 0f);
            var mask = new ushort[g.CellCount];
            var w = new MaskWriter(mask, g.Columns);

            w.TrySet(0, CellFlag.OccupiedHard);   // 09 H-2：adopt 必须禁建
            w.TrySet(1, CellFlag.NoBuild);
            w.TrySet(2, CellFlag.Road);
            w.TrySet(3, CellFlag.WaterBuffer);
            w.TrySet(4, CellFlag.InsetBand);
            w.TrySet(5, CellFlag.OutOfBounds);
            w.TrySet(6, CellFlag.Parcel);          // 普通属性位：不得禁建

            for (var i = 0; i <= 5; i++)
                Assert.IsTrue(w.BlocksBuilding(i), $"格 {i} 的位必须落在 NoBuildAny 内");
            Assert.IsFalse(w.BlocksBuilding(6), "Parcel 只是属性位，不是禁入位");

            // 置位/清位互不污染
            w.TrySet(0, CellFlag.NoRoad);
            Assert.IsTrue(w.Has(0, CellFlag.OccupiedHard), "加新位不得清掉旧位");
            w.TrySet(0, CellFlag.None, CellFlag.NoRoad);
            Assert.IsFalse(w.Has(0, CellFlag.NoRoad));
            Assert.IsTrue(w.Has(0, CellFlag.OccupiedHard), "清某位不得连带清邻位");
        }

        [Test]
        public void InsetBand_Only_Exists_When_Inset_Positive()
        {
            var withBand = new CityGrid(200f, 2f, 8f, 20f, 100f, 60f);
            var noBand = new CityGrid(200f, 2f, 8f, 20f, 100f, 0f);

            withBand.TryCellIndex(10f, 10f, out var corner);
            withBand.TryCellIndex(100f, 100f, out var center);
            Assert.IsTrue(withBand.IsInInsetBand(corner), "外环角格应为 InsetBand");
            Assert.IsFalse(withBand.IsInInsetBand(center), "图幅中心不属于带");

            noBand.TryCellIndex(10f, 10f, out var sameCorner);
            Assert.IsFalse(noBand.IsInInsetBand(sameCorner), "inset=0 时不得产生带");
        }

        // ─────────── A-51 / 11 §4.3：heightValue 双义（已裁定保留） ───────────

        [Test]
        public void A51_HeightValue_Four_Cases_Of_Two_Dimensional_Table()
        {
            // 情形一：stretch + 非降级 ⇒ repeat 段数
            Assert.IsTrue(RecordGeometry.Resolve(new BuildingRecord { HeightValue = 7 }, true,
                out var k1, out var v1));
            Assert.AreEqual(RecordGeometry.HeightKind.RepeatSegments, k1);
            Assert.AreEqual(7, v1);

            // 情形二：stretch + 降级 ⇒ 缩放百分数
            Assert.IsTrue(RecordGeometry.Resolve(
                new BuildingRecord { HeightValue = 250, Flags = (byte)BuildingFlags.Approximated }, true,
                out var k2, out var v2));
            Assert.AreEqual(RecordGeometry.HeightKind.ScalePercent, k2);
            Assert.AreEqual(2.5f, RecordGeometry.ToScaleFactor(v2), 1e-5f);

            // 情形三：非 stretch ⇒ 哨兵 1（A-9 几何恒等的判据）
            Assert.IsTrue(RecordGeometry.Resolve(new BuildingRecord { HeightValue = 1 }, false,
                out var k3, out var v3));
            Assert.AreEqual(RecordGeometry.HeightKind.NotStretchable, k3);
            Assert.AreEqual(1, v3);

            // 情形四：false + approximated ⇒ 非法组合（11 §10 error）
            Assert.IsFalse(RecordGeometry.Resolve(
                new BuildingRecord { HeightValue = 300, Flags = (byte)BuildingFlags.Approximated }, false,
                out _, out _));
        }

        [Test]
        public void A51_Out_Of_Range_Values_Rejected_At_Frozen_Bounds()
        {
            Assert.IsFalse(RecordGeometry.Resolve(new BuildingRecord { HeightValue = 0 }, true, out _, out _),
                "repeat 0 非法（11 §4.3）");
            Assert.IsFalse(RecordGeometry.Resolve(new BuildingRecord { HeightValue = -3 }, true, out _, out _));
            Assert.IsTrue(RecordGeometry.Resolve(new BuildingRecord { HeightValue = 1 }, true, out _, out _));

            foreach (var v in new[] { 49, 50, 800, 801, 1439 })
            {
                var ok = RecordGeometry.Resolve(
                    new BuildingRecord { HeightValue = (short)v, Flags = (byte)BuildingFlags.Approximated },
                    true, out _, out _);
                Assert.AreEqual(v >= RecordGeometry.ScaleMin && v <= RecordGeometry.ScaleMax, ok,
                    $"{v} 的合法性须与 11 §4.3 有效区 [50,800] 一致");
            }
        }

        [Test]
        public void A48_Footprint_Override_Sentinel_Zero_Means_Table_Value()
        {
            Assert.IsFalse(RecordGeometry.TryGetFootprintOverride(
                new BuildingRecord { FootWQ = 0, FootDQ = 0 }, out _, out _), "0 = 用表值（H-26 哨兵约定）");

            var r = new BuildingRecord
            {
                FootWQ = (short)RecordGeometry.EncodeFootprintOverride(12.34f),
                FootDQ = (short)RecordGeometry.EncodeFootprintOverride(8.10f),
            };
            Assert.IsTrue(RecordGeometry.TryGetFootprintOverride(r, out var w, out var d));
            Assert.AreEqual(12.34f, w, 0.01f);
            Assert.AreEqual(8.10f, d, 0.01f);

            // 单轴覆盖在 11 未定义语义 → 不得静默半生效（防止实现期自作主张）
            Assert.IsFalse(RecordGeometry.TryGetFootprintOverride(
                new BuildingRecord { FootWQ = 100, FootDQ = 0 }, out _, out _));
        }

        [Test]
        public void SlotKey_Namespace_Separates_Manual_And_Derived()
        {
            Assert.IsTrue(SlotKey.IsManual(SlotKey.MakeManual(42)));
            Assert.IsFalse(SlotKey.IsManual(42));
            Assert.AreEqual(42, SlotKey.MakeManual(42) & ~SlotKey.NamespaceBit);
            Assert.AreEqual(int.MaxValue, SlotKey.DerivedMax, "派生键上限（bit31 让给命名空间）");
        }

        [Test]
        public void Record_Bit_Accessors_Do_Not_Clobber_Neighbors()
        {
            var r = new BuildingRecord();
            r.Set(BuildingFlags.Orphan);
            r.Set(BuildingFlags.Adopted);
            Assert.IsTrue(r.Has(BuildingFlags.Orphan));
            Assert.IsTrue(r.Has(BuildingFlags.Adopted));
            r.Set(BuildingFlags.Orphan, false);
            Assert.IsFalse(r.Has(BuildingFlags.Orphan), "清位不得连带清掉邻位");
            Assert.IsTrue(r.Has(BuildingFlags.Adopted));
        }

        // ─────────── A-0-3 / A-0-4：调度器语义 ───────────

        private sealed class FakeStage : IPipelineStage
        {
            public StageId Id { get; }
            public DirtyBits DependsOn { get; }
            public int Runs;
            public bool Throw;
            public Action<PipelineContext> OnRun;
            public FakeStage(StageId id, DirtyBits depends) { Id = id; DependsOn = depends; }
            public StageResult Run(PipelineContext ctx, RunMode mode)
            {
                Runs++;
                if (Throw) throw new Exception("故意抛");
                OnRun?.Invoke(ctx);
                return StageResult.Ok(1, 1);
            }
        }

        private sealed class RecordingStage : IPipelineStage
        {
            private readonly List<StageId> _log;
            public StageId Id { get; }
            public DirtyBits DependsOn { get; }
            public RecordingStage(StageId id, DirtyBits d, List<StageId> log) { Id = id; DependsOn = d; _log = log; }
            public StageResult Run(PipelineContext ctx, RunMode mode) { _log.Add(Id); return StageResult.Ok(); }
        }

        [Test]
        public void Runner_Merges_Same_Frame_Dirt_And_Executes_Minimal_Set()
        {
            var ctx = new PipelineContext(MakeProject(600f));
            var runner = new PipelineRunner();
            var field = new FakeStage(StageId.S5_LandUse, DirtyBits.Field);
            var build = new FakeStage(StageId.S6_BuildingFill, DirtyBits.Building);
            var river = new FakeStage(StageId.S2_River, DirtyBits.River);
            runner.Register(field); runner.Register(build); runner.Register(river);

            runner.MarkDirty(DirtyBits.Field);
            runner.MarkDirty(DirtyBits.Field);
            runner.MarkDirty(DirtyBits.Profile);
            var n = runner.Tick(ctx, RunMode.Commit);

            Assert.AreEqual(2, n, "同帧多次 Mark 只跑一轮（00 §4.2）");
            Assert.AreEqual(1, field.Runs);
            Assert.AreEqual(1, build.Runs, "Profile→Building 的下游必须执行");
            Assert.AreEqual(0, river.Runs, "未涉及水系不得执行（最小脏集合）");
            Assert.AreEqual(DirtyBits.None, runner.Pending);
            Assert.AreEqual(2, ctx.Report.Entries.Count);
        }

        [Test]
        public void Runner_Executes_In_Fixed_Order_Regardless_Of_Registration_Order()
        {
            var ctx = new PipelineContext(MakeProject(600f));
            var runner = new PipelineRunner();
            var log = new List<StageId>();
            // 故意乱序注册：执行序必须来自 ExecutionOrder（S1a 早于 S1b，D-12 的实现约束）
            runner.Register(new RecordingStage(StageId.S1b_Flatten, DirtyBits.Flatten, log));
            runner.Register(new RecordingStage(StageId.S4_Parcel, DirtyBits.Parcel, log));
            runner.Register(new RecordingStage(StageId.S1a_TerrainBase, DirtyBits.Terrain, log));

            runner.MarkDirty(DirtyBits.Terrain);
            runner.Tick(ctx, RunMode.Commit);

            CollectionAssert.AreEqual(
                new[] { StageId.S1a_TerrainBase, StageId.S4_Parcel, StageId.S1b_Flatten }, log,
                "S1a 必须早于 S1b；执行序与注册序无关");
        }

        [Test]
        public void A0_4_Cancel_Takes_Effect_At_Next_Boundary()
        {
            var ctx = new PipelineContext(MakeProject(600f));
            var runner = new PipelineRunner();
            var s0 = new FakeStage(StageId.S0_Assemble, DirtyBits.World);
            var s1 = new FakeStage(StageId.S1a_TerrainBase, DirtyBits.Terrain);
            var s2 = new FakeStage(StageId.S2_River, DirtyBits.River);
            // 仅【首次】执行时请求取消：否则第二次 Tick 会被同一个 lambda 再取消一次，测不到"复位后重跑"
            var cancelOnce = true;
            s0.OnRun = c => { if (cancelOnce) { cancelOnce = false; c.RequestCancel(); } };
            runner.Register(s0); runner.Register(s1); runner.Register(s2);

            runner.MarkDirty(DirtyBits.World);
            var n = runner.Tick(ctx, RunMode.Commit);

            Assert.AreEqual(1, s0.Runs);
            Assert.AreEqual(0, s1.Runs, "取消后不得继续跑后续阶段（00 §4.4）");
            Assert.AreEqual(0, s2.Runs);
            Assert.AreEqual(1, n);
            Assert.IsTrue(ctx.CancelRequested);

            ctx.ResetCancel();
            Assert.IsFalse(ctx.CancelRequested, "复位后必须可再次运行，不留粘滞取消态");
            runner.MarkDirty(DirtyBits.World);
            Assert.AreEqual(3, runner.Tick(ctx, RunMode.Commit), "复位后可完整重跑");
            Assert.AreEqual(2, s0.Runs);
        }

        [Test]
        public void Cancel_Before_Tick_Reports_Cancellation_And_Runs_Nothing()
        {
            var ctx = new PipelineContext(MakeProject(600f));
            var runner = new PipelineRunner();
            var s0 = new FakeStage(StageId.S0_Assemble, DirtyBits.World);
            runner.Register(s0);
            ctx.RequestCancel();

            runner.MarkDirty(DirtyBits.World);
            Assert.AreEqual(0, runner.Tick(ctx, RunMode.Commit));
            Assert.AreEqual(0, s0.Runs);
            Assert.AreEqual(1, ctx.Report.Entries.Count, "取消也要留痕（面板可见）");
            Assert.IsTrue(ctx.Report.Entries[0].Cancelled);
            Assert.IsFalse(ctx.Report.AnyFailure, "取消不算失败，只是中止");
            ctx.ResetCancel();
        }

        [Test]
        public void Runner_Stage_Failure_Stops_Chain_But_Does_Not_Throw_Out()
        {
            var ctx = new PipelineContext(MakeProject(600f));
            var runner = new PipelineRunner();
            var bad = new FakeStage(StageId.S0_Assemble, DirtyBits.World) { Throw = true };
            var after = new FakeStage(StageId.S6_BuildingFill, DirtyBits.Building);
            runner.Register(bad); runner.Register(after);

            // 该阶段【必须】抛（被测行为就是"抛了也不外泄"），而 Runner 会把异常记成 LogException。
            // 这条 error 若留在控制台，会直接违反 AGENTS §1.6b 的归零门禁；
            // 又因为堆栈含本包程序集，不适用 §1.8 的环境噪声豁免 ⇒ 唯一正解是在断言期间关掉日志输出。
            // 这不是测试专用分支：生产代码的 LogError 路径原样保留。
            Debug.unityLogger.logEnabled = false;
            try
            {
                runner.MarkDirty(DirtyBits.World);
                Assert.DoesNotThrow(() => runner.Tick(ctx, RunMode.Commit));
            }
            finally { Debug.unityLogger.logEnabled = true; }

            Assert.AreEqual(0, after.Runs, "上游失败不得拿半成品算下游（00 §4.4）");
            Assert.IsTrue(ctx.Report.AnyFailure);
        }
        [Test]
        public void Runner_Rejects_Duplicate_Stage_Registration()
        {
            var runner = new PipelineRunner();
            runner.Register(new FakeStage(StageId.S0_Assemble, DirtyBits.World));
            Assert.Throws<InvalidOperationException>(() =>
                runner.Register(new FakeStage(StageId.S0_Assemble, DirtyBits.World)));
            Assert.DoesNotThrow(() =>
                runner.Register(new FakeStage(StageId.S6_BuildingFill, DirtyBits.Building)));

            runner.Unregister(StageId.S0_Assemble);
            Assert.DoesNotThrow(() => runner.Register(new FakeStage(StageId.S0_Assemble, DirtyBits.World)));
        }

        [Test]
        public void Context_Slots_Are_Type_Safe_And_Stage_Keyed()
        {
            var ctx = new PipelineContext(MakeProject(600f));
            Assert.IsFalse(ctx.HasSlot(StageId.S0_Assemble));
            Assert.IsNull(ctx.GetSlot<AssembleStage.AssembleData>(StageId.S0_Assemble));
            ctx.SetSlot(StageId.S0_Assemble, new AssembleStage.AssembleData { CellSize = 2f, Touched = 7 });
            Assert.IsTrue(ctx.HasSlot(StageId.S0_Assemble));
            Assert.AreEqual(7, ctx.GetSlot<AssembleStage.AssembleData>(StageId.S0_Assemble).Touched);
            Assert.IsNull(ctx.GetSlot<AssembleStage.AssembleData>(StageId.S5_LandUse), "槽位不得串台");
        }

        [Test]
        public void Dirty_Bounds_Union_Clamps_Far_Coordinates()
        {
            var ctx = new PipelineContext(MakeProject(600f));
            Assert.IsNull(ctx.UnionOfDirtyBounds(), "无脏区域 = null（调用方按全图处理）");

            ctx.AddDirtyRegion(new Vector2(100f, 100f), 30f);
            ctx.AddDirtyRegion(new Vector2(200f, 150f), 20f);
            var u = ctx.UnionOfDirtyBounds();
            Assert.NotNull(u);
            Assert.Greater(u.Value.size.x, 130f);

            ctx.ClearDirtyBounds();
            Assert.IsNull(ctx.UnionOfDirtyBounds());

            ctx.AddDirtyRegion(new Vector2(1e6f, 1e6f), 10f);   // 远越界坐标不得抛，须裁进图幅
            Assert.NotNull(ctx.UnionOfDirtyBounds());

            ctx.ClearDirtyBounds();
            ctx.AddDirtyBound(default);                          // 空包围盒必须被忽略
            Assert.IsNull(ctx.UnionOfDirtyBounds());
        }

        [Test]
        public void Context_Rejects_Null_Project_And_Exposes_Grid()
        {
            Assert.Throws<ArgumentNullException>(() => new PipelineContext(null));
            var ctx = new PipelineContext(MakeProject(600f));
            Assert.AreEqual(2f, ctx.Grid.CellSize);
            Assert.AreEqual(ctx.Project.GetGrid().Columns, ctx.Grid.Columns);
            Assert.AreEqual(600f / 2f, ctx.Mask.Length / ctx.Grid.Columns, "掩码长度必须等于格数/列数=行数");
        }

        // ─────────── S0 装配：外环 + 负约束烘焙 ───────────

        [Test]
        public void S0_Bakes_InsetBand_And_Negative_Constraints_Into_Mask()
        {
            var p = MakeProject(400f);
            var cons = Spawn(ScriptableObject.CreateInstance<ConstraintAsset>());
            cons.EnsureStableId();
            Assert.IsTrue(cons.Add(ConstraintKind.NoBuild, new Vector2(200f, 200f), 20f, out _));

            var ctx = new PipelineContext(p);
            var res = new AssembleStage(cons).Run(ctx, RunMode.Commit);
            Assert.IsTrue(res.Success);

            ctx.Grid.TryCellIndex(200f, 200f, out var centerCell);
            Assert.IsTrue(ctx.Writer.Has(centerCell, CellFlag.NoBuild), "圆心格必须禁建");
            Assert.IsTrue(ctx.Writer.BlocksBuilding(centerCell));

            ctx.Grid.TryCellIndex(395f, 200f, out var edgeCell);
            Assert.IsTrue(ctx.Writer.Has(edgeCell, CellFlag.InsetBand), "外环格必须置 InsetBand");
            Assert.IsTrue(ctx.Writer.BlocksBuilding(edgeCell));

            ctx.Grid.TryCellIndex(200f, 60f, out var openCell);
            Assert.IsFalse(ctx.Writer.Has(openCell, CellFlag.NoBuild), "约束半径外不得被污染");
            Assert.IsTrue(ctx.Writer.Has(openCell, CellFlag.Buildable));

            Assert.AreEqual(60f, ctx.Grid.BuildableInset);
            Assert.IsTrue(res.CellsTouched > 0);
        }

        [Test]
        public void S0_Rerun_Clears_Removed_Constraints_First_Then_Sets()
        {
            var ctx = new PipelineContext(MakeProject(400f));
            var cons = Spawn(ScriptableObject.CreateInstance<ConstraintAsset>());
            cons.EnsureStableId();
            cons.Add(ConstraintKind.NoRoad, new Vector2(100f, 100f), 16f, out var id);

            var s0 = new AssembleStage(cons);
            s0.Run(ctx, RunMode.Commit);
            ctx.Grid.TryCellIndex(100f, 100f, out var c);
            Assert.IsTrue(ctx.Writer.Has(c, CellFlag.NoRoad));

            Assert.IsTrue(cons.Remove(id));
            var before = ctx.Mask[c];
            s0.Run(ctx, RunMode.Commit);
            Assert.AreNotEqual(before, ctx.Mask[c], "重跑必须先清位再置位（否则删除不生效）");
            Assert.IsFalse(ctx.Writer.Has(c, CellFlag.NoRoad), "移除的约束不得残留禁入");
            Assert.IsTrue(ctx.Writer.Has(c, CellFlag.Buildable), "禁入解除后必须恢复可建");
            Assert.IsFalse(cons.Remove("不存在"), "删除不存在的 id 返回 false 且不 bump");
        }

        [Test]
        public void S0_Ignores_Disabled_Entries_Without_Deleting_Them()
        {
            var ctx = new PipelineContext(MakeProject(400f));
            var cons = Spawn(ScriptableObject.CreateInstance<ConstraintAsset>());
            cons.EnsureStableId();
            cons.Add(ConstraintKind.NoWater, new Vector2(150f, 150f), 18f, out var id);
            var v0 = cons.ManualVersion;

            Assert.IsTrue(cons.SetEnabled(id, false));
            Assert.Greater(cons.ManualVersion, v0, "禁用也是意图变化（必须 bump，否则 Tracker 看不见）");
            Assert.AreEqual(1, cons.Entries.Count, "禁用不删数据（05 A-5-8 同语义）");

            new AssembleStage(cons).Run(ctx, RunMode.Commit);
            ctx.Grid.TryCellIndex(150f, 150f, out var c);
            Assert.IsFalse(ctx.Writer.Has(c, CellFlag.NoWater));

            Assert.IsFalse(cons.SetEnabled(id, false), "同值重复提交不得再 bump（防重算风暴）");
            Assert.IsTrue(cons.SetEnabled(id, true), "重新启用是新变化");
            new AssembleStage(cons).Run(ctx, RunMode.Commit);
            ctx.Grid.TryCellIndex(150f, 150f, out c);
            Assert.IsTrue(ctx.Writer.Has(c, CellFlag.NoWater), "重新启用后必须再次禁水");
        }

        [Test]
        public void S0_Without_Constraints_Still_Marks_Inset_And_Buildable()
        {
            var ctx = new PipelineContext(MakeProject(400f));
            var res = new AssembleStage(null).Run(ctx, RunMode.Commit);
            Assert.IsTrue(res.Success);
            ctx.Grid.TryCellIndex(200f, 200f, out var center);
            Assert.IsTrue(ctx.Writer.Has(center, CellFlag.Buildable));
            Assert.IsFalse(ctx.Writer.BlocksBuilding(center));
        }

        [Test]
        public void S0_Respects_Cancellation_At_Entry()
        {
            var ctx = new PipelineContext(MakeProject(400f));
            ctx.RequestCancel();
            var res = new AssembleStage(null).Run(ctx, RunMode.Commit);
            Assert.IsTrue(res.Cancelled);
            ctx.ResetCancel();
        }

        // ─────────── H-7 / D-19：城心锚点必须显式提交 ───────────

        [Test]
        public void H7_Center_Anchor_Is_Explicit_Idempotent_And_Traceable()
        {
            var p = MakeProject(400f);
            var v0 = p.ManualVersion;

            Assert.IsFalse(p.Center.IsSet, "未显式设置 ⇒ 不启用（地形回落 TerrainProfile 默认）");

            Assert.IsTrue(p.TrySetCenterAnchor(new Vector2(120f, 80f), 300f, "field-cbd"));
            Assert.Greater(p.ManualVersion, v0, "提交必须 bump（Tracker 据此标 Center 脏）");
            Assert.IsTrue(p.Center.IsSet);
            Assert.AreEqual("field-cbd", p.Center.SourceFieldStableId, "来源必须可追溯（01 §4.4 可见性）");
            Assert.AreEqual(300f, p.Center.Radius, 1e-4f);

            var v1 = p.ManualVersion;
            Assert.IsFalse(p.TrySetCenterAnchor(new Vector2(120f, 80f), 300f, "field-cbd"),
                "同值重复提交 = 无变化，不得 bump（空重算风暴的防线）");
            Assert.AreEqual(v1, p.ManualVersion);

            Assert.IsTrue(p.TrySetCenterAnchor(new Vector2(240f, 80f), 300f, "field-cbd"), "挪位是新提交");
            Assert.Greater(p.ManualVersion, v1);

            // 关键区分：动场域 ≠ 动城心（否则 H-7 的修复等于没做）
            var fieldOnly = IntentChangePlanner.Expand(IntentChangePlanner.DirtyOf(IntentKind.Field));
            Assert.AreEqual(0, (int)(fieldOnly & DirtyBits.Center));
            Assert.IsTrue((IntentChangePlanner.DirtyOf(IntentKind.CityCenter) & DirtyBits.Center) != 0);
        }

        [Test]
        public void World_Params_Reject_Invalid_And_Bump_Only_On_Change()
        {
            var p = MakeProject(600f);
            var v0 = p.ManualVersion;

            Assert.IsFalse(p.SetWorldParams(600f, WorldScale.Small2km, 60f, 20f, 100f, 100f),
                "全等提交必须无变化（不 bump）");
            Assert.AreEqual(v0, p.ManualVersion);

            Assert.IsTrue(p.SetWorldParams(800f, WorldScale.Small2km, 60f, 20f, 100f, 100f));
            Assert.Greater(p.ManualVersion, v0);
            Assert.AreEqual(800f, p.GetGrid().WorldSize);

            // 非法组合：校验必须先于版本 bump（失败不留"已 bump"的脏状态）
            var v1 = p.ManualVersion;
            Assert.Throws<ArgumentException>(() =>
                p.SetWorldParams(800f, WorldScale.Small2km, 60f, 20f, 100f, 99f));
            Assert.AreEqual(v1, p.ManualVersion, "抛异常时不得 bump 版本");
            Assert.AreEqual(100f, p.SceneChunkSize, "非法写入不得落到字段上");
        }

        // ─────────── 通道记账守卫（M1 修的缺陷，防被改回单一 Kind） ───────────

        [Test]
        public void Channels_On_One_Object_Do_Not_Pollute_Each_Other()
        {
            var p = MakeProject(600f);

            // 城心提交：只推进 CityCenter 通道，WorldParam 通道保持不动
            var worldSeq0 = p.Ledger.SequenceOf(IntentKind.WorldParam);
            var centerSeq0 = p.Ledger.SequenceOf(IntentKind.CityCenter);
            Assert.IsTrue(p.TrySetCenterAnchor(new Vector2(120f, 80f), 300f, "field-cbd"));
            Assert.Greater(p.Ledger.SequenceOf(IntentKind.CityCenter), centerSeq0, "城心通道必须前进");
            Assert.AreEqual(worldSeq0, p.Ledger.SequenceOf(IntentKind.WorldParam),
                "世界参数通道不得被城心提交推进（否则被误读成种子变更 ⇒ Expand 成 All，H-7 修复被抵消）");

            // 世界参数提交：只推进 WorldParam 通道
            var centerSeq1 = p.Ledger.SequenceOf(IntentKind.CityCenter);
            Assert.IsTrue(p.SetWorldParams(700f, WorldScale.Small2km, 60f, 20f, 100f, 100f));
            Assert.Greater(p.Ledger.SequenceOf(IntentKind.WorldParam), worldSeq0);
            Assert.AreEqual(centerSeq1, p.Ledger.SequenceOf(IntentKind.CityCenter),
                "反向同理：改世界参数不得污染城心通道");

            // 两通道各自映射到自己的脏位，互不越权
            Assert.AreEqual(DirtyBits.Center, IntentChangePlanner.DirtyOf(IntentKind.CityCenter));
            Assert.AreEqual(DirtyBits.Seed | DirtyBits.World, IntentChangePlanner.DirtyOf(IntentKind.WorldParam));
        }

        [Test]
        public void Tracker_Dirties_On_Change_And_On_Value_Rollback()
        {
            // 本用例直接驱动 Tracker 静态态，跑完必须复位以免污染其它测试
            Collin.CityMap.EditorTooling.UndoHistoryTracker.Reset();
            try
            {
                var p = MakeProject(600f);
                p.EnsureRegistered();

                // ① 首帧只建基线，不得产生任何重算（A-33 / H-10b）
                Assert.AreEqual(DirtyBits.None, Collin.CityMap.EditorTooling.UndoHistoryTracker.Scan(),
                    "首帧必须只建基线");
                Assert.AreEqual(DirtyBits.None, Collin.CityMap.EditorTooling.UndoHistoryTracker.PendingDirty);

                // ② 提交城心 ⇒ 按 CityCenter 通道归因（不是 WorldParam）
                p.TrySetCenterAnchor(new Vector2(200f, 200f), 250f, "field-cbd");
                var d2 = Collin.CityMap.EditorTooling.UndoHistoryTracker.Scan();
                Assert.IsTrue((d2 & DirtyBits.Center) != 0, "城心通道变化必须映射为 Center");
                Assert.AreEqual(0, (int)(d2 & DirtyBits.Seed), "不得被归因成种子变更");
                Collin.CityMap.EditorTooling.UndoHistoryTracker.ConsumeDirty();

                // ③ 模拟 undo：序列化通道序列号被还原成旧值 ⇒ 与基线不等 ⇒ 仍要标脏
                //    （这条锁住"账本必须是序列化字段"的设计；若改成普通 C# 字段，undo 就看不见）
                var channels = p.Ledger.Channels;
                var idx = channels.FindIndex(c => c.Kind == IntentKind.CityCenter);
                Assert.GreaterOrEqual(idx, 0, "城心通道必须存在");
                var saved = channels[idx];
                channels[idx] = new IntentChangeLedger.Channel
                {
                    Kind = IntentKind.CityCenter, Sequence = saved.Sequence - 1,
                    Center = saved.Center, Size = saved.Size,
                };

                var d3 = Collin.CityMap.EditorTooling.UndoHistoryTracker.Scan();
                Assert.IsTrue((d3 & DirtyBits.Center) != 0, "序列号回滚（undo 语义）必须被感知为变化");
            }
            finally
            {
                Collin.CityMap.EditorTooling.UndoHistoryTracker.Reset();
            }
        }

        // ─────────── 意图登记与 stableId 唯一性（05/08 的地基） ───────────

        [Test]
        public void StableId_Is_Unique_And_ReRegister_Is_Idempotent()
        {
            var a = MakeConstraint();
            var b = MakeConstraint();
            Assert.AreNotEqual(a.StableId, b.StableId, "同类型不同实例的 stableId 必须不同");
            Assert.IsNotNull(a.Ledger, "意图对象必须带账本");
            Assert.AreEqual(0, a.Ledger.ManualVersion, "未提交过必须为 0（新对象不得被当成已改动）");

            var before = IntentRegistry.Count;
            Assert.DoesNotThrow(() => { IntentRegistry.Register(a); IntentRegistry.Register(a); });
            Assert.AreEqual(before, IntentRegistry.Count, "同实例重复登记必须幂等");

            Assert.IsTrue(IntentRegistry.TryGet(a.StableId, out var got));
            Assert.AreSame(a, got);

            IntentRegistry.Unregister(a.StableId);
            IntentRegistry.Unregister(b);
            Assert.IsFalse(IntentRegistry.TryGet(a.StableId, out _), "注销后必须查不到");
        }

        [Test]
        public void Duplicate_StableId_From_Different_Instance_Is_Rejected_With_Error()
        {
            var a = MakeConstraint();
            var b = MakeConstraint();
            // 人为制造同 id（模拟 Ctrl+D 未重发 stableId 的坏情况）
            typeof(ConstraintAsset)
                .GetField("stableId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(b, a.StableId);

            var countBefore = IntentRegistry.Count;
            // 被测行为【必须】打 error（拒绝重复 id 是设计要求的显式报错，05 §8）。
            // LogAssert.Expect 只让测试不失败，不会回退控制台计数 ⇒ 归零门禁仍会红，
            // 且堆栈含本包程序集、不适用 §1.8 豁免 ⇒ 在断言期间关闭日志输出（生产路径不改）。
            Debug.unityLogger.logEnabled = false;
            try
            {
                Assert.IsFalse(IntentRegistry.Register(b), "重复 stableId 必须登记失败");
                Assert.AreEqual(countBefore, IntentRegistry.Count);
                Assert.IsTrue(IntentRegistry.TryGet(a.StableId, out var kept));
                Assert.AreSame(a, kept, "先登记者必须保有登记位（不被后来者静默替换）");

                Assert.IsFalse(IntentRegistry.Register(null), "null 登记必须安全失败");

                // CreateInstance 会触发 OnEnable ⇒ 自动登记一个合法 id（实测结论，见 CityProject.EnsureRegistered 注释）。
                // 因此"无 stableId"必须人为构造：先摘掉自动登记位，再用反射清空 id，模拟绕过生命周期的坏状态。
                var orphan = MakeConstraint();
                IntentRegistry.Unregister(orphan);
                typeof(ConstraintAsset)
                    .GetField("stableId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .SetValue(orphan, string.Empty);
                Assert.IsFalse(IntentRegistry.Register(orphan), "无 stableId 的对象必须被拒");
                Assert.IsFalse(IntentRegistry.TryGet(string.Empty, out _));
            }
            finally { Debug.unityLogger.logEnabled = true; }
        }

        // ───────────────────────── 工具 ─────────────────────────

        private CityProject MakeProject(float size)
        {
            var p = Spawn(ScriptableObject.CreateInstance<CityProject>());
            p.EnsureRegistered();
            // 便于测试的小档：cell 2m / display 8m / chunk 20m / region 100m
            Assert.IsTrue(p.SetWorldParams(size, WorldScale.Small2km, 60f, 20f, 100f, 100f));
            return p;
        }

        private ConstraintAsset MakeConstraint()
        {
            var c = Spawn(ScriptableObject.CreateInstance<ConstraintAsset>());
            c.EnsureStableId();
            IntentRegistry.Register(c);
            return c;
        }
    }
}
