using System.Collections.Generic;
using Collin.StealthPlanner;
using NUnit.Framework;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 相位可达传播（M2 核心算法，亦是 M3 求解器地基）的守卫用例。
    /// <para>全部用<b>手工拼出来的假表</b>断言：不依赖玩法包、不依赖场景、不依赖 Play 态
    /// （DESIGN D-01 的取证方式）。危险位的填法刻意与「全局桶」对齐——
    /// 让哨兵 <c>cycleSeconds == buckets × Δt</c>，此时 <c>BucketOf(t) </c>恒等于全局桶号，
    /// 于是「桶 b 危险」可以直接写进分片而不必换算取模。</para>
    /// <para>最关键的一条是 <see cref="CannotJumpOverBlockingCell"/>：
    /// 若实现只判起、止格而漏掉沿途格，玩家就能一步跳过掩体/墙体，
    /// 可达域会虚胖成「哪儿都能去」——那正是设计工具最坏的假承诺。</para>
    /// </summary>
    public class PhaseReachabilityTests
    {
        const float Dt = 1f;        // 全局时间步长（表内口径）

        /// <summary>
        /// 造一张 <c>dimX × 1</c> 的走廊表；危险位按「(格, 桶)」清单写入单个哨兵分片。
        /// 周期取 <c>buckets × Dt</c> ⇒ 分片桶与全局桶一一对应。
        /// </summary>
        static VisibilityTable Corridor(int dimX, int buckets, IEnumerable<(int cell, int bucket)> danger)
        {
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, 1f, dimX, 1, Dt);
            var slice = t.AddSlice(1, "guard", buckets * Dt, buckets);
            foreach (var (cell, bucket) in danger)
                t.SetSliceVisible(slice, bucket, cell, true);
            t.RebuildPermanentSafeLayer();
            return t;
        }

        static PhaseReachability.Field Run(VisibilityTable table, int start, int buckets,
                                           int stepCells, out PhaseReachability.Failure f)
            => PhaseReachability.Compute(table, start, buckets, stepCells, out f);

        // ── 1. 沿途格必须安全（不可跳过掩体）────────────────────

        [Test]
        public void CannotJumpOverBlockingCell()
        {
            // 走廊 5 格：格 2 在所有桶都危险（一堵墙）。一步能跨 2 格。
            var table = Corridor(5, 4, new[] { (2, 0), (2, 1), (2, 2), (2, 3) });
            var f = Run(table, 0, 4, stepCells: 2, out var fail);

            Assert.True(fail.ok, fail.reason);
            Assert.True(f.IsReachable(0, 0), "起点桶 0 可达");
            Assert.True(f.IsReachable(1, 1), "格 1 应在桶 1 可达");
            Assert.False(f.IsReachable(1, 2), "格 2 是墙，任何桶都不可达");

            // 若实现漏掉「沿途格」判定，就会 1 → 3 一步跨过去（起终都安全、中间是墙）
            for (var b = 0; b < 4; b++)
            {
                Assert.False(f.IsReachable(b, 3), "桶 " + b + "：不得越过墙到达格 3");
                Assert.False(f.IsReachable(b, 4), "桶 " + b + "：不得越过墙到达格 4");
            }
            Assert.AreEqual(float.PositiveInfinity, f.EarliestSeconds[4],
                "被墙隔住的格必须记为不可达（不是某个很晚的时刻）");
        }

        [Test]
        public void BlockingCell_ReleasesLater_ThenPathOpens()
        {
            // 同一条走廊，但格 2 只在桶 0、1 危险（哨兵转身就走开）⇒ 桶 2 起应能通行
            var table = Corridor(5, 5, new[] { (2, 0), (2, 1) });
            var f = Run(table, 0, 5, stepCells: 1, out var fail);   // 每步 1 格，必须逐格挪

            Assert.True(fail.ok, fail.reason);
            Assert.True(f.IsReachable(2, 1), "桶 2 应已站上新格（桶 0/1 只能待在 0/1 格）");
            Assert.Greater(f.EarliestSeconds[2], 0f, "格 2 危险时不得提前到达");
            Assert.False(float.IsPositiveInfinity(f.EarliestSeconds[4]),
                "障碍解除后走廊尽头必须可达");
        }

        // ── 2. 原地等待（潜行的命脉）──────────────────────────

        [Test]
        public void CanWaitInPlace_ThroughDangerWindow()
        {
            // 格 1 在桶 1、2 危险；格 0 全程安全 ⇒ 玩家可在格 0 等到桶 3 再前进
            var table = Corridor(4, 5, new[] { (1, 1), (1, 2) });
            var f = Run(table, 0, 5, stepCells: 1, out var fail);

            Assert.True(fail.ok, fail.reason);
            for (var b = 0; b < 5; b++)
            {
                Assert.True(f.IsReachable(b, 0), "桶 " + b + "：等待必须让格 0 一直可达");
            }
            // 格 1 要等危险窗过去：桶 3 时前进 ⇒ 到达时刻 3
            Assert.AreEqual(3f, f.EarliestSeconds[1], 1e-4f,
                "格 1 的最早到达时刻应是危险窗结束后（等出来的，不是冲出来的）");
        }

        // ── 3. 半径口径：欧氏，不是切比雪夫方框 ───────────────

        [Test]
        public void StepRadius_IsEuclidean_NotChebyshevBox()
        {
            // 3×3，中心 (1,1) 出发，stepCells=1 ⇒ 对角 (2,2) 的平方距离 2 > 1
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, 1f, 3, 3, Dt);
            t.AddSlice(1, "none", 4f, 4);           // 空分片：全程无人可见
            t.RebuildPermanentSafeLayer();

            var f = Run(t, 4, 4, stepCells: 1, out var fail);   // cell 4 = (1,1)
            Assert.True(fail.ok, fail.reason);

            Assert.True(f.IsReachable(1, 3), "正左 (0,1) 一步可达");
            Assert.True(f.IsReachable(1, 5), "正右 (2,1) 一步可达");
            Assert.True(f.IsReachable(1, 1), "正下 (1,0) 一步可达");
            Assert.False(f.IsReachable(1, 8), "对角 (2,2) 欧氏距离 √2 > 1 ⇒ 一步不该到");
        }

        // ── 4. 失败原因必须可读（UI 直接显示，绝不回空场让策划猜）──

        [Test]
        public void StartCellExposedAtT0_FailsWithReason()
        {
            var table = Corridor(5, 4, new[] { (0, 0) });
            var f = Run(table, 0, 4, 1, out var fail);
            Assert.False(fail.ok);
            Assert.That(fail.reason, Does.Contain("出发格"));
            Assert.AreEqual(0, f.Buckets, "失败时不得给出半张可用的场");
        }

        [Test]
        public void BadInputs_AllReportReasons()
        {
            var good = Corridor(3, 4, new[] { (1, 1) });

            Assert.False(PhaseReachability.Compute(null, 0, 4, 1, out var a).CellCount > 0);
            Assert.That(a.reason, Does.Contain("表为空"));

            PhaseReachability.Compute(good, 0, 0, 1, out var b);
            Assert.False(b.ok);

            PhaseReachability.Compute(good, 999, 4, 1, out var c);
            Assert.False(c.ok);
            Assert.That(c.reason, Does.Contain("越界"));
        }

        // ── 5. 结果自身的不变式 ────────────────────────────────

        [Test]
        public void EarliestSeconds_MatchFirstReachableBucket()
        {
            var table = Corridor(5, 6, new[] { (3, 0), (3, 1) });
            var f = Run(table, 0, 6, 1, out var fail);
            Assert.True(fail.ok, fail.reason);

            for (var cell = 0; cell < 5; cell++)
            {
                if (float.IsPositiveInfinity(f.EarliestSeconds[cell])) continue;
                int first = -1;
                for (var b = 0; b < f.Buckets; b++)
                {
                    if (f.IsReachable(b, cell)) { first = b; break; }
                }
                Assert.AreEqual(first * Dt, f.EarliestSeconds[cell], 1e-4f,
                    "格 " + cell + " 的最早时刻必须等于其首个可达桶");
            }
        }

        [Test]
        public void ReachableSet_IsMonotone_WhenNoDanger()
        {
            // 全程无人可见 ⇒ 可达域只能随时间扩大（没有时间压力的纯几何扩散）
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, 1f, 6, 6, Dt);
            t.AddSlice(1, "none", 5f, 5);
            t.RebuildPermanentSafeLayer();

            var f = Run(t, 0, 12, 1, out var fail);        // 6×6 自角落出发，最远格需 10 步 ⇒ 给足 12 桶
            Assert.True(fail.ok, fail.reason);
            var prev = 0;
            for (var b = 0; b < f.Buckets; b++)
            {
                var now = f.ReachableCountInBucket(b);
                Assert.That(now, Is.GreaterThanOrEqualTo(prev), "桶 " + b + " 可达格数不得减少");
                prev = now;
            }
            Assert.AreEqual(36, f.ReachableCountInBucket(11), "无障碍且桶数给足时应铺满整张表");
            Assert.Less(f.ReachableCountInBucket(4), 36, "桶 4 只该扩到曼哈顿距离 4（远未满铺）");
        }

        [Test]
        public void StepCellsFor_FloorsAndMayBeZero()
        {
            // 口径：floor(速度 × Δt ÷ 格长)。向上取整会凭空造出走不到的距离 ⇒ 必须向下。
            Assert.AreEqual(3, PhaseReachability.StepCellsFor(3.2f, 0.5f, 0.5f),
                            "3.2m/s×0.5s=1.6m ÷0.5m格 = 3.2 ⇒ 3 格（不是 4）");
            Assert.AreEqual(2, PhaseReachability.StepCellsFor(2.5f, 0.5f, 0.5f),
                            "1.25m ÷0.5m = 2.5 ⇒ 2 格（不是 3）");
            Assert.AreEqual(1, PhaseReachability.StepCellsFor(1.0f, 1.0f, 1.0f), "1m ÷1m = 1");
            Assert.AreEqual(0, PhaseReachability.StepCellsFor(0.9f, 1.0f, 1.0f),
                            "0.9m 走不到一格 ⇒ 0（只能原地等）。旧实现在这里兜成 1 = 虚构移动力，是 bug");
            Assert.AreEqual(0, PhaseReachability.StepCellsFor(0f, 0.5f, 0.5f), "非法输入同样给 0");
        }

        [Test]
        public void StepCells_Zero_StillWaitsInPlace()
        {
            // stepCells=0 的合法语义：只能原地等。障碍解除后也**不该**能挪窝（本用例只验不崩、
            // 且起点持续可达）。
            var table = Corridor(4, 5, new[] { (1, 0) });
            var f = Run(table, 0, 5, stepCells: 0, out var fail);
            Assert.True(fail.ok, fail.reason);
            for (var b = 0; b < 5; b++)
                Assert.True(f.IsReachable(b, 0), "桶 " + b + "：只能等待，但起点须持续可达");
            for (var cell = 1; cell < 4; cell++)
                Assert.True(float.IsPositiveInfinity(f.EarliestSeconds[cell]),
                    "stepCells=0 时格 " + cell + " 不得被算成可达");
        }

        // ── 6. 表侧新增查询（M2 热区与诊断的地基）─────────────

        [Test]
        public void BuildDangerMask_ExactLengthAndNoPartialWrite()
        {
            var table = Corridor(4, 3, new[] { (2, 1) });
            var mask = new byte[table.CellCount];
            Assert.True(table.BuildDangerMask(1f, mask), "长度够应成功");
            Assert.AreEqual(1, mask[2], "桶 1 的格 2 危险");
            Assert.AreEqual(0, mask[1]);
            Assert.AreEqual(0, mask[3]);

            // 本用例的表没标足迹 ⇒ 无数据保护不介入，墙要由下面两条单独验
            var shortMask = new byte[2];
            Assert.False(table.BuildDangerMask(1f, shortMask), "长度不足必须返回 false");
            Assert.AreEqual(0, shortMask[0], "失败路径不得部分写入");
            Assert.AreEqual(0, shortMask[1]);
        }

        // ── 7. 足迹与「无数据格」（真实烘焙里墙就是这么表达的）────

        [Test]
        public void NoDataCells_DefaultToWalkable_WhenFootprintAbsent()
        {
            var table = Corridor(5, 3, new[] { (2, 0), (2, 1), (2, 2) });

            Assert.False(table.HasFootprint, "没标足迹时不得谎称有");
            var noData = new byte[table.CellCount];
            Assert.True(table.BuildNoDataMask(noData));
            for (var c = 0; c < 5; c++)
                Assert.AreEqual(0, noData[c], "无足迹信息时按「全部有数据」兼容处理");
        }

        [Test]
        public void MarkEvaluated_OnlyListedCells_HaveData()
        {
            var table = Corridor(6, 2, new[] { (0, 0) });
            table.MarkEvaluated(new List<int> { 0, 1, 4, 5 });   // 2、3 是墙：没打过射线

            Assert.True(table.HasFootprint);
            Assert.True(table.IsEvaluated(0));
            Assert.False(table.IsEvaluated(2), "墙格必须标为无数据");
            Assert.False(table.IsEvaluated(3));

            var mask = new byte[table.CellCount];
            table.BuildNoDataMask(mask);
            Assert.AreEqual(1, mask[2], "无数据格在独立掩码里为 1");
            Assert.AreEqual(0, mask[1]);
            // 注意：BuildDangerMask 仍只表达「被看见」，不掺无数据
            var danger = new byte[table.CellCount];
            table.BuildDangerMask(0f, danger);
            Assert.AreEqual(1, danger[0]);
        }

        /// <summary>造一条「谁都看不见」的空分片走廊（危险位全 0；格 2 只在标了足迹时才是墙）。</summary>
        static VisibilityTable BlindCorridor(int buckets)
        {
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, 1f, 5, 1, Dt);
            t.AddSlice(1, "blind", buckets * Dt, buckets);
            t.RebuildPermanentSafeLayer();
            return t;
        }

        [Test]
        public void NoDataCell_BlocksPropagation_ButOnlyWhenFootprintRecorded()
        {
            const int Buckets = 4;

            // ① 标了足迹、格 2 未被探测（= 一堵墙）：危险位全 0，唯一能挡住玩家的就是足迹。
            //    这条**端到端**验 BuildBlockedMask 里的「或进无数据格」——
            //    上面 CannotJumpOverBlockingCell 用的是 danger 位，
            //    测不到「只有无数据」这一路；删掉合并逻辑时只有本条会红。
            var walled = BlindCorridor(Buckets);
            walled.MarkEvaluated(new List<int> { 0, 1, 3, 4 });
            Assert.False(walled.IsEvaluated(2), "前置：格 2 未被探测");

            var fW = Run(walled, 0, Buckets, stepCells: 2, out var failW);
            Assert.True(failW.ok, failW.reason);
            Assert.True(fW.IsReachable(1, 1), "前置：格 1 可达");
            for (var b = 0; b < Buckets; b++)
            {
                Assert.False(fW.IsReachable(b, 2), "桶 " + b + "：墙格不得可达");
                Assert.False(fW.IsReachable(b, 3), "桶 " + b + "：不得越过墙格");
                Assert.False(fW.IsReachable(b, 4), "桶 " + b + "：不得越过墙格");
            }

            // ② 不标足迹（向后兼容语义）：同一几何会被判「可走」⇒ 证明上面的拦截确实来自足迹
            var bare = BlindCorridor(Buckets);
            Assert.False(bare.HasFootprint, "前置：未标足迹时按无数据处理");
            var fB = Run(bare, 0, Buckets, stepCells: 2, out var failB);
            Assert.True(failB.ok, failB.reason);
            Assert.True(fB.IsReachable(1, 2),
                "无足迹时按「全部有数据」兼容 ⇒ 格 2 可达（这正是真实烘焙必须标足迹的原因）");
        }

        [Test]
        public void UnknownGuardNames_RecordedAtTableLevel()
        {
            var table = Corridor(4, 2, new[] { (1, 0) });
            Assert.AreEqual(0, table.UnknownGuardNames.Count, "初始应无缺失记录");

            table.SetUnknownGuardNames(new List<string> { "Body", "", null, "Sweeper" });
            Assert.AreEqual(2, table.UnknownGuardNames.Count, "空名与 null 不记账");
            Assert.AreEqual("Body", table.UnknownGuardNames[0]);

            table.SetUnknownGuardNames(null);
            Assert.AreEqual(0, table.UnknownGuardNames.Count, "传 null 应清空而非累加");
        }

        [Test]
        public void VisibleCellCountInBucket_CountsOnlyThatBucket()
        {
            var table = Corridor(4, 3, new[] { (1, 0), (2, 0), (3, 1) });
            Assert.AreEqual(2, table.VisibleCellCountInBucket(0, 0f), "桶 0 可见 2 格");
            Assert.AreEqual(1, table.VisibleCellCountInBucket(0, 1f), "桶 1 可见 1 格");
            Assert.AreEqual(0, table.VisibleCellCountInBucket(0, 2f));
            Assert.AreEqual(0, table.VisibleCellCountInBucket(-1, 0f), "越界分片给 0，不抛");
        }

        // ── 8. 累计热区的语义：「截至此刻」而非「整窗并集」──────────

        [Test]
        public void FillReachableUpTo_GrowsMonotonically_AndEndsAtEverReachable()
        {
            // 这条用例是「拖时间轴时累计层一动不动」那个 bug 的直接守卫：
            // 若实现拷的是 EverReachable（整窗并集），下面「中途必须严格小于满值」会立刻红。
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, 1f, 6, 6, Dt);
            t.AddSlice(1, "none", 5f, 5);
            t.RebuildPermanentSafeLayer();

            var f = Run(t, 0, 12, 1, out var fail);
            Assert.True(fail.ok, fail.reason);
            var n = f.CellCount;
            var full = f.EverReachable;
            var fullCount = 0;
            for (var c = 0; c < n; c++) if (full[c] != 0) fullCount++;

            var mask = new byte[n];
            var prev = 0;
            for (var b = 0; b < f.Buckets; b++)
            {
                Assert.True(f.FillReachableUpTo(b, mask), "桶 " + b + " 应成功");
                var cnt = 0;
                for (var c = 0; c < n; c++) if (mask[c] != 0) cnt++;
                Assert.That(cnt, Is.GreaterThanOrEqualTo(prev), "桶 " + b + "：累计可达不得倒退");
                prev = cnt;
                if (b == 4)
                    Assert.Less(cnt, fullCount,
                        "桶 4 的累计必须<b>小于</b>整窗并集——相等就说明拷成了 EverReachable（时间轴失效）");
            }
            Assert.AreEqual(fullCount, prev, "最后一桶的累计应恰好等于整窗并集");

            // 前缀包含关系：本桶可达的格，一定在「截至本桶」的累计里
            Assert.True(f.FillReachableUpTo(6, mask));
            for (var c = 0; c < n; c++)
                if (f.IsReachable(6, c)) Assert.AreEqual(1, mask[c], "格 " + c + "：累计漏掉了本桶可达格");
        }

        [Test]
        public void FillReachableUpTo_RejectsBadInputs_WithoutPartialWrite()
        {
            var t = Corridor(4, 4, new[] { (2, 0) });
            var f = Run(t, 0, 4, 1, out var fail);
            Assert.True(fail.ok, fail.reason);

            var shortMask = new byte[f.CellCount - 1];
            shortMask[0] = 7;
            Assert.False(f.FillReachableUpTo(1, shortMask), "长度不足必须返回 false");
            Assert.AreEqual(7, shortMask[0], "失败路径不得部分写入");

            Assert.False(f.FillReachableUpTo(1, null), "null 掩码不得抛异常");

            var mask = new byte[f.CellCount];
            Assert.True(f.FillReachableUpTo(999, mask), "超范围桶号应封顶到最后一桶");
            var capped = CountOnes(mask);

            Assert.False(f.FillReachableUpTo(-1, mask), "负桶=还没开始，属非法输入");
            Assert.AreEqual(0, CountOnes(mask), "非法桶号也得把上一刻的结果清掉，否则残留热区");

            Assert.That(capped, Is.GreaterThan(0), "封顶到最后一桶时应有内容");
        }

        static int CountOnes(byte[] a)
        {
            var n = 0;
            for (var i = 0; i < a.Length; i++) if (a[i] != 0) n++;
            return n;
        }
    }
}
