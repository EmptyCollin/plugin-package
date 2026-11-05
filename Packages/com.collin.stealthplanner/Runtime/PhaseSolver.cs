using System.Collections.Generic;
using UnityEngine;

namespace Collin.StealthPlanner
{
    /// <summary>
    /// <b>相位可行性求解器</b>（静态设计 2「有无可行解」+ 静态设计 3「无解给最近撤离点 Top-K」）。
    /// <para><b>本类不写第二套可达判据</b>：可行性与每格最早到达时间一律复用
    /// <see cref="PhaseReachability"/>（其 <see cref="PhaseReachability.ExpandSuccessors"/> 与
    /// <see cref="PhaseReachability.BuildBlockedMask"/> 是 M2 时间轴与 M3 求解器的<b>同一份</b>
    /// 邻居/安全真源，DESIGN 单一真源纪律）。时间步代价恰为一桶、且「原地等待」恒可选，
    /// 故传播是<b>层序</b>的 ⇒ 第 b 层首次命中目标就是<b>最早到达</b>，A* 在这种
    /// unit-cost + earliest-arrival 图上不改变答案、只改变展开顺序 ⇒ M3 用带目标早退的传播即等价，
    /// <b>不手搓可能出可采纳性 bug 的启发式</b>（k-shortest 多条解与剪枝加速属 M4，见 DESIGN §9）。</para>
    ///
    /// <para><b>保守方向与 M1/M2 一致</b>：越界、墙、未探测格一律判不可走；
    /// 无解不返回半张结果，给可读原因。</para>
    /// <para>纯 CPU + 只读表，不引 UnityEditor、不调物理 ⇒ 可后台、可 EditMode 断言、打包后可复用。</para>
    /// </summary>
    public static class PhaseSolver
    {
        /// <summary>
        /// 视界（时间桶数）硬上限。默认 <c>N=4 × BucketsPerPhase=120 = 480</c>，
        /// 但 N 可到 64、P 可配很大 ⇒ <c>N×⌈P/Δt⌉</c> 无上界会算爆内存/时间。
        /// 超过本上限时不静默截断，而是回可读 failure（见 <see cref="Horizon"/>）。
        /// </summary>
        public const int MaxHorizonBuckets = 4096;

        /// <summary>一次求解的输出。</summary>
        public sealed class Result
        {
            /// <summary>≤ 视界内是否存在到达撤离点的路线。</summary>
            public bool Feasible;
            /// <summary>最早到达的层（=全局桶号）；无解为 -1。</summary>
            public int ArrivalBucket = -1;
            /// <summary>最早到达时刻（秒）；无解为 <see cref="float.PositiveInfinity"/>。</summary>
            public float EarliestArrivalSeconds = float.PositiveInfinity;
            /// <summary>本次实际使用的视界桶数。</summary>
            public int HorizonBuckets;
            /// <summary>视界内玩家能到达的格数（可行性诊断与无解规模判断）。</summary>
            public int CellsReached;

            /// <summary>无解时的最近撤离点候选（静态设计 3），按(距撤离点, 最早到达)升序，最多 K 个。</summary>
            public readonly List<Candidate> FallbackTopK = new List<Candidate>();
        }

        /// <summary>无解 Top-K 里的一个候选藏身点。</summary>
        public struct Candidate
        {
            public int cell;
            public Vector3 world;
            /// <summary>该格绕开墙体到撤离点的<b>最少格步数</b>（越接近撤离点越小）。不可达为 int.MaxValue。</summary>
            public int DistCellsToGoal;
            /// <summary>玩家最早能到达该候选的时刻（秒）。</summary>
            public float EarliestSeconds;
        }

        /// <summary>视界桶数 = N 相位 × 每相位桶数，并检查是否超硬上限。超限时 <paramref name="fail"/> 给原因。</summary>
        public static int Horizon(PlannerBakeSettings s, out string fail)
        {
            fail = null;
            if (s == null) { fail = "缺烘焙设置（相位轮数 N 无从取得）"; return -1; }
            var h = s.MaxPhases * s.BucketsPerPhase;
            if (h > MaxHorizonBuckets)
            {
                fail = "时间视界过大：" + s.MaxPhases + " 相位 × " + s.BucketsPerPhase
                       + " 桶 = " + h + " > 上限 " + MaxHorizonBuckets + "。请减小相位轮数 N 或相位窗 P";
                return -1;
            }
            return h < 1 ? 1 : h;
        }

        /// <summary>
        /// 便利入口：从烘焙设置 + 人速算出视界与步长后求解（面板走这条，参数单一来源见 settings）。
        /// </summary>
        public static Result Solve(VisibilityTable table, PlannerBakeSettings settings,
                                   Vector3 startWorld, Vector3 goalWorld, float walkSpeed,
                                   out PhaseReachability.Failure failure)
        {
            var h = Horizon(settings, out var why);
            if (h < 0)
            {
                failure = PhaseReachability.Failure.Fail(why);
                return new Result();
            }
            var step = table != null
                ? PhaseReachability.StepCellsFor(walkSpeed, table.TimeStep, table.CellSize)
                : 0;
            return Solve(table, WorldCell(table, startWorld), WorldCell(table, goalWorld),
                         h, step, settings.FallbackTopK, out failure);
        }

        static int WorldCell(VisibilityTable table, Vector3 w)
        {
            if (table == null) return -1;
            return table.TryCellIndex(w, out var c) ? c : -1;
        }

        /// <summary>
        /// 核心求解入口（测试直接构造格号走这条，不经世界换算）。
        /// <paramref name="startCell"/>/<paramref name="goalCell"/> 越界或非法回可读 failure。
        /// </summary>
        public static Result Solve(VisibilityTable table, int startCell, int goalCell,
                                   int horizonBuckets, int stepCells, int topK,
                                   out PhaseReachability.Failure failure)
        {
            failure = PhaseReachability.Failure.Good();
            var res = new Result { HorizonBuckets = horizonBuckets };

            if (table == null)
            {
                failure = PhaseReachability.Failure.Fail("表为空：尚未烘焙可见性表");
                return res;
            }
            var n = table.CellCount;
            if (n <= 0)
            {
                failure = PhaseReachability.Failure.Fail("表的格数为 0（footprint 为空？）");
                return res;
            }
            if (horizonBuckets < 1)
            {
                failure = PhaseReachability.Failure.Fail("视界桶数 < 1");
                return res;
            }
            if (startCell < 0 || startCell >= n)
            {
                failure = PhaseReachability.Failure.Fail("起点格越界：" + startCell + " / " + n);
                return res;
            }
            if (goalCell < 0 || goalCell >= n)
            {
                failure = PhaseReachability.Failure.Fail("撤离点格越界：" + goalCell + " / " + n);
                return res;
            }

            // 复用 M2 传播：给出 goalCell ⇒ 命中即早退（有解不必跑满视界）。
            // ⚠️ 语义要点：
            //   有解 → 场只覆盖到命中层，但 feasible 分支只读 arrival / EarliestSeconds[goal]（已记），够用。
            //   无解 → 传播跑满 horizon、场是<b>完整</b>的 ⇒ Top-K 才能基于全视界可达集来选。
            var field = PhaseReachability.Compute(table, startCell, horizonBuckets, stepCells,
                                                  goalCell, out failure, out var arrival);
            if (!failure.ok) return res;   // 起点是墙 / 起点 t=0 已被看见等，Compute 已给原因

            res.ArrivalBucket = arrival;
            res.Feasible = arrival >= 0;
            res.EarliestArrivalSeconds = arrival >= 0 ? arrival * table.TimeStep : float.PositiveInfinity;

            // 已到达格数（可行性诊断；无解时场完整、计数可信；有解时反映到达 goal 前累计到的规模）
            for (var c = 0; c < n; c++) if (field.EverReachable[c] != 0) res.CellsReached++;

            if (!res.Feasible)
                BuildFallback(table, field, goalCell, topK, res);

            return res;
        }

        /// <summary>
        /// 静态设计 3：无解时给出「玩家实际能到的、离撤离点最近的 K 个位置」。
        /// <para>排序键是<b>绕墙的最近格步数</b>（不是直线距离）——被墙隔开的点看着近其实到不了，
        /// 直线距离会给出误导性的「就差一格」。距离场从撤离点做一次<b>只穿可走格</b>的多源 BFS
        /// （D-04 明说启发式，故用均匀代价近似，不做带对角修正的 Dijkstra）。</para>
        /// <para>候选必须<b>两个条件都满足</b>：①玩家在视界内确实到达过（<c>EarliestSeconds</c> 有限）；
        /// ②该格绕墙能连到撤离点（距离场有限）。只满足其一都不是「有意义的下一步藏身点」。</para>
        /// </summary>
        static void BuildFallback(VisibilityTable table, PhaseReachability.Field field,
                                  int goalCell, int topK, Result res)
        {
            var n = table.CellCount;
            var dist = new int[n];
            for (var i = 0; i < n; i++) dist[i] = int.MaxValue;

            // 「无数据格」= 墙（D-19）。距离场只能在非墙上走 ⇒ 与求解同一份 noData 口径。
            var noData = new byte[n];
            var hasNoData = PhaseReachability.TryBuildNoData(table, noData);

            // 撤离点<b>无条件作为距离场源点</b>（即使它自己落在墙上/未探测）：
            // 语义是「还能多接近撤离点」，源点必须存在，否则紧贴撤离点外侧的可达格全拿 int.MaxValue、
            // Top-K 直接空掉。撤离点是墙时玩家本来就到不了那格，但「最近的可藏点」仍然有意义。
            dist[goalCell] = 0;

            // 多源 BFS（8 邻域，均匀代价）
            var q = new Queue<int>();
            q.Enqueue(goalCell);
            var dimX = table.DimX;
            var dimZ = table.DimZ;
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                var cx = cur % dimX;
                var cz = cur / dimX;
                var nd = dist[cur] + 1;
                for (var dz = -1; dz <= 1; dz++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var nx = cx + dx;
                        var nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= dimX || nz >= dimZ) continue;
                        var to = nz * dimX + nx;
                        if (hasNoData && noData[to] != 0) continue;      // 墙：不穿
                        if (dist[to] <= nd) continue;
                        dist[to] = nd;
                        q.Enqueue(to);
                    }
                }
            }

            // 收集候选：玩家到过 且 能绕墙连到撤离点（撤离点自身不算，那是 goal）
            var list = res.FallbackTopK;
            for (var c = 0; c < n; c++)
            {
                if (c == goalCell) continue;
                if (field.EarliestSeconds[c] >= float.PositiveInfinity) continue;   // 玩家到不了
                if (dist[c] == int.MaxValue) continue;                              // 连不到撤离点
                list.Add(new Candidate
                {
                    cell = c,
                    world = table.CellCenterWorld(c),
                    DistCellsToGoal = dist[c],
                    EarliestSeconds = field.EarliestSeconds[c]
                });
            }

            // 排序：先按绕墙距离（越近越优先），再按最早到达（越早越好）
            list.Sort((a, b) =>
            {
                var byDist = a.DistCellsToGoal.CompareTo(b.DistCellsToGoal);
                return byDist != 0 ? byDist : a.EarliestSeconds.CompareTo(b.EarliestSeconds);
            });

            // 截断到 Top-K。K 从设置传入；非法值（<1）按 1 处理，绝不返回空清单假装「没有更近的了」
            var keep = Mathf.Max(1, topK);
            while (list.Count > keep) list.RemoveAt(list.Count - 1);
        }
    }
}
