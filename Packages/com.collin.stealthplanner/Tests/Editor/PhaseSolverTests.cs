using System.Collections.Generic;
using Collin.StealthPlanner;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 相位求解器（静态设计 2「有无解」+ 静态设计 3「无解 Top-K」）的守卫用例。
    /// <para>全部用<b>手工拼的假表</b>断言：不依赖玩法包、场景、Play 态。
    /// 求解器复用 <see cref="PhaseReachability"/> 的判据，这里验的是<b>求解层</b>新增的行为
    /// （可行性判定、目标早退、Top-K 候选筛选与排序、视界上限、入参越界），
    /// 可达传播本身的语义由 <c>PhaseReachabilityTests</c> 独立覆盖，不在此重测。</para>
    /// </summary>
    public class PhaseSolverTests
    {
        const float Dt = 1f;

        static VisibilityTable Corridor(int dimX, int buckets, List<(int cell, int bucket)> danger)
        {
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, 1f, dimX, 1, Dt);
            var slice = t.AddSlice(1, "guard", buckets * Dt, buckets);
            for (var i = 0; i < danger.Count; i++)
                t.SetSliceVisible(slice, danger[i].bucket, danger[i].cell, true);
            t.RebuildPermanentSafeLayer();
            return t;
        }

        static PhaseSolver.Result Run(VisibilityTable table, int start, int goal,
                                      int horizon, int step, int topK,
                                      out PhaseReachability.Failure f)
            => PhaseSolver.Solve(table, start, goal, horizon, step, topK, out f);

        // ── 1. 有解：目标早退给出最早到达 ─────────────────────────

        [Test]
        public void ReachableGoal_Feasible_EarlyArrivalMatchesDistance()
        {
            // 5 格 1D 走廊、全程无人可见、步长 1 ⇒ 到格 4 恰需 4 步 = 4 桶
            var table = Corridor(5, 8, new List<(int, int)>());
            var r = Run(table, 0, 4, 8, 1, 5, out var f);
            Assert.True(f.ok, f.reason);
            Assert.True(r.Feasible, "无障碍时目标应可达");
            Assert.AreEqual(4, r.ArrivalBucket, "步长 1、距离 4 ⇒ 第 4 层首达");
            Assert.AreEqual(4 * Dt, r.EarliestArrivalSeconds, 1e-4f);
            Assert.GreaterOrEqual(r.CellsReached, 5, "到目标前累计应至少铺满 0..4");
        }

        [Test]
        public void GoalBlockedEarly_WaitsThenDashes_EarliestIsWhenWindowOpens()
        {
            // 2 格：格 1（目标）在桶 0/1/2 都被看见，桶 3 起才安全 ⇒ 最早只能第 3 层冲到
            var table = Corridor(2, 4, new List<(int, int)> { (1, 0), (1, 1), (1, 2) });
            var r = Run(table, 0, 1, 4, 1, 5, out var f);
            Assert.True(f.ok, f.reason);
            Assert.True(r.Feasible);
            Assert.AreEqual(3, r.ArrivalBucket, "等待到危险窗关闭那层才能踏入目标");
        }

        [Test]
        public void StartEqualsGoal_ArrivesAtBucketZero()
        {
            // 起点已经在撤离点上：第 0 层即命中，不该报「无解」
            var table = Corridor(3, 4, new List<(int, int)>());
            var r = Run(table, 2, 2, 4, 1, 5, out var f);
            Assert.True(f.ok, f.reason);
            Assert.True(r.Feasible, "起点==目标应判可行");
            Assert.AreEqual(0, r.ArrivalBucket);
            Assert.AreEqual(0f, r.EarliestArrivalSeconds, 1e-4f);
        }

        // ── 2. 无解：Top-K 候选必须「玩家到过」且「绕墙连得到撤离点」──

        [Test]
        public void UnreachableGoal_FallbackRanksByWallDistance()
        {
            // 3 格、格 2（目标）全桶被看见 ⇒ 不可达；但走廊无墙，格 0/1 玩家到过且能连到目标
            var table = Corridor(3, 4, new List<(int, int)> { (2, 0), (2, 1), (2, 2), (2, 3) });
            var r = Run(table, 0, 2, 4, 1, 5, out var f);
            Assert.True(f.ok, f.reason);
            Assert.False(r.Feasible);
            Assert.AreEqual(2, r.FallbackTopK.Count, "候选=玩家到过且连得到目标的格 0、格 1");
            // 按绕墙格步数升序：格 1（距目标 1）应在格 0（距目标 2）之前
            Assert.AreEqual(1, r.FallbackTopK[0].cell);
            Assert.AreEqual(1, r.FallbackTopK[0].DistCellsToGoal);
            Assert.AreEqual(0, r.FallbackTopK[1].cell);
            Assert.AreEqual(2, r.FallbackTopK[1].DistCellsToGoal);
        }

        [Test]
        public void TopKIsTruncatedToRequestedCount()
        {
            var table = Corridor(3, 4, new List<(int, int)> { (2, 0), (2, 1), (2, 2), (2, 3) });
            var r = Run(table, 0, 2, 4, 1, 1, out _);
            Assert.AreEqual(1, r.FallbackTopK.Count, "topK=1 时只保留最近的一个");
            Assert.AreEqual(1, r.FallbackTopK[0].cell, "保留的应是绕墙更近的格 1");
        }

        [Test]
        public void WallSeparation_YieldsEmptyFallback_NotLyingNearMiss()
        {
            // 5 格、格 2 是墙（未探测）。玩家困在左侧 {0,1}，右侧撤离点用墙隔开。
            // 距离场不穿墙 ⇒ 左侧格到撤离点距离 = ∞ ⇒ Top-K 必须为空。
            // 这是**诚实的「没有任何可达位置能接近撤离点」**，比谎报「就差一格」正确。
            var table = Corridor(5, 4, new List<(int, int)>());
            table.MarkEvaluated(new List<int> { 0, 1, 3, 4 });      // 格 2 无数据 = 墙
            var r = Run(table, 0, 4, 4, 1, 5, out var f);
            Assert.True(f.ok, f.reason);
            Assert.False(r.Feasible, "墙把两侧彻底隔断 ⇒ 不可达");
            Assert.AreEqual(0, r.FallbackTopK.Count,
                "左侧到不了、也就无法接近撤离点 ⇒ 不得给出误导性的近邻候选");
        }

        [Test]
        public void FallbackOnlyIncludesCellsPlayerActuallyReached()
        {
            // 步长 0 ⇒ 只能原地等待，永远停在格 0。撤离点在格 4（走廊无墙，距离场连得到）。
            // 候选必须「玩家到过」⇒ 只有格 0 入选，格 1~3 虽连得到撤离点但从没到过。
            var table = Corridor(5, 4, new List<(int, int)>());
            var r = Run(table, 0, 4, 4, 0, 5, out var f);
            Assert.True(f.ok, f.reason);
            Assert.False(r.Feasible, "步长 0 只能等待，到不了格 4");
            Assert.AreEqual(1, r.FallbackTopK.Count, "只有起点格 0 是「到过且连得到」");
            Assert.AreEqual(0, r.FallbackTopK[0].cell);
            Assert.AreEqual(0f, r.FallbackTopK[0].EarliestSeconds, 1e-4f);
        }

        // ── 3. 入参与视界上限的保守失败 ───────────────────────────

        [Test]
        public void OutOfRangeInputs_ReportReasons_NoHalfResult()
        {
            var table = Corridor(3, 4, new List<(int, int)>());

            Assert.False(PhaseSolver.Solve(null, 0, 1, 4, 1, 5, out var fNull).Feasible);
            Assert.That(fNull.reason, Does.Contain("表为空"));

            Run(table, -1, 2, 4, 1, 5, out var fStart);
            Assert.False(fStart.ok);
            Assert.That(fStart.reason, Does.Contain("起点格越界"));

            Run(table, 0, 99, 4, 1, 5, out var fGoal);
            Assert.False(fGoal.ok);
            Assert.That(fGoal.reason, Does.Contain("撤离点格越界"));
        }

        [Test]
        public void Horizon_UsesSettingsAndCapsAtHardLimit()
        {
            var s = ScriptableObject.CreateInstance<PlannerBakeSettings>();
            try
            {
                // 默认 N=4、P=60、Δt=0.5 ⇒ BucketsPerPhase=120 ⇒ 视界 480，未超限
                var ok = PhaseSolver.Horizon(s, out var whyOk);
                Assert.AreEqual(480, ok, "默认参数视界 = N×⌈P/Δt⌉");
                Assert.IsNull(whyOk);

                // 顶到 N=64、Δt=0.05、P=60 ⇒ ⌈P/Δt⌉=1200 ⇒ 64×1200 远超 4096
                var so = new SerializedObject(s);
                so.FindProperty("_maxPhases").intValue = 64;
                so.FindProperty("_timeStep").floatValue = 0.05f;
                so.FindProperty("_phaseWindow").floatValue = 60f;
                so.ApplyModifiedPropertiesWithoutUndo();

                var over = PhaseSolver.Horizon(s, out var whyOver);
                Assert.AreEqual(-1, over, "超硬上限不得返回一个会被算爆的视界");
                Assert.That(whyOver, Does.Contain("视界过大"));
            }
            finally { Object.DestroyImmediate(s); }
        }
    }
}
