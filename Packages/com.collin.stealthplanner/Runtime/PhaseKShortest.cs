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
    /// ② 改 parent 哨兵后用 <c>Array.Clear</c> 初始化 ⇒「未发现 = -1」被填成 0（0 是合法格号）⇒ 又判 0 条。
    /// 正解是<b>按 (cell,layer) 判发现</b> + <c>Fill(-1)</c>。用例 <c>ReentryAfterForbidden</c>、
    /// <c>SingleCorridor</c>、<c>FirstRouteArrival_MatchesPhaseSolver</c> 分别钉这三面。</para>
    ///
    /// <para><b>最早到达</b>：单位代价（每边恰一桶）下，同一节点的所有来路代价恒等（都等于它的层号），
    /// 故「最早到达层」唯一确定。§6.2 剪枝②落地后，本类用<b>可采纳启发式引导的搜索</b>（见
    /// <see cref="SearchEarliest"/>）替代原来的「逐层灌满全图」：<b>答案不变，只减少展开</b>；
    /// 首条路线仍是合法动线 + 最短时间解。后续路线靠「禁止该路线用到的边」逐条重搜得到。</para>
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

        /// <summary>
        /// <b>诊断计数（不参与任何判定）</b>：最近一次 <see cref="FindRoutes"/> 期间从开放表<b>出堆的
        /// 节点数</b>（= 实际展开次数），入口清零、跨 M 轮累加。
        /// <para>为什么要有它（D-23 同类要求：效果必须有可观测出口）：耗时是<b>结果</b>指标、机器忙闲
        /// 会污染；展开数才是<b>机制</b>指标——只有它能证明启发式削掉的是"无用洪水"而非"恰好空转"。
        /// 没有这个数，下一次又会退回凭结构直觉排性能优先级（M4 性能增量刚栽过）。</para>
        /// <para>⚠️ 静态可变状态，仅供同主线程的取证/面板读数；<b>任何求解逻辑都不得读它</b>——
        /// 求解结果仍是纯函数、同输入同输出。多线程下计数无意义但不影响正确性。</para>
        /// </summary>
        public static long LastExpansionCount;

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

            // ── 逐桶阻断掩码：按需构建 + 缓存（与 M2/M3 同一 BuildBlockedMask 入口）──
            // ⚠️ 性能实测（2026-11-05 探针 perf_probe）：horizon=480 × 11664 格的<b>全量</b>预计算
            //    要 562ms，而首条路线最早层通常远小于 horizon（示例场景 L*=55）——
            //    预计算是在为根本走不到的层白付 88% 的成本。改为「用到第 b 层才建第 b 层」。
            //    掩码构建失败只可能源于长度不匹配（对同一张表逐层恒同），故只在第 0 层验一次。
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
            var masks = new byte[layerCells];       // masks[b*n + cell]，1=该桶该格不能站
            var built = new byte[horizonBuckets];   // 该层是否已构建（0 会被误当「构建好的全安全层」，必须另记）
            var row = new byte[n];
            if (!EnsureLayer(table, masks, built, row, 0, dt, n, noData))
            { failure = PhaseReachability.Failure.Fail("阻断掩码构造失败（表与缓冲长度不符）"); return; }

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
            var heap = new List<long>();           // 引导搜索的最小堆：键=(f,层,格)，出堆序即展开序
            var buf = new int[Mathf.Max(1, (2 * stepCells + 1) * (2 * stepCells + 1))];
            LastExpansionCount = 0;                 // 诊断计数：本次调用的展开数从 0 累加（见字段注释）

            while (output.Count < maxRoutes)
            {
                var goalLayer = SearchEarliest(table, n, horizonBuckets, masks, built, row, noData, dt,
                                               startCell, goalCell, stepCells, forbidden, parent, heap, buf);
                if (goalLayer < 0) break;          // 禁边后无可达路线 ⇒ 如实少于 M 条

                var route = BuildRoute(n, startCell, goalCell, goalLayer, parent, dt);
                output.Add(route);
                // 用它的移动 + 等待边都锁掉 ⇒ 下一条必然不同走法
                RecordForbidden(n, goalCell, goalLayer, parent, forbidden);
            }
        }

        /// <summary>
        /// 确保第 <paramref name="b"/> 层掩码已构建（按需、幂等）。已建则直接返回；未建才做一次
        /// <see cref="PhaseReachability.BuildBlockedMask"/> 写进缓存。
        /// <para>⚠️ 必须<b>另用 built 标记</b>判断「是否已建」：全安全层的内容本身也是全 0，
        /// 若靠「缓存非零才算建过」会把已建好的全安全层误判成未建 ⇒ 每层反复重算，白丢这项优化。</para>
        /// </summary>
        static bool EnsureLayer(VisibilityTable table, byte[] masks, byte[] built, byte[] row,
                                int b, float dt, int n, byte[] noData)
        {
            if (built[b] != 0) return true;
            if (!PhaseReachability.BuildBlockedMask(table, b * dt, noData, row)) return false;
            System.Array.Copy(row, 0, masks, (long)b * n, n);
            built[b] = 1;
            return true;
        }

        /// <summary>
        /// 一次「可采纳启发式引导」的最早到达搜索（§6.2 剪枝②）。返回撤离点的最早层，不可达回 -1。
        /// <para><b>与分层 BFS 的关系（D-24，关键）</b：节点是 (cell,layer)，每条边恰让层号 +1 ⇒
        /// 同一节点的所有来路代价恒等（都等于 layer）。故本函数<b>不改变答案，只改变展开顺序</b>：
        /// BFS 是「把整层灌满再进下一层」，引导式是「优先展开离撤离点近的节点」，
        /// 实测省下的是那 81% 无用的洪水（示例场景只为目标桶 55 附近那条带，却把 108×108×56 全铺开）。
        /// 答案一致性靠两条：<b>①启发可采纳</b>（见 <see cref="GoalHeuristic"/>，永不高估剩余桶数）；
        /// <b>②判据仍是同一份 <see cref="PhaseReachability.ExpandSuccessors"/>／<see cref="EnsureLayer"/>
        /// 缓存的 <c>BuildBlockedMask</c></b>——引导只动"先展开谁"，绝不动"哪些转移合法"。</para>
        /// <para>⚠️ <b>目标必须"首次出队"才返回，不能"首次发现"就返回</b>（与旧 BFS 版本的语义差别，
        /// 也是这次改造唯一真正的风险点）：BFS 按层推进 ⇒ 首次发现即最早；引导式会乱序发现，
        /// 若沿用"发现即返回"，可能返回一条更晚的路线，与 M3 可行性判据自相矛盾。
        /// 出队即最早的标准反证：设最优到达层 L*，沿该最优路径必有节点在开放表中且
        /// f = layer + h ≤ L*；若出队的目标层 L &gt; L*，则该节点 f 更小会先出队 ⇒ 矛盾。</para>
        /// <para>⚠️ <b>发现标志仍按 (cell,layer)</b>，绝不可退回格级：<c>ReentryAfterForbidden</c>
        /// 钉的就是禁边后第二条路线要<b>折返</b>重走已在早期层到过的格。
        /// 也必须是 <c>Fill(-1)</c> 而非 <c>Clear(=0)</c>（0 是合法格号，实测踩过）。</para>
        /// <para><b>确定性</b>：堆键为 <c>(f, layer, cell)</c> 打包的 long ⇒ 同分先浅层、同层先小格号，
        /// 与插入顺序无关，满足本包「同输入同输出」。</para>
        /// </summary>
        static int SearchEarliest(VisibilityTable table, int n, int horizon, byte[] masks,
                                  byte[] built, byte[] row, byte[] noData, float dt,
                                  int startCell, int goalCell, int stepCells, HashSet<long> forbidden,
                                  int[] parent, List<long> heap, int[] buf)
        {
            var dimX = table.DimX;
            var lastGoalLayer = horizon - 1;                 // BFS 版最多返回 horizon-1，此处保持一致

            System.Array.Fill(parent, -1);
            heap.Clear();

            var h0 = GoalHeuristic(startCell, goalCell, stepCells, dimX);
            // 起点的启发下界已超视界 ⇒ 视界内绝无可能到达，直接判无解（保守方向正确：宁可少给路线）
            if (h0 > lastGoalLayer) return -1;
            parent[startCell] = startCell;                   // 层 0 起点自指为「已发现」哨兵（回溯见 layer==0 停）
            HeapPush(heap, PackKey(h0, 0, startCell));

            while (heap.Count > 0)
            {
                var key = HeapPop(heap);
                LastExpansionCount++;                     // 诊断：出堆即展开一次（见字段注释，不参与判定）
                var b = (int)((key >> 21) & PackMask);
                var cell = (int)(key & PackMask);

                // 首次出队目标 ⇒ 最早到达层（见上注释②的可采纳性反证）。起点==目标已在入口单独处理。
                if (cell == goalCell) return b;

                // 按需建第 b+1 层（失败只源于长度不匹配，且第 0 层已在入口验过 ⇒ 实际不会失败）
                if (!EnsureLayer(table, masks, built, row, b + 1, dt, n, noData)) return -1;

                // 复用 M2 判据：欧氏半径、等待(to==from)、中间格本桶安全、终点只查下一桶。
                // 掩码直接给「逐层拼成的大数组 + 层偏移」，省掉每次弹节点整层 11664 格的拷贝——
                // 引导式会乱序弹节点，若仍按 BFS 那样维护「当前层副本」，拷贝会把省下的展开全吃回去。
                var hereOff = b * n;
                var nextOff = (b + 1) * n;                   // ⚠️ int 安全：MaxLayerCells(24M) 远小于 int.MaxValue
                var cnt = PhaseReachability.ExpandSuccessors(table, cell, stepCells,
                                                             masks, hereOff, masks, nextOff, buf);
                for (var k = 0; k < cnt; k++)
                {
                    var to = buf[k];
                    // ① 禁边只跳「这一跳」，continue 到同格其余后继（误用 break 会丢掉整格后继）
                    if (forbidden.Count > 0 && forbidden.Contains(EdgeKey(cell, b, to))) continue;
                    // ② 按 (to, 下一层) 判发现；同格在别层仍可再来 ⇒ 折返/等待得以保留
                    var idx = (long)nextOff + to;
                    if (parent[idx] != -1) continue;
                    // ③ 视界剪枝：该后继的启发下界已让它不可能在视界内抵达 ⇒ 不入队（这是省展开的另一半）
                    var h = GoalHeuristic(to, goalCell, stepCells, dimX);
                    if (b + 1 + h > lastGoalLayer) continue;
                    parent[idx] = cell;
                    HeapPush(heap, PackKey(b + 1 + h, b + 1, to));
                }
            }
            return -1;                                       // 开放表枯竭 ⇒ 视界内无可达路线
        }

        /// <summary>
        /// 到撤离点的<b>可采纳启发</b>（剩余桶数的下界）：<c>ceil(max(|dx|,|dz|) / stepCells)</c>。
        /// <para>为什么恒 ≤ 真实剩余桶数：一桶只能移动一步，而一步在<b>单个坐标轴</b>上最多前进
        /// <paramref name="stepCells"/> 格（欧氏半径只约束"两者同时"的上限，单轴各取 stepCells 已是
        /// 乐观值）⇒ 用切比雪夫距离算下界而非欧氏距离，宁可低估也不高估。
        /// 墙、危险窗、途中格一律<b>不计入</b>——计入就可能高估，破坏可采纳性 ⇒ 返回非最早解。</para>
        /// <para>刻意<b>不用</b>绕墙距离场（M3 无解 Top-K 那个多源 BFS）：它只穿非墙格，因此
        /// <b>不</b>考虑危险窗 ⇒ 看似更准，但"能绕开墙"与"某时刻不能站"是两套口径，一旦距离场因
        /// 某层被封锁而绕远，就可能高估剩余时间，启发式立刻变得不可采纳、答案随之漂移。
        /// 用便宜的格距下界换来的是「结构上不可能骗人」，正是 D-24 当初不上真 A* 的理由。</para>
        /// </summary>
        static int GoalHeuristic(int cell, int goalCell, int stepCells, int dimX)
        {
            var dx = Mathf.Abs(cell % dimX - goalCell % dimX);
            var dz = Mathf.Abs(cell / dimX - goalCell / dimX);
            var s = stepCells > 0 ? stepCells : 1;           // 步长 0（人速为 0）时只能等待，下界退化为格距
            var m = dx > dz ? dx : dz;
            return (m + s - 1) / s;                          // 向上取整：宁可多算一格也绝不低估成 0
        }

        /// <summary>堆键打包 <c>(f, layer, cell)</c>；三者各 &lt; 2^21 ⇒ long 内不串键，字典序即比较序。</summary>
        const long PackMask = (1L << 21) - 1;

        static long PackKey(int f, int layer, int cell)
            => ((long)f << 42) | ((long)layer << 21) | (uint)cell;

        /// <summary>入堆（上浮）。键值序即 (f,layer,cell) 字典序 ⇒ 同分先浅层、同层先小格号，与插入顺序无关。</summary>
        static void HeapPush(List<long> heap, long key)
        {
            heap.Add(key);
            var i = heap.Count - 1;
            while (i > 0)
            {
                var p = (i - 1) / 2;
                if (heap[p] <= heap[i]) break;
                var t = heap[p]; heap[p] = heap[i]; heap[i] = t;
                i = p;
            }
        }

        static long HeapPop(List<long> heap)
        {
            var top = heap[0];
            var last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);
            var i = 0;
            while (true)
            {
                var l = 2 * i + 1;
                if (l >= heap.Count) break;
                var r = l + 1;
                var small = (r < heap.Count && heap[r] < heap[l]) ? r : l;
                if (heap[i] <= heap[small]) break;
                var t = heap[i]; heap[i] = heap[small]; heap[small] = t;
                i = small;
            }
            return top;
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

        static long EdgeKey(int fromCell, int fromLayer, int toCell)
        {
            // 三者各 < 2^21 ⇒ 拼进 long 不串键。
            // 末位必须 (uint)：裸 int 参与 long 的 | 会先做**符号扩展** ⇒ CS0675 warning，
            // 直接破坏 §1.6b 归零门禁（本包此处曾长期挂着这条告警，属自造红灯，不走 §1.8 豁免）。
            // 语义不变：toCell 恒 < 2^21 ⇒ 非负 ⇒ 零扩展与符号扩展结果相同，只是编译器无法自行证明。
            // 同族写法见 PackKey（同样末位转 uint），两处口径一致。
            return ((long)fromLayer << 42) | ((long)fromCell << 21) | (uint)toCell;
        }
    }
}
