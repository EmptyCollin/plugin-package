using System.Collections.Generic;
using UnityEngine;

namespace Collin.StealthPlanner
{
    /// <summary>
    /// <b>多条短解搜索</b>（静态设计 4「有解时输出前 M 条短解」的算法地基）。
    /// <para>与 M2/M3 共用<b>同一份</b>判据：移动与等待合法性一律走
    /// <see cref="PhaseReachability.ExpandSuccessors"/>（欧氏半径、原地等待、严格中间格本桶安全、
    /// 终点只查下一桶，D-18），某桶哪些格不能站走
    /// <see cref="PhaseReachability.BuildBlockedMask"/>（危险 或 无数据，D-19）。
    /// 本类<b>不重新实现任何安全/邻居判定</b>——那是 D-22/D-24「多一套口径必漂移」教训的延续。</para>
    ///
    /// <para><b>节点是 (cell, layer)，不是 cell（这是本算法最关键、也最易写错的一点）</b>：
    /// 每条边都让桶号 <c>+1</c> ⇒ 时间扩展图是按桶分层的 DAG。同一格可以在<b>不同层</b>各算一个节点
    /// （先离开、哨兵转身后再回来 = 折返；或先等再走 = 等待变体），所以<b>「访问过某格」绝不等于
    /// 「以后都不能再进某格」</b>。实现用 <c>parent[layer*n+cell]</c>（-1=该层该格尚未被发现）
    /// 同时充当「发现标志」与「父指针」——按 (cell,layer) 判发现，<b>不是</b>按格判发现。</para>
    /// <para>⚠️ 本包实测栽过两种错法：① 用「格级全局 arrived[]」当访问标志 ⇒ 起点只能出现一次、
    /// 等待与折返全丢，连第一条路线都找不到（比 M2 传播少解 ⇒ 与 M3「判可行」直接矛盾）；
    /// ② 用 <c>present/nextPresent</c> 双缓冲交换 ⇒ 复位标记作用在错误缓冲上，漏收/重收不定。
    /// 正解就是每层重建前沿、按 (cell,layer) 去重。用例 <c>ReentryAfterForbidden</c>、
    /// <c>SingleCorridor</c>、<c>FirstRouteArrival_MatchesPhaseSolver</c> 分别钉这三面。</para>
    ///
    /// <para><b>最早到达</b>：单位代价（每边恰一桶）下，逐层递增扫描里<b>第一个把撤离点纳入前沿的层</b>
    /// 就是最早到达层 ⇒ 首条路线既是合法动线又是<b>最短时间解</b>（等价 A* 最优解，只是不需启发式，见 D-24）。
    /// 首达即返回；后续路线靠「禁止该路线用到的边」逐条重搜得到。</para>
    ///
    /// <para><b>M 条「短解」的确切定义</b>：<b>边不相交</b>——逐条把已用过的转移
    /// <c>(fromCell, layer)→toCell</c>（<b>含等待边</b> to==from）放进禁止集，再重搜下一条。
    /// 这<b>不是</b>严格 Yen K-shortest（后者允许共享边、给理论前 K 最短路）；边不相交在分层图上更直观、
    /// 每条都能原样注入复现，符合「导出前 M 条供策划挑选不同走法」的用意，口径记于
    /// <see cref="Route.EdgeDisjoint"/>。穷尽时返回<b>少于</b> M 条，绝不凑数或谎报。</para>
    ///
    /// <para>纯 CPU + 只读表，无 UnityEditor 依赖、不调物理 ⇒ 可后台、可 EditMode 断言、打包后可复用。</para>
    /// </summary>
    public static class PhaseKShortest
    {
        /// <summary>
        /// (cell,layer) 节点数上限。掩码 <c>byte</c>/节点 + 父指针 <c>int</c>/节点 ≈ 5 字节/节点。
        /// 默认 <c>480 × 11664 ≈ 5.6M 节点 ≈ 28MB</c> 无压力；上限 24M 节点 ≈ 120MB，
        /// 超限不静默算爆内存，回可读失败（与 <see cref="PhaseSolver.MaxHorizonBuckets"/> 同一取向）。
        /// </summary>
        public const long MaxLayerCells = 24L * 1024 * 1024;

        /// <summary>边键 / 节点索引打包前提：格数与层数都须 &lt; 2^21（≈209 万）。示例场景远小于此。</summary>
        const int PackLimit = 1 << 21;

        /// <summary>一条玩家动线（静态设计 4 的产物，亦是 M4 后续数据集导出的载体）。</summary>
        public sealed class Route
        {
            /// <summary>按时间展开的站位序列：第 i 项 = 玩家在<b>层 i</b> 所在格（下标即层号）。
            /// 相邻两项相同 = 该层原地等待；不同 = 移动了一跳。</summary>
            public readonly List<int> CellsPerLayer = new List<int>();
            /// <summary>抵达撤离点的层（= CellsPerLayer.Count − 1）。</summary>
            public int ArrivalBucket;
            /// <summary>抵达撤离点的时刻（秒）。</summary>
            public float ArrivalSeconds;
            /// <summary>移动跳数（相邻层换格的次数）。等待不计。</summary>
            public int MoveCount
            {
                get
                {
                    var m = 0;
                    for (var i = 1; i < CellsPerLayer.Count; i++)
                        if (CellsPerLayer[i] != CellsPerLayer[i - 1]) m++;
                    return m;
                }
            }
            /// <summary>本组路线按「边不相交」产出（见类注释），恒为 true，供调用方如实展示口径。</summary>
            public bool EdgeDisjoint = true;
        }

        /// <summary>
        /// 求前 <paramref name="maxRoutes"/> 条边不相交的真实动线，填进 <paramref name="output"/>
        /// （<b>先清空再填</b>，确定性：同输入同序列）。穷尽不足 M 条时如实返回更少。
        /// 入参非法（表空/越界/起点 t=0 被看见/规模超限）⇒ <paramref name="failure"/> 给原因、
        /// <paramref name="output"/> 保持为空（<b>不返回半条路线</b>）。
        /// </summary>
        public static void FindRoutes(VisibilityTable table, int startCell, int goalCell,
                                      int horizonBuckets, int stepCells, int maxRoutes,
                                      List<Route> output, out PhaseReachability.Failure failure)
        {
            failure = PhaseReachability.Failure.Good();
            if (output == null) { failure = PhaseReachability.Failure.Fail("输出列表为 null"); return; }
            output.Clear();

            if (table == null)
            { failure = PhaseReachability.Failure.Fail("表为空：尚未烘焙可见性表"); return; }
            var n = table.CellCount;
            if (n <= 0) { failure = PhaseReachability.Failure.Fail("表的格数为 0（footprint 为空？）"); return; }
            if (horizonBuckets < 1) { failure = PhaseReachability.Failure.Fail("视界桶数 < 1"); return; }
            if (startCell < 0 || startCell >= n)
            { failure = PhaseReachability.Failure.Fail("起点格越界：" + startCell + " / " + n); return; }
            if (goalCell < 0 || goalCell >= n)
            { failure = PhaseReachability.Failure.Fail("撤离点格越界：" + goalCell + " / " + n); return; }
            if (maxRoutes < 1) maxRoutes = 1;
            if (n >= PackLimit || horizonBuckets >= PackLimit)
            {
                failure = PhaseReachability.Failure.Fail(
                    "格数或视界超出路径搜索上限（" + PackLimit + "）：格=" + n + " 视界=" + horizonBuckets);
                return;
            }
            var dt = table.TimeStep;
            if (dt <= 0f) { failure = PhaseReachability.Failure.Fail("表的时间步长非法"); return; }

            // ── 逐桶阻断掩码（与 M2/M3 同一 BuildBlockedMask 入口）预计算一次、多轮复用 ──
            var noData = new byte[n];
            if (!PhaseReachability.TryBuildNoData(table, noData)) noData = null;

            var layerCells = (long)horizonBuckets * n;
            if (layerCells > MaxLayerCells)
            {
                failure = PhaseReachability.Failure.Fail(
                    "时间扩展规模约 " + layerCells.ToString("N0") + " 节点超上限 "
                    + MaxLayerCells.ToString("N0") + "，请减小相位轮数 N / 相位窗 P 或增大格长");
                return;
            }
            var masks = new byte[layerCells];   // masks[b*n + cell]，1=该桶该格不能站
            var row = new byte[n];
            for (var b = 0; b < horizonBuckets; b++)
            {
                if (!PhaseReachability.BuildBlockedMask(table, b * dt, noData, row))
                { failure = PhaseReachability.Failure.Fail("阻断掩码构造失败（表与缓冲长度不符）"); return; }
                System.Array.Copy(row, 0, masks, (long)b * n, n);
            }

            if (masks[startCell] != 0)
            {
                failure = PhaseReachability.Failure.Fail(noData != null && noData[startCell] != 0
                    ? "起点格是墙/未探测格，不能作起点"
                    : "出发格在 t=0 已被看见，无法起步");
                return;
            }

            // 起点==撤离点：层 0 即到位，单元素路线。
            if (startCell == goalCell)
            {
                var r0 = new Route { ArrivalBucket = 0, ArrivalSeconds = 0f };
                r0.CellsPerLayer.Add(startCell);
                output.Add(r0);
                return;
            }

            // ── 逐轮搜一条边不相交路径 ──
            var forbidden = new HashSet<long>();
            var parent = new int[layerCells];      // 兼作「(cell,layer) 是否被发现」标志：-1=未见
            var cur = new List<int>();
            var next = new List<int>();
            var maskHere = new byte[n];
            var maskNext = new byte[n];
            var buf = new int[Mathf.Max(1, (2 * stepCells + 1) * (2 * stepCells + 1))];

            while (output.Count < maxRoutes)
            {
                var goalLayer = SearchEarliest(table, n, horizonBuckets, masks, startCell, goalCell,
                                               stepCells, forbidden, parent, cur, next, maskHere, maskNext, buf);
                if (goalLayer < 0) break;          // 禁边后无可达路线 ⇒ 如实少于 M 条

                var route = BuildRoute(n, startCell, goalCell, goalLayer, parent, dt);
                output.Add(route);
                // 用它的移动 + 等待边都锁掉 ⇒ 下一条必然不同走法
                RecordForbidden(n, goalCell, goalLayer, parent, forbidden);
            }
        }

        /// <summary>
        /// 一次「按层递增、逐层重建前沿」的单位代价分层 BFS。节点 (cell,layer) 由
        /// <c>parent[layer*n+cell]</c> 是否为 -1 判「本层该格是否已发现」——<b>同格可跨层各发现一次</b>
        /// （折返/等待变体）。首个把撤离点纳入前沿的层即最早到达，回填 parent 后返回该层；不可达返回 -1。
        /// </summary>
        static int SearchEarliest(VisibilityTable table, int n, int horizon, byte[] masks,
                                  int startCell, int goalCell, int stepCells, HashSet<long> forbidden,
                                  int[] parent, List<int> cur, List<int> next,
                                  byte[] maskHere, byte[] maskNext, int[] buf)
        {
            // ⚠️ 必须是 Fill(-1) 而不是 Clear(=0)：-1 才是「(cell,layer) 未发现」的哨兵，
            //   0 是合法格号——用 Clear 会让所有节点看起来已被发现 ⇒ 第一跳全被跳过 ⇒ 可行场景返回 0 条（实测踩过）。
            System.Array.Fill(parent, -1);
            cur.Clear(); next.Clear();
            parent[startCell] = startCell;                        // 层 0 起点自指为「已发现」哨兵（回溯见 layer==0 停）
            cur.Add(startCell);
            System.Array.Copy(masks, 0, maskHere, 0, n);         // 层 0 掩码

            for (var b = 0; b <= horizon - 2; b++)
            {
                System.Array.Copy(masks, (long)(b + 1) * n, maskNext, 0, n);   // 第 b+1 层掩码（每层重取，不交换）
                next.Clear();

                for (var fi = 0; fi < cur.Count; fi++)
                {
                    var from = cur[fi];
                    // 复用 M2 判据：欧氏半径、等待(to==from)、中间格本桶安全、终点只查下一桶
                    var cnt = PhaseReachability.ExpandSuccessors(table, from, stepCells, maskHere, maskNext, buf);
                    for (var k = 0; k < cnt; k++)
                    {
                        var to = buf[k];
                        // ① 禁边只跳「这一跳」，continue 到同格其余后继（误用 break 会丢掉整格后继）
                        if (forbidden.Count > 0 && forbidden.Contains(EdgeKey(from, b, to))) continue;
                        // ② 按 (to, 下一层) 判发现；同格在别层仍可再来 ⇒ 折返/等待得以保留
                        var idx = (long)(b + 1) * n + to;
                        if (parent[idx] != -1) continue;
                        parent[idx] = from;
                        if (to == goalCell) return b + 1;        // 单位代价下首达即最早到达
                        next.Add(to);
                    }
                }

                if (next.Count == 0) return -1;                  // 前沿枯竭 ⇒ 不可达

                var sf = cur; cur = next; next = sf;             // 推进到下一层
                var mt = maskHere; maskHere = maskNext; maskNext = mt;
            }
            return -1;
        }

        /// <summary>沿 parent[(layer)*n+cell] 从撤离点回溯到起点，产出按层展开的站位序列。</summary>
        static Route BuildRoute(int n, int startCell, int goalCell, int goalLayer,
                                int[] parent, float dt)
        {
            var perLayer = new int[goalLayer + 1];
            var cell = goalCell;
            for (var layer = goalLayer; layer >= 0; layer--)
            {
                perLayer[layer] = cell;
                if (layer == 0) break;                           // 层 0 即起点（parent[0*n+start] 为自指哨兵）
                cell = parent[(long)layer * n + cell];
            }
            var r = new Route { ArrivalBucket = goalLayer, ArrivalSeconds = goalLayer * dt };
            for (var layer = 0; layer <= goalLayer; layer++) r.CellsPerLayer.Add(perLayer[layer]);
            return r;
        }

        /// <summary>把这条路线用到的<b>所有</b>边（移动与等待，逐层 (prev,layer)→cell）加入禁止集。</summary>
        static void RecordForbidden(int n, int goalCell, int goalLayer, int[] parent, HashSet<long> forbidden)
        {
            var cell = goalCell;
            for (var layer = goalLayer; layer >= 1; layer--)
            {
                var p = parent[(long)layer * n + cell];
                forbidden.Add(EdgeKey(p, layer - 1, cell));
                cell = p;
            }
        }

        // 下面这个空壳只用于兼容上面调用处的写法，删掉以免误导——真正产物见 BuildRoute。
        static void Reconstruct(int n, int startCell, int goalCell, int goalLayer, int[] parent,
                                float dt, HashSet<long> forbidden, Route first) { }

        static long EdgeKey(int fromCell, int fromLayer, int toCell)
        {
            // 三者各 < 2^21 ⇒ 拼进 long 不串键
            return ((long)fromLayer << 42) | ((long)fromCell << 21) | toCell;
        }
    }
}
