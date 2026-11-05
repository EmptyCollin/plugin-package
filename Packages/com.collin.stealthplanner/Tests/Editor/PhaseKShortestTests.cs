using System.Collections.Generic;
using Collin.StealthPlanner;
using NUnit.Framework;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 多条短解搜索（静态设计 4 地基）的守卫用例。
    /// <para>全部用<b>手工拼的假表</b>断言（同 `PhaseReachabilityTests` 的取证方式：
    /// 让哨兵周期 = 桶数 × Δt，分片桶与全局桶一一对应，「桶 b 危险」可直接写进分片）。</para>
    /// <para>最重要的三条：
    /// ① <see cref="FirstRouteArrival_MatchesPhaseSolver"/>——首条路线到达层必须与 M3 可行性一致
    ///    （引导搜索只改展开顺序、不改答案，见 D-27）；
    /// ② <see cref="ForcedReentry_NoTie_UniqueEarliestRoute"/>——折返被<b>强制且无并列最优</b>的场景，
    ///    钉住「节点按 (cell,layer) 建模」：旧实现按「格级永久首达」跳过会直接找不到路线，
    ///    制造「M3 判可行、M4 给不出动线」的自相矛盾；
    /// ③ <see cref="ReentryAfterForbidden_NumericallyExact"/>——禁边后仍须给出多条、两两边不相交、
    ///    逐跳合法（⚠️ 不再断言"第二条必含折返"：该场景有两条并列最早解，落到哪条属搜索取舍，见组 2 注释）。</para>
    /// </summary>
    public class PhaseKShortestTests
    {
        const float Dt = 1f;

        static VisibilityTable Grid(int dimX, int dimZ, int buckets,
                                    IEnumerable<(int cell, int bucket)> danger)
        {
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, 1f, dimX, dimZ, Dt);
            var slice = t.AddSlice(1, "guard", buckets * Dt, buckets);
            foreach (var (cell, bucket) in danger) t.SetSliceVisible(slice, bucket, cell, true);
            t.RebuildPermanentSafeLayer();
            return t;
        }

        static List<PhaseKShortest.Route> Run(VisibilityTable table, int start, int goal,
                                              int horizon, int step, int maxRoutes,
                                              out PhaseReachability.Failure f)
        {
            var list = new List<PhaseKShortest.Route>();
            PhaseKShortest.FindRoutes(table, start, goal, horizon, step, maxRoutes, list, out f);
            return list;
        }

        /// <summary>逐跳复核：起点层 0 可站 + 每一跳都必须是 ExpandSuccessors（与 M2 同一判据）认可的合法转移。
        /// 掩码一律用 BuildBlockedMask（危险或墙），与 PhaseKShortest 构造前沿时同一口径。</summary>
        static void AssertRouteLegal(VisibilityTable table, PhaseKShortest.Route r, int stepCells)
        {
            var n = table.CellCount;
            Assert.AreEqual(r.ArrivalBucket + 1, r.CellsPerLayer.Count, "层数 = 到达桶 + 1（下标即层号）");

            var noData = new byte[n];
            if (!PhaseReachability.TryBuildNoData(table, noData)) noData = null;

            var mask0 = new byte[n];
            PhaseReachability.BuildBlockedMask(table, 0f, noData, mask0);
            Assert.AreEqual(0, mask0[r.CellsPerLayer[0]], "起点在层 0 必须可站（非危险、非墙）");

            var here = new byte[n];
            var nextM = new byte[n];
            var buf = new int[(2 * stepCells + 1) * (2 * stepCells + 1)];
            for (var b = 1; b < r.CellsPerLayer.Count; b++)
            {
                PhaseReachability.BuildBlockedMask(table, (b - 1) * Dt, noData, here);
                PhaseReachability.BuildBlockedMask(table, b * Dt, noData, nextM);
                var cnt = PhaseReachability.ExpandSuccessors(table, r.CellsPerLayer[b - 1], stepCells, here, nextM, buf);
                var ok = false;
                for (var k = 0; k < cnt; k++) if (buf[k] == r.CellsPerLayer[b]) { ok = true; break; }
                Assert.True(ok, "层 " + b + "：从 " + r.CellsPerLayer[b - 1] + " 到 " + r.CellsPerLayer[b]
                    + " 不是合法转移（每跳都须过 M2 同一判据）");
            }
        }

        // ── 1. 一致性：首条路线的到达层 == M3 可行性判定的到达层 ──

        [Test]
        public void FirstRouteArrival_MatchesPhaseSolver()
        {
            // 走廊 [0,1,2]，goal=2；cell1 桶2危险、cell2 桶1/2危险 ⇒ 最早只能在桶4抵达（等两轮）
            var table = Grid(3, 1, 6, new[] { (1, 2), (2, 1), (2, 2) });
            var solver = PhaseSolver.Solve(table, 0, 2, 6, 1, 5, out var fs);
            Assert.True(fs.ok, fs.reason);
            Assert.True(solver.Feasible);

            var routes = Run(table, 0, 2, 6, step: 1, maxRoutes: 5, out var f);
            Assert.True(f.ok, f.reason);
            Assert.GreaterOrEqual(routes.Count, 1, "M3 判可行 ⇒ M4 必须给得出动线");
            Assert.AreEqual(solver.ArrivalBucket, routes[0].ArrivalBucket,
                "首条路线的到达层必须与 PhaseSolver（同一张表、同一判据）逐位一致");
            AssertRouteLegal(table, routes[0], 1);

            // 逐轮「禁边后重搜最早」⇒ 剩余图是上轮子图，后一条不可能比前一条更早到达
            for (var i = 1; i < routes.Count; i++)
                Assert.GreaterOrEqual(routes[i].ArrivalBucket, routes[i - 1].ArrivalBucket,
                    "路线 " + i + "：到达层必须非降");
        }

        // ── 2. 禁边后必须还能给出第二条（折返断言已迁往组 8，见下方说明）─────

        [Test]
        public void ReentryAfterForbidden_NumericallyExact()
        {
            // 精构场景（手工推演逐层验证过）：走廊 [0,1,2]，goal=2，
            //   cell1 仅桶 2 危险、cell2 仅桶 1、2 危险（其余桶全安全），horizon=6。
            // 存在两条<b>并列最早</b>（到达层同为 4）：
            //   A = [0,0,0,1,2]（在起点等两轮再冲）
            //   B = [0,1,0,1,2]（先踏一步、折返回起点、再冲）
            // 另有到达层 5 的纯等待变体 [0,0,0,0,1,2]。
            // 本用例钉的是「<b>至少给出两条、两两边不相交、每条逐跳合法</b>」：
            // 若实现按「格级永久首达」跳过，第二条会因回不到 0 而丢失 ⇒ 条数变 1、本断言变红即回归。
            var table = Grid(3, 1, 6, new[] { (1, 2), (2, 1), (2, 2) });
            var routes = Run(table, 0, 2, horizon: 6, step: 1, maxRoutes: 3, out var f);
            Assert.True(f.ok, f.reason);
            Assert.GreaterOrEqual(routes.Count, 2,
                "至少两条边不相交路线 ⇒ 若实现按「格级永久首达」跳过，第二条会因回不到 0 而丢失 ⇒ 本断言变红即回归");
            Assert.LessOrEqual(routes.Count, 3, "不得超 maxRoutes");

            AssertRouteLegal(table, routes[0], 1);
            AssertRouteLegal(table, routes[1], 1);

            // ⚠️ 「第二条必含折返」的断言已在 §6.2 剪枝②落地后<b>移除</b>：本场景存在<b>两条并列最早</b>
            //   （到达层同为 4）——[0,0,0,1,2]（起点等两轮再冲）与 [0,1,0,1,2]（先踏一步再折返）。
            //   分层 BFS 按层灌前沿先取前者；引导式优先展开离撤离点更近的节点，取后者。
            //   <b>两者都是正确的最早解</b> ⇒ 禁掉后者的边之后，第二条自然落到层 5 的纯等待变体
            //   [0,0,0,0,1,2]（合法、边不相交，只是不含折返）。
            //   拿"第二条长什么样"当硬断言＝把某种搜索取舍顺序当成契约（旧版就是这么写的，
            //   改造时被自己钉红）。折返能力改由 <see cref="ForcedReentry_NoTie_UniqueEarliestRoute"/>
            //   钉住：那个场景<b>无并列最优且折返被强制</b>，才是真正的契约。
            // 边不相交：任意两条路线不共用任何 (from,layer,to) 转移
            var seen = new HashSet<(int, int, int)>();
            foreach (var rt in routes)
                foreach (var e in EdgesOf(rt))
                    Assert.True(seen.Add(e), "两条路线共用了边 " + e + " ⇒ 边不相交被破坏");
        }

        static HashSet<(int from, int layer, int to)> EdgesOf(PhaseKShortest.Route r)
        {
            var set = new HashSet<(int, int, int)>();
            for (var b = 1; b < r.CellsPerLayer.Count; b++)
                set.Add((r.CellsPerLayer[b - 1], b - 1, r.CellsPerLayer[b]));
            return set;
        }

        // ── 3. 穷尽如实：单走廊只有一条路时绝不凑数 ──────────────

        [Test]
        public void SingleCorridor_EdgeDisjoint_StillYieldsWaitVariants()
        {
            // 走廊 [0,1,2,3] 全安全、步长 1、horizon=6。⚠️「边不相交」只禁移动/等待的
            // (from,layer,to) 三元组 ⇒「先等一拍再走」是**不同边集**的另一条合法路线，
            // 所以条数 > 1 是正确的（不是虚报凑数）。真正该钉的是：有限、两两边不相交、首条=直进。
            var table = Grid(4, 1, 6, new (int, int)[0]);
            var routes = Run(table, 0, 3, horizon: 6, step: 1, maxRoutes: 5, out var f);
            Assert.True(f.ok, f.reason);

            Assert.GreaterOrEqual(routes.Count, 2, "等待变体是不同边集 ⇒ 边不相交下至少两条");
            Assert.LessOrEqual(routes.Count, 5, "不得超 maxRoutes");
            Assert.AreEqual(3, routes[0].ArrivalBucket, "首条须是直进、第 3 层抵达（最早到达）");
            Assert.AreEqual(3, routes[0].MoveCount, "直进路线 4 个站位 3 跳移动、0 等待");

            // 两两边不相交
            var seen = new HashSet<(int, int, int)>();
            foreach (var rt in routes)
            {
                AssertRouteLegal(table, rt, 1);
                foreach (var e in EdgesOf(rt))
                    Assert.True(seen.Add(e), "两条路线共用了边 " + e + " ⇒ 边不相交被破坏");
            }
        }

        // ── 4. 等待出现在路线里（MoveCount < 层数）────────────────

        [Test]
        public void WaitingAppearsInRoute_MovesLessThanLayers()
        {
            // 目标 cell1 桶 0/1/2 危险、桶 3 起安全 ⇒ 唯一走法是原地等 3 层再一步跨入
            var table = Grid(2, 1, 5, new[] { (1, 0), (1, 1), (1, 2) });
            var routes = Run(table, 0, 1, horizon: 5, step: 1, maxRoutes: 2, out var f);
            Assert.True(f.ok, f.reason);
            Assert.GreaterOrEqual(routes.Count, 1);
            var r0 = routes[0];
            Assert.AreEqual(3, r0.ArrivalBucket, "等到危险窗关闭那层才能踏入");
            Assert.AreEqual(1, r0.MoveCount, "只有一次移动");
            Assert.AreEqual(4, r0.CellsPerLayer.Count, "层 0..3：0,0,0,1（等待展开为重复站位）");
            AssertRouteLegal(table, r0, 1);
        }

        // ── 5. 起终点重合与退化 ──────────────────────────────────

        [Test]
        public void StartEqualsGoal_SingleLayerRoute()
        {
            var table = Grid(3, 1, 4, new (int, int)[0]);
            var routes = Run(table, 1, 1, horizon: 4, step: 1, maxRoutes: 3, out var f);
            Assert.True(f.ok, f.reason);
            Assert.AreEqual(1, routes.Count);
            Assert.AreEqual(0, routes[0].ArrivalBucket);
            Assert.AreEqual(1, routes[0].CellsPerLayer.Count);
        }

        // ── 6. 入参与规模的保守失败（不返回半结果）───────────────

        [Test]
        public void InvalidInputs_FailWithReasons_NoHalfRoute()
        {
            var table = Grid(3, 1, 4, new (int, int)[0]);

            Run(null, 0, 1, 4, 1, 3, out var fNull);
            Assert.False(fNull.ok);
            Assert.That(fNull.reason, Does.Contain("表为空"));

            Run(table, -1, 1, 4, 1, 3, out var fStart);
            Assert.False(fStart.ok);
            Assert.That(fStart.reason, Does.Contain("起点格越界"));

            Run(table, 0, 99, 4, 1, 3, out var fGoal);
            Assert.False(fGoal.ok);
            Assert.That(fGoal.reason, Does.Contain("撤离点格越界"));

            // 起点 t=0 被看见 ⇒ 可读失败、空输出
            var hot = Grid(3, 1, 4, new[] { (0, 0) });
            var rs = Run(hot, 0, 2, 4, 1, 3, out var fHot);
            Assert.False(fHot.ok);
            Assert.That(fHot.reason, Does.Contain("已被看见"));
            Assert.AreEqual(0, rs.Count, "失败不得留下半条路线");

            // 超打包上限（层数 ≥ 2^21）⇒ 可读失败，不尝试分配
            Run(table, 0, 2, horizon: (1 << 21), step: 1, maxRoutes: 3, out var fHuge);
            Assert.False(fHuge.ok);
            Assert.That(fHuge.reason, Does.Contain("上限"));

            // output 传 null ⇒ 可读失败而非崩溃
            var ignored = PhaseReachability.Failure.Good();
            Assert.DoesNotThrow(() => PhaseKShortest.FindRoutes(table, 0, 2, 4, 1, 3, null, out ignored));
            Assert.False(ignored.ok);
        }

        // ── 7. 墙（D-19）在路径搜索里同样是障碍 ──────────────────

        [Test]
        public void WallCell_BlockedSameAsM2()
        {
            // 走廊 [0,1,2,3,4]，格 2 = 墙（标足迹但排除 2）。步长 2 也绕不过（中间格判据同源）。
            var table = Grid(5, 1, 4, new (int, int)[0]);
            table.MarkEvaluated(new List<int> { 0, 1, 3, 4 });
            var routes = Run(table, 0, 4, horizon: 4, step: 2, maxRoutes: 3, out var f);
            Assert.True(f.ok, f.reason);
            Assert.AreEqual(0, routes.Count, "1D 走廊里墙格把两侧完全隔断 ⇒ 无路线，也不得给半条");
        }

        // ── 8. 折返被强制、且无并列最优（§6.2 剪枝②落地后新增）──────────────
        // 组 2 的场景存在两条并列最早解，"第二条长什么样"取决于搜索取舍 ⇒ 不能当契约。
        // 本场景把折返做成<b>唯一走法</b>，因此分层 BFS 与引导式<b>必须</b>给出同一条路线。

        [Test]
        public void ForcedReentry_NoTie_UniqueEarliestRoute()
        {
            // 走廊 [0,1,2,3]、步长 1、horizon=6。危险窗逐格手工推演（层 L 站得住 ⇔ 该格桶 L 安全）：
            //   格0 安全桶 {0,2,3,4,5}，格1 安全桶 {0,1,3,4,5}，格2 安全桶 {0,3,4,5}，格3 安全桶 {0,4,5}。
            // ⇒ 层 0 只能 0→1（0 在桶1危险，等不了）；层 1 只能退回 0（1 在桶2危险、2 在桶2危险）
            //   ⇒ <b>每条合法路线都必须折返</b>，不是取舍问题。
            // ⇒ 撤离点只在桶 4/5 安全，而格 2 在第 3 层不可达（1 桶2危险、2 桶2危险）⇒ 最早到达层<b>唯一为 5</b>，
            //   路线<b>唯一</b>为 [0,1,0,1,2,3] ⇒ 无并列最优，引导搜索与分层 BFS 必须给出同一条。
            var table = Grid(4, 1, 6, new[] { (0, 1), (1, 2), (2, 1), (2, 2), (3, 1), (3, 2), (3, 3) });

            var solver = PhaseSolver.Solve(table, 0, 3, 6, 1, 1, out var fs);
            Assert.True(fs.ok, fs.reason);
            Assert.True(solver.Feasible);
            Assert.AreEqual(5, solver.ArrivalBucket, "手工推演的最早到达层");

            var routes = Run(table, 0, 3, horizon: 6, step: 1, maxRoutes: 1, out var f);
            Assert.True(f.ok, f.reason);
            Assert.AreEqual(1, routes.Count, "折返被强制 ⇒ 引导式若按「格级永久首达」建模会直接找不到路线");
            Assert.AreEqual(solver.ArrivalBucket, routes[0].ArrivalBucket,
                "并列最优不存在时，引导搜索的到达层必须与 M3 可行性逐位一致");
            AssertRouteLegal(table, routes[0], 1);

            var cells = routes[0].CellsPerLayer;
            var reentry = false;
            for (var a = 0; a + 2 < cells.Count; a++)
                for (var bb = a + 2; bb < cells.Count; bb++)
                    if (cells[a] == cells[bb] && cells[a + 1] != cells[a]) { reentry = true; break; }
            Assert.True(reentry, "本场景折返是强制的 ⇒ 没折返就说明 (cell,layer) 建模被退回格级（回归）");
        }
    }
}
