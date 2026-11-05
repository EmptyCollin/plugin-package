using System.Collections.Generic;
using Collin.StealthPlanner;
using NUnit.Framework;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 时空可见性表 + 烘焙驱动的守卫用例（M1）。
    /// <para>本文件最重要的三条是 <see cref="FillQueryRoundTrip"/>、
    /// <see cref="FillQueryRoundTrip_NonIntegerCycleNoOffByOneBucket"/>、
    /// <see cref="DegradedSourceIsSkipped_NotSilentlyBaked"/>——它们各自钉住一处
    /// <b>会静默毁掉整张表、但全程不报错</b>的失效模式：</para>
    /// <list type="number">
    /// <item>填桶与取桶口径不一致 → 查不到刚写进去的位；</item>
    /// <item>c_i 不是 Δt 整数倍时浮点边界让桶号差一格 → 同上，且只在特定周期出现；</item>
    /// <item>契约 <c>CanSeeAt</c> 在单位无巡逻组件时<b>静默降级</b>用当前位姿 → 把一瞬间冒充整条周期。</item>
    /// </list>
    /// <para>全部断言在 EditMode 完成（DESIGN D-01 的取证方式）：可见性源是<b>假世界</b>，
    /// 不依赖物理、不依赖 Play 态，也不依赖玩法包是否在场。</para>
    /// </summary>
    public class VisibilityTableBakeTests
    {
        // ── 假世界：可编程的可见性源 ──────────────────────────────

        /// <summary>
        /// 假可见性源。「谁在何时能看见哪里」由委托决定，便于构造可解析的极端场景。
        /// </summary>
        sealed class FakeSource : IVisibilitySource
        {
            public readonly List<GuardInfo> Guards = new List<GuardInfo>();
            /// <summary>返回 false 模拟「该单位没有巡逻组件 ⇒ 契约将静默降级」。</summary>
            public System.Func<int, bool> Resolveable = _ => true;
            public System.Func<int, float, Vector3, bool> See = (_, __, ___) => false;
            /// <summary>非 null 时记录每次被问到的时刻，用于证明「没拿当前位姿冒充周期」。</summary>
            public List<float> QueriedAt;

            IReadOnlyList<GuardInfo> IVisibilitySource.Guards => Guards;
            bool IVisibilitySource.CanResolvePoseAt(int actorId) => Resolveable(actorId);

            bool IVisibilitySource.CanSeeAt(int actorId, float t, Vector3 worldPoint)
            {
                QueriedAt?.Add(t);
                return See(actorId, t, worldPoint);
            }
        }

        static VisibilityTable NewTable(int dimX = 4, int dimZ = 3, float cell = 1f, float dt = 0.5f)
        {
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, cell, dimX, dimZ, dt);
            return t;
        }

        static List<int> AllCells(VisibilityTable table)
        {
            var list = new List<int>(table.CellCount);
            for (var c = 0; c < table.CellCount; c++) list.Add(c);
            return list;
        }

        // ── 1. 桶映射口径（分片用 mod、不是全局 LCM）─────────────

        [Test]
        public void BucketOf_WrapsByOwnCycle_NotGlobalLcm()
        {
            // c=10、8 桶 ⇒ 桶宽 1.25
            Assert.AreEqual(0, VisibilityTable.BucketOf(0f, 10f, 8));
            Assert.AreEqual(1, VisibilityTable.BucketOf(1.25f, 10f, 8));
            Assert.AreEqual(7, VisibilityTable.BucketOf(8.75f, 10f, 8));
            Assert.AreEqual(0, VisibilityTable.BucketOf(10f, 10f, 8), "一个整周期后须回到桶 0");
            // t=15 ⇒ 15 mod 10 = 5 ⇒ 5 / 桶宽1.25 = 4
            Assert.AreEqual(4, VisibilityTable.BucketOf(15f, 10f, 8), "15 = 一个周期 + 5 ⇒ 与 t=5 同桶");
            Assert.AreEqual(VisibilityTable.BucketOf(5f, 10f, 8),
                            VisibilityTable.BucketOf(15f, 10f, 8), "mod 回绕须与同相位时刻一致");
        }

        [Test]
        public void BucketOf_NegativeTime_StillLandsInPositiveBuckets()
        {
            // 回溯/未来都可能传负 t；C# 的 % 会保留符号，不修就会得负桶号 → 越界
            var b = VisibilityTable.BucketOf(-1.25f, 10f, 8);
            Assert.AreEqual(7, b, "-1.25 等价于 8.75（mod 10）");
            Assert.That(b, Is.InRange(0, 7));
        }

        [Test]
        public void BucketOf_ClampsAtUpperEdge_NeverOutOfRange()
        {
            // 浮点上界：t 极接近周期末尾时可能算出 buckets，必须钳回最后一桶
            Assert.AreEqual(7, VisibilityTable.BucketOf(9.999999f, 10f, 8));
            Assert.AreEqual(0, VisibilityTable.BucketOf(1f, 0f, 8), "c≤0 视为常数，恒桶 0");
            Assert.AreEqual(0, VisibilityTable.BucketOf(5f, 10f, 1), "单桶片恒 0");
        }

        // ── 2. 填桶 / 取桶 round-trip（本文件最关键的一条）────────

        [Test]
        public void FillQueryRoundTrip()
        {
            var table = NewTable();
            var slice = table.AddSlice(1, "g", 10f, 8);          // 桶宽 1.25
            const int cell = 5;
            const int bucket = 3;
            table.SetSliceVisible(slice, bucket, cell, true);
            table.RebuildPermanentSafeLayer();

            // 用「桶起点时刻」查，必须命中刚写进去的那一位
            var tOfBucket = bucket * (10f / 8);
            Assert.True(table.IsDangerousAt(cell, tOfBucket),
                        "填进桶 " + bucket + " 的位，按同桶时刻查询必须为危险（口径不一致=整张表白烘）");
            Assert.False(table.IsDangerousAt(cell + 1, tOfBucket), "不得污染邻格");
            Assert.False(table.IsDangerousAt(cell, 0f), "其他桶未填，不得连带判危险");
        }

        [Test]
        public void FillQueryRoundTrip_NonIntegerCycleNoOffByOneBucket()
        {
            // c=7、Δt=0.5 ⇒ buckets=14、桶宽 0.5；c=6.5 ⇒ buckets=13、桶宽 0.5 但不整除
            // 浮点边界最容易在这里差一格，逐桶全量 round-trip 才测得出来
            var table = NewTable();
            float cycle = 6.5f;
            var buckets = VisibilityBakeRunner.BucketsFor(cycle, table.TimeStep);
            Assert.AreEqual(13, buckets);
            var slice = table.AddSlice(7, "g", cycle, buckets);

            for (var b = 0; b < buckets; b++)
            {
                table.SetSliceVisible(slice, b, 0, true);
                var tFill = b * (cycle / buckets);
                var got = VisibilityTable.BucketOf(tFill, cycle, buckets);
                Assert.AreEqual(b, got,
                    "桶 " + b + " 的填桶时刻 t=" + tFill.ToString("0.####") +
                    " 取桶却落到 " + got + "（浮点边界错位）");
            }
        }

        // ── 3. 永久安全层（剪枝①）──────────────────────────────

        [Test]
        public void PermanentSafe_OnlyWhenInvisibleToEveryGuard_AllBuckets()
        {
            var table = NewTable();
            var a = table.AddSlice(1, "a", 4f, 4);
            var b = table.AddSlice(2, "b", 4f, 4);

            table.SetSliceVisible(a, 1, 0, true);     // 格 0 被 a 在桶 1 看见
            table.SetSliceVisible(b, 2, 1, true);     // 格 1 被 b 在桶 2 看见
            table.RebuildPermanentSafeLayer();

            Assert.False(table.IsPermanentlySafe(0), "任一刻被任一见 ⇒ 动态格");
            Assert.False(table.IsPermanentlySafe(1));
            Assert.True(table.IsPermanentlySafe(2), "从未被任何片见过 ⇒ 永久安全");

            // 永久安全必须是「任何 t 都安全」的强结论
            for (var i = 0; i < 8; i++)
            {
                Assert.False(table.IsDangerousAt(2, i * 0.7f), "永久安全格在任何时刻都不该判危险");
            }
        }

        [Test]
        public void Query_DoesNotConsultOtherGuardsSlices()
        {
            var table = NewTable();
            var a = table.AddSlice(1, "a", 2f, 2);
            table.AddSlice(2, "b", 2f, 2);            // b 全空 = 永不看见
            table.SetSliceVisible(a, 0, 0, true);
            table.RebuildPermanentSafeLayer();

            Assert.True(table.IsDangerousAt(0, 0f));
            Assert.False(table.IsDangerousAt(0, 1f), "a 只在桶 0；b 整片空 ⇒ 不得互相串台");
        }

        // ── 4. 世界↔格换算 ─────────────────────────────────────

        [Test]
        public void CellIndex_OutOfRangeIsConservativelyDangerous()
        {
            var table = NewTable(4, 3, 1f);
            Assert.True(table.TryCellIndex(new Vector3(0.5f, 0f, 0.5f), out var c0));
            Assert.AreEqual(0, c0);
            Assert.True(table.TryCellIndex(new Vector3(3.5f, 0f, 2.5f), out var cEnd));
            Assert.AreEqual(11, cEnd, "右下角 = z*(dimX)+x = 2*4+3");

            Assert.False(table.TryCellIndex(new Vector3(-0.5f, 0f, 0.5f), out _), "x 负向越界");
            Assert.False(table.TryCellIndex(new Vector3(0.5f, 0f, 3.5f), out _), "z 正向越界");

            // 越界的保守语义必须与「未烘/降级」一致：宁判危险，不谎报安全
            Assert.True(table.IsDangerousWorld(new Vector3(999f, 0f, 999f), 1f));
        }

        [Test]
        public void CellCenterWorld_IsInsideItsOwnCell()
        {
            var table = NewTable(4, 3, 2f);
            var center = table.CellCenterWorld(5);
            Assert.True(table.TryCellIndex(center, out var back));
            Assert.AreEqual(5, back, "格中心必须落回自己那格（否则烘焙采样点会错位）");
        }

        // ── 5. 烘焙驱动：填桶正确性 ────────────────────────────

        [Test]
        public void Runner_FillsPerGuardBuckets_AndRoundTrips()
        {
            var table = NewTable(4, 1, 1f, 0.5f);      // 4 格
            var src = new FakeSource();
            src.Guards.Add(new GuardInfo { actorId = 1, displayName = "g1", cycleSeconds = 4f });
            src.Guards.Add(new GuardInfo { actorId = 2, displayName = "g2", cycleSeconds = 6f });

            // g1 只在桶 2 看见格 1；g2 只在桶 5 看见格 3
            src.See = (actor, t, p) =>
            {
                var w = actor == 1 ? 4f / 8 : 6f / 12;
                var b = Mathf.FloorToInt(t / w);
                return actor == 1 ? (b == 2 && Mathf.Approximately(p.x, 1.5f))
                                  : (b == 5 && Mathf.Approximately(p.x, 3.5f));
            };

            var runner = new VisibilityBakeRunner(table, src, AllCells(table), 0.5f);
            Assert.True(runner.Step(10000), "应一次跑完");
            Assert.AreEqual(2, table.Slices.Count);
            Assert.AreEqual(8, table.Slices[0].buckets, "c=4/Δt0.5 ⇒ 8 桶");
            Assert.AreEqual(12, table.Slices[1].buckets, "c=6/Δt0.5 ⇒ 12 桶");
            Assert.AreEqual(4 * 8 + 4 * 12, (int)runner.Queries, "查询数 = Σ 桶×格");

            Assert.True(table.IsDangerousWorld(new Vector3(1.5f, 0f, 0.5f), 2f * 0.5f),
                        "g1 桶 2 的格 1 应按其时刻查得危险");
            Assert.True(table.IsDangerousWorld(new Vector3(3.5f, 0f, 0.5f), 5f * 0.5f), "g2 桶 5");
            Assert.False(table.IsDangerousWorld(new Vector3(3.5f, 0f, 0.5f), 6 * 0.5f), "g2 桶 6 未填");
            Assert.True(table.IsPermanentlySafe(0), "格 0 全程无人见 ⇒ 永久安全");
        }

        [Test]
        public void Runner_StepBudgetYieldsAndResumes_IdenticalToSingleStep()
        {
            // 分帧必须「可中断且结果不变」——否则进度条期间的中间态与最终态不同，可视化会闪
            var table = NewTable(3, 3, 1f, 1f);
            var src = new FakeSource();
            src.Guards.Add(new GuardInfo { actorId = 1, displayName = "g", cycleSeconds = 3f });
            src.See = (a, t, p) => (int)(t / 1f) == 1 && p.x > 1f;   // 桶 1、右列可见

            var cells = AllCells(table);
            var once = new VisibilityBakeRunner(NewTable(3, 3, 1f, 1f), src, cells, 1f);
            once.Step(100000);

            var stepped = new VisibilityBakeRunner(table, src, cells, 1f);
            var frames = 0;
            while (!stepped.Step(2))
            {
                Assert.Less(++frames, 100, "分帧应有限步内收敛，不能卡住");
            }

            Assert.True(stepped.Queries == once.Queries, "查询次数应与不分帧一致");

            for (var c = 0; c < 9; c++)
            {
                for (var b = 0; b < 3; b++)
                {
                    Assert.AreEqual(
                        once.Table.IsDangerousAt(c, b * 1f),
                        table.IsDangerousAt(c, b * 1f),
                        "分帧结果必须与一次跑完逐格逐桶相同");
                }
            }
        }

        [Test]
        public void Runner_SkipsUnresolvableGuard_AndReportsIt()
        {
            // 契约在单位缺巡逻组件时会静默降级用当前位姿；驱动必须整片跳过并上报，
            // 否则「某一瞬间的摆位」会被当成「整条周期」写进表
            var table = NewTable(2, 1, 1f, 1f);
            var src = new FakeSource { QueriedAt = new List<float>() };
            src.Guards.Add(new GuardInfo { actorId = 1, displayName = "无巡逻", cycleSeconds = 2f });
            src.Guards.Add(new GuardInfo { actorId = 2, displayName = "正常", cycleSeconds = 2f });
            src.Resolveable = id => id != 1;
            src.See = (a, t, p) => a == 2;

            var runner = new VisibilityBakeRunner(table, src, AllCells(table), 1f);
            Assert.True(runner.Step(10000));

            Assert.AreEqual(1, runner.SkippedGuards.Count, "缺巡逻组件的哨兵必须被登记，不能静默");
            Assert.AreEqual("无巡逻", runner.SkippedGuards[0]);
            Assert.AreEqual(1, table.Slices.Count, "被跳过的不得留下空分片冒充「安全」");

            // 2 格 × 2 桶 = 4 次查询，全部属于「正常」那个哨兵；一次都没问过降级那个
            Assert.AreEqual(4, src.QueriedAt.Count);
            Assert.AreEqual(runner.Queries, src.QueriedAt.Count, "Queries 计数应与实际发出的查询一致");
            foreach (var q in src.QueriedAt)
            {
                Assert.That(q, Is.InRange(0f, 2f), "查询时刻须落在该哨兵自身周期内（c=2）");
            }
        }

        [Test]
        public void Runner_EmptyGuards_StillFinalizesAndMarksAllDangerousFree()
        {
            var table = NewTable(2, 2, 1f, 1f);
            var src = new FakeSource();
            var runner = new VisibilityBakeRunner(table, src, AllCells(table), 1f);

            Assert.True(runner.Step(10));
            Assert.True(runner.Done);
            Assert.AreEqual(0, table.Slices.Count);
            Assert.AreEqual(4, table.PermanentlySafeCellCount, "无哨兵 ⇒ 全场永久安全（求解器可无条件通行）");
        }

        [Test]
        public void Runner_ZeroBudget_StillMakesProgress()
        {
            // maxQueries<=0 若直接返回，Editor 泵会以为自己没推进而死循环
            var table = NewTable(2, 2, 1f, 1f);
            var src = new FakeSource();
            src.Guards.Add(new GuardInfo { actorId = 1, displayName = "g", cycleSeconds = 2f });
            var runner = new VisibilityBakeRunner(table, src, AllCells(table), 1f);
            Assert.False(runner.Done);
            runner.Step(0);
            Assert.AreEqual(1, (int)runner.Queries, "预算 0 时也要至少推进一格，保证可收敛");
        }

        // ── 6. 体积（验收「表体积在预算内」的读数口径）──────────

        [Test]
        public void PackedByteCount_GrowsLinearlyWithBuckets_NotLcm()
        {
            var table = NewTable(100, 100, 0.5f, 0.5f);
            var cells = table.CellCount;
            Assert.AreEqual(10000, cells);

            // 两个 c=20s 的哨兵：分片各自 ⌈20/0.5⌉=40 桶 ⇒ 10000×40/8×2 = 100KB
            table.AddSlice(1, "a", 20f, 40);
            table.AddSlice(2, "b", 20f, 40);
            var perSlice = (long)40 * cells / 8;
            Assert.AreEqual(2 * perSlice + cells, table.PackedByteCount,
                "含永久安全层每格 1 字节；这是分片口径（非 LCM）下的体积读数");

            // 对照：若按全局 LCM 物化（两周期不同则 lcm 远大于各自），体积会显著更大 ⇒ 反证 D-02 的必要性
            var one = NewTable(100, 100, 0.5f, 0.5f);
            one.AddSlice(1, "a", 20f, 40);
            Assert.AreEqual(perSlice + 10000, one.PackedByteCount);
        }

        [Test]
        public void ZeroVisibilityGuard_IsReported_AndPersistedOnTable()
        {
            // D-17 的第二道防线必须有**出口**。此前 ScanZeroVisibility 只在 runner 内部记一份、
            // 无人可读 ⇒ 烘出「全场安全」的表被下游当可信数据（本轮实测撞见：切场景后视野体没注册，
            // 三个哨兵全瞎，永久安全格算成 11664=全部格，日志与表里都没有任何痕迹）。
            var table = NewTable(3, 1, 1f, 1f);
            var src = new FakeSource();
            src.Guards.Add(new GuardInfo { actorId = 1, displayName = "瞎子", cycleSeconds = 2f });
            src.Guards.Add(new GuardInfo { actorId = 2, displayName = "正常", cycleSeconds = 2f });
            src.See = (actorId, __, ___) => actorId == 2;     // 1 号整条周期一个格都没看见

            var runner = new VisibilityBakeRunner(table, src, AllCells(table), 1f);
            Assert.True(runner.Step(10000), "应一次跑完并收尾");

            Assert.AreEqual(1, runner.ZeroVisibilityGuards.Count, "零可见必须被扫出");
            Assert.AreEqual("瞎子", runner.ZeroVisibilityGuards[0]);

            // 关键断言：名单必须落到**表**上（持久化出口），否则面板/资产读取方拿不到警告
            Assert.AreEqual(1, table.UnknownGuardNames.Count,
                "零可见哨兵必须写进表级留痕；只在 runner 内存里等于没有防线");
            Assert.AreEqual("瞎子", table.UnknownGuardNames[0]);

            // 且「正常」那个不得被误报（防线不能顺手把可信数据也涂脏）
            Assert.DoesNotThrow(() =>
            {
                foreach (var n in table.UnknownGuardNames)
                    Assert.AreNotEqual("正常", n, "看得见的哨兵不得进可疑名单");
            });
        }
    }
}
