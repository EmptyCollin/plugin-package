using System;
using UnityEngine;

namespace Collin.StealthPlanner
{
    /// <summary>
    /// <b>相位可达性传播</b>（静态设计 1 的「玩家可达位置——基于上一个轴节点的安全区域」，
    /// 也是静态设计 2 求解器的地基）。
    /// <para>输入一张已烘的 <see cref="VisibilityTable"/> 与一个出发格，输出「每个全局时间桶上
    /// 玩家可能站立的格集合」。之所以是包内<b>唯一</b>的一份展开逻辑：时间轴热区（M2）与
    /// A* 搜索（M3）都必须吃同一套「一步能走到哪 / 何处算安全」的判定，两处各写一份必然分叉
    /// （本包 DESIGN 反复强调的单一真源纪律）。</para>
    ///
    /// <para><b>三条定死的语义（改动任何一条都会让结果静默失真）</b>：</para>
    /// <list type="number">
    /// <item><b>桶内不插值</b>。表的危险位是「该桶起点时刻」的判定（M1 填桶口径），故传播也只在
    /// 全局桶网格上走。绝不在这里改成按 t 线性插值——那会造出第二套时间口径。</item>
    /// <item><b>一步跨多格 ⇒ 沿途每格都要安全</b>。Δt=0.5s、人速 3.2m/s ⇒ 一步约 3 格；
    /// 只判起终点会让玩家「跳过掩体/穿墙」，正是要给策划看的那个错误。中间格在<b>本桶与下一桶
    /// 都</b>要求安全（它在途中被跨越，两个时刻都可能被盯上）⇒ 只会更严、不会放宽。</item>
    /// <item><b>必须允许原地等待</b>。潜行的核心动作就是「蹲在掩体后等哨兵转身」，
    /// 没有 wait 就不是潜行可达域，而只是「安全窗内一口气冲过去」。</item>
    /// </list>
    ///
    /// <para><b>保守方向与 M1 一致</b>：任何不确定（表未烘、越界、掩码长度不足）一律判<b>不可达</b>。
    /// 谎报可达会让策划做出走不通的关卡；少报只是让他多试一次。</para>
    /// <para>纯 CPU + 只读表，不引用 UnityEditor、不调物理 ⇒ 可在 EditMode 断言、打包后亦可复用。</para>
    /// </summary>
    public static class PhaseReachability
    {
        /// <summary>一次传播的结果（每桶一个格位图，1=该桶可达）。</summary>
        public sealed class Field
        {
            /// <summary>桶数（= 全局时间桶数，与表的 per-NPC 分片无关）。</summary>
            public int Buckets;
            /// <summary>每格是否被任一桶覆盖过（供「累计可达并集」热区与无解诊断）。</summary>
            public byte[] EverReachable = Array.Empty<byte>();
            /// <summary>逐桶可达位图，扁平存储：<c>perBucket[b*CellCount + cell]</c>。</summary>
            public byte[] PerBucket = Array.Empty<byte>();
            /// <summary>每格的<b>最早到达时刻</b>（秒）；不可达为 <see cref="float.PositiveInfinity"/>。</summary>
            public float[] EarliestSeconds = Array.Empty<float>();
            public int CellCount;
            public float TimeStep;
            /// <summary>一步可跨的最大格数（按速度×Δt/格长算出，供面板展示与调参）。</summary>
            public int StepCells;

            /// <summary>某桶某格是否可达。越界返回 false。</summary>
            public bool IsReachable(int bucket, int cell)
            {
                if (PerBucket == null || cell < 0 || cell >= CellCount) return false;
                if (bucket < 0 || bucket >= Buckets) return false;
                return PerBucket[(long)bucket * CellCount + cell] != 0;
            }

            /// <summary>该桶可达格数（热区统计与性能诊断）。</summary>
            public int ReachableCountInBucket(int bucket)
            {
                if (PerBucket == null || bucket < 0 || bucket >= Buckets) return 0;
                var off = (long)bucket * CellCount;
                var n = 0;
                for (var c = 0; c < CellCount; c++) if (PerBucket[off + c] != 0) n++;
                return n;
            }

            /// <summary>
            /// 填「<b>截至</b> <paramref name="bucket"/> 桶（含）为止已经能到达哪些格」到
            /// <paramref name="mask"/>（先清零再按桶做并集）。
            /// <para>与 <see cref="EverReachable"/> 的区别必须讲清，否则时间轴会看着像坏了：
            /// <see cref="EverReachable"/> 是<b>整次传播（全部桶）</b>的并集 ⇒ 拖 t 时它<b>恒定</b>，
            /// 只适合回答「这关最多能摸到哪」（可达上限／无解诊断）。
            /// 而「基于上一个轴节点的安全区域」要的是<b>截至此刻</b>的推进量 ⇒ 必须走本方法。</para>
            /// </summary>
            /// <returns>掩码长度不足或参数非法时 false（不部分写入）。</returns>
            public bool FillReachableUpTo(int bucket, byte[] mask)
            {
                if (mask == null || mask.Length < CellCount) return false;
                for (var i = 0; i < CellCount; i++) mask[i] = 0;
                if (bucket < 0) return false;                       // 负桶=还没开始，保持全 0
                if (PerBucket == null) return false;
                var upto = bucket < Buckets ? bucket : Buckets - 1;
                for (var b = 0; b <= upto; b++)
                {
                    var off = (long)b * CellCount;
                    for (var c = 0; c < CellCount; c++)
                    {
                        if (mask[c] == 0 && PerBucket[off + c] != 0) mask[c] = 1;
                    }
                }
                return true;
            }
        }

        /// <summary>传播失败时的可读原因（UI 直接显示，绝不返回空结果让人猜）。</summary>
        public struct Failure
        {
            public bool ok;
            public string reason;
            public static Failure Fail(string why) => new Failure { ok = false, reason = why };
            public static Failure Good() => new Failure { ok = true };
        }

        /// <summary>
        /// 算出「一步能跨几格」= <c>floor(速度 × Δt ÷ 格长)</c>。
        /// <para><b>刻意向下取整、且允许返回 0</b>：向上取整或兜成 1 都会<b>虚构</b>出走不到的距离
        /// （0.9m 一步却算成能跨 1m 格）。返回 0 表示「这一步只能原地等」——传播器本来
        /// 就支持原地等待分支（语义 ③），所以 0 是合法且有意义的值，不是退化。</para>
        /// </summary>
        public static int StepCellsFor(float speedMetersPerSecond, float timeStep, float cellSize)
        {
            if (speedMetersPerSecond <= 0f || timeStep <= 0f || cellSize <= 0f) return 0;
            return Mathf.FloorToInt(speedMetersPerSecond * timeStep / cellSize);
        }

        /// <summary>
        /// 从 <paramref name="startCell"/> 出发，沿全局桶 0..<paramref name="buckets"/> 传播可达域。
        /// <para>本桶可达读 <see cref="Field.IsReachable"/>、累计可达读 <see cref="Field.EverReachable"/>，
        /// 因此<b>不再额外传输出缓冲</b>：多传两个掩码只会多出一条口径，而不是少算一遍。</para>
        /// <para>这是<b>跑满视界</b>的版本：时间轴热区与无解 Top-K 要用它——
        /// <see cref="Field.EarliestSeconds"/> 必须覆盖全部桶才选得出候选。</para>
        /// </summary>
        public static Field Compute(VisibilityTable table, int startCell, int buckets,
                                    int stepCells, out Failure failure)
        {
            int arrival;
            return Compute(table, startCell, buckets, stepCells, -1, out failure, out arrival);
        }

        /// <summary>
        /// 同上，但给出 <paramref name="goalCell"/> 后<b>命中即早退</b>。
        /// <para>正确性依据：每步代价恰为一桶，传播是<b>层序</b>推进的，所以第 b 层出现目标
        /// 就是<b>最早到达</b>，更晚的层必然不会更早 ⇒ 可安全停止。</para>
        /// <para><paramref name="goalArrivalBucket"/> 返回命中层（未命中为 -1）。
        /// ⚠️ <b>早退时返回的场只覆盖到命中层</b>，其后各层为 0 ⇒
        /// 不得拿早退的场去做 Top-K／累计热区／时间轴展示。未命中时循环跑满视界，场是完整的。</para>
        /// </summary>
        public static Field Compute(VisibilityTable table, int startCell, int buckets,
                                    int stepCells, int goalCell,
                                    out Failure failure, out int goalArrivalBucket)
        {
            failure = Failure.Good();
            goalArrivalBucket = -1;
            var empty = new Field();

            if (table == null)
            {
                failure = Failure.Fail("表为空：尚未烘焙可见性表");
                return empty;
            }
            var n = table.CellCount;
            if (n <= 0)
            {
                failure = Failure.Fail("表的格数为 0（footprint 为空？）");
                return empty;
            }
            if (buckets < 1)
            {
                failure = Failure.Fail("时间桶数 < 1（相位窗或 Δt 配错）");
                return empty;
            }
            if (startCell < 0 || startCell >= n)
            {
                failure = Failure.Fail("出发格索引越界：" + startCell + " / " + n);
                return empty;
            }

            var dt = table.TimeStep;
            if (dt <= 0f)
            {
                failure = Failure.Fail("表的时间步长非法");
                return empty;
            }

            // 复用的两张「阻断掩码」（危险 或 无数据）：本桶 + 下一桶。
            // 一律走 BuildBlockedMask——它是<b>全工程唯一</b>的「某桶哪些格不能站/不能过」入口，
            // BFS、M3 的 A*、时间轴热区都吃它；各处各写一遍必然分叉（本包单一真源纪律）。
            var maskHere = new byte[n];
            var maskNext = new byte[n];
            var cur = new byte[n];
            var nxt = new byte[n];

            // 「无数据格」（墙内、掩体内、地图外——烘焙时压根没打过射线）掩码。
            // 刻意<b>或进阻断掩码</b>而不是另传一个参数：这样终点判定、中间格判定、原地等待
            // 三条路径<b>全部自动</b>把墙当障碍，不会有「某处忘了查」的漏洞。
            // 反例：只查危险位 ⇒ 墙体掩码为 0 = 「安全」⇒ 玩家穿墙，且 UI 把墙涂成可走（D-19）。
            var noData = new byte[n];
            if (!TryBuildNoData(table, noData)) noData = null;

            // 一步后继的缓冲上限 = (2·stepCells+1)²（欧氏半径只会更少，不会更多）。
            // A* 与 BFS 共用同一个 outBuf 尺寸口径，避免两边各自算出不同的邻居集合。
            var outBuf = new int[Mathf.Max(1, (2 * stepCells + 1) * (2 * stepCells + 1))];

            var f = new Field
            {
                Buckets = buckets,
                CellCount = n,
                TimeStep = dt,
                StepCells = stepCells,
                EverReachable = new byte[n],
                PerBucket = new byte[(long)buckets * n],
                EarliestSeconds = new float[n]
            };
            for (var i = 0; i < n; i++) f.EarliestSeconds[i] = float.PositiveInfinity;

            // 桶 0：出发格此刻必须「不被看见 且 不是墙」。否则整次传播无解——这是最常见的
            // 「起点没挑好」，要给<b>明确原因</b>而不是回一张全 0 的表。
            BuildBlockedMask(table, 0f, noData, maskHere);
            if (maskHere[startCell] != 0)
            {
                failure = Failure.Fail(noData != null && noData[startCell] != 0
                    ? "出发格是墙/未探测格，不能作起点（换个可站立的格；若本表是旧版烘焙，请重烘让足迹生效）"
                    : "出发格在 t=0 已被看见，无法起步（换个起点，或把它挪到掩体后）");
                return empty;
            }
            cur[startCell] = 1;

            for (var b = 0; b < buckets; b++)
            {
                var off = (long)b * n;

                for (var c = 0; c < n; c++)
                {
                    if (cur[c] == 0) continue;
                    f.PerBucket[off + c] = 1;
                    f.EverReachable[c] = 1;
                    var tArrive = b * dt;
                    if (tArrive < f.EarliestSeconds[c]) f.EarliestSeconds[c] = tArrive;
                }

                // 目标命中即早退：每步代价恰为一桶、传播是层序的 ⇒ 本层出现目标就是最早到达，
                // 之后的层只会更晚。起点==目标也算命中（第 0 层，「已经在撤离点上」）。
                if (goalCell >= 0 && cur[goalCell] != 0)
                {
                    goalArrivalBucket = b;
                    return f;
                }

                if (b == buckets - 1) break;         // 最后一桶只记录，不再往外扩

                // 下一桶的阻断掩码（与起点判定同一个入口）
                BuildBlockedMask(table, (b + 1) * dt, noData, maskNext);
                for (var i = 0; i < n; i++) nxt[i] = 0;

                for (var c = 0; c < n; c++)
                {
                    if (cur[c] == 0) continue;
                    var cnt = ExpandSuccessors(table, c, stepCells, maskHere, maskNext, outBuf);
                    for (var k = 0; k < cnt; k++) nxt[outBuf[k]] = 1;
                }

                var swap = cur; cur = nxt; nxt = swap;

                // 推进时间轴：本桶掩码换成下一桶的结果，供下一步的「沿途格」判定复用
                var tmp = maskHere; maskHere = maskNext; maskNext = tmp;
            }
            return f;
        }

        /// <summary>
        /// <b>「某时刻哪些格不能站/不能过」的唯一构造入口</b>：危险（被看见）<b>或</b> 无数据（墙/未探测）。
        /// <para>BFS（<see cref="Compute"/>）、M3 的 A*、时间轴热区三处都调本方法取掩码，
        /// 于是 D-19「墙当障碍」不可能在某一处被漏掉。传 <paramref name="noData"/>=null 表示
        /// 本表无足迹（旧版资产），只按危险判定。</para>
        /// </summary>
        /// <returns>缓冲长度不足时 false（不部分写入）。<paramref name="mask"/> 全程被覆写。</returns>
        public static bool BuildBlockedMask(VisibilityTable table, float t, byte[] noData, byte[] mask)
        {
            if (table == null || mask == null || mask.Length < table.CellCount) return false;
            if (!table.BuildDangerMask(t, mask)) return false;    // 逐格走 IsDangerousAt（同一查询口径）
            if (noData != null)
                for (var i = 0; i < mask.Length; i++)
                    if (noData[i] != 0) mask[i] = 1;               // 或进无数据格
            return true;
        }

        /// <summary>构造「无数据格」掩码（1=该格从未被射线探测过）。表无足迹时返回 false。</summary>
        public static bool TryBuildNoData(VisibilityTable table, byte[] noData)
        {
            return table != null && noData != null
                   && table.HasFootprint && table.BuildNoDataMask(noData);
        }

        /// <summary>
        /// 从格 <paramref name="from"/> 展开一步，把<b>合法后继格</b>（含「原地等待」→ 自身）
        /// 去重后写入 <paramref name="out"/>，返回个数。<b>M2 的 BFS 与 M3 的 A* 共用本方法</b>
        /// ——邻居与安全性判定只此一份，两处各写必分叉（本包单一真源纪律）。
        /// <para>三条定死的语义（DESIGN D-18）：① 原地等待只需下一桶安全；
        /// ② 终点格只查下一桶（否则「掩体后刚解除危险那格」永远踏不进、等待失效）；
        /// ③ 严格中间格须在本桶安全（否则玩家穿掩体「跳」过去）。欧氏半径，非切比雪夫方盒。</para>
        /// </summary>
        public static int ExpandSuccessors(VisibilityTable table, int from, int stepCells,
                                           byte[] maskHere, byte[] maskNext, int[] outBuf)
            => ExpandSuccessors(table, from, stepCells, maskHere, 0, maskNext, 0, outBuf);

        /// <summary>
        /// 带<b>层偏移</b>的同判据版本：<c>maskHere/maskNext</c> 允许是「逐桶掩码拼成的大数组」，
        /// <paramref name="hereOffset"/>/<paramref name="nextOffset"/> 指向相应层的首格。
        /// 供 <see cref="PhaseKShortest"/> 的引导搜索<b>乱序</b>弹节点时直接查缓存大数组——
        /// 若每次弹节点都整层拷贝掩码（11664 格/层），拷贝成本会把启发式省的展开全吃回去。
        /// 判据逻辑与上面完全同一份实现（本方法就是唯一实现，旧签名只是 hereOffset=0 的别名）。
        /// </summary>
        public static int ExpandSuccessors(VisibilityTable table, int from, int stepCells,
                                           byte[] maskHere, int hereOffset,
                                           byte[] maskNext, int nextOffset, int[] outBuf)
        {
            if (table == null || outBuf == null) return 0;
            var n = table.CellCount;
            var fx = from % table.DimX;
            var fz = from / table.DimX;
            var count = 0;

            for (var dz = -stepCells; dz <= stepCells; dz++)
            {
                for (var dx = -stepCells; dx <= stepCells; dx++)
                {
                    var tz = fz + dz;
                    var tx = fx + dx;
                    if (tx < 0 || tz < 0 || tx >= table.DimX || tz >= table.DimZ) continue;
                    var to = tz * table.DimX + tx;
                    if (to < 0 || to >= n) continue;
                    if (count > 0 && Contains(outBuf, count, to)) continue;   // 去重：同一桶只收一次

                    // 欧氏半径：切比雪夫会把斜角那一步算得比直着走更快（对角多走 41% 的路
                    // 却花同样的时间），可达域会虚胖成方框。
                    if (dx * dx + dz * dz > stepCells * stepCells) continue;

                    // ① 原地等待：潜行的命脉。只需下一桶仍安全（to==from）。
                    if (dx == 0 && dz == 0)
                    {
                        if (maskNext[nextOffset + to] == 0) outBuf[count++] = to;
                        continue;
                    }

                    // ② 终点格只查<b>下一桶</b>（到达时刻 = 下一桶起点）。刻意<b>不查本桶</b>：
                    //    潜行的核心动作是「躲在掩体后等哨兵转身」——若要求终点在本桶也安全，
                    //    则「障碍刚解除的那一格」永远踏不进去，等待就失去意义。
                    if (maskNext[nextOffset + to] != 0) continue;

                    // ③ <b>严格中间格</b>须在<b>本桶</b>安全：一步耗时恰为一个 Δt，途中经过的格子
                    //    是在本桶的时间区间内被跨越的。漏掉这步＝玩家能穿掩体「跳」过去。
                    //    起点格不必再查（上一层已保证它在安全桶内），终点格按 ② 的口径处理。
                    if (!IntermediateCellsClear(fx, fz, tx, tz, table.DimX, maskHere, hereOffset)) continue;

                    outBuf[count++] = to;
                    if (count >= outBuf.Length) return count;      // 缓冲写满（尺寸已按上限留足）
                }
            }
            return count;
        }

        static bool Contains(int[] buf, int count, int v)
        {
            for (var i = 0; i < count; i++) if (buf[i] == v) return true;
            return false;
        }

        /// <summary>
        /// 线段栅格化（整数超行算法）：<b>只检查严格中间格</b>是否在 <paramref name="maskHere"/> 下安全。
        /// <para>口径推导：一步耗时恰为一个 Δt（桶 b 起点 → 桶 b+1 起点）。</para>
        /// <list type="bullet">
        /// <item><b>起点格</b>不查——上一层 <c>cur</c> 已保证它在桶 b 安全；</item>
        /// <item><b>终点格</b>不查本桶——它是在桶 b+1 <b>起点</b>才被到达，途中从未停留，
        ///       由 <see cref="ExpandSuccessors"/> 的 ② 只校验 <c>maskNext</c>；
        ///       若这里也要求本桶安全，「掩体后方刚解除危险的那一格」就永远踏不进去，
        ///       <b>等待</b>这一潜行动作会彻底失效（实测表现为最远格时刻恒为无穷）；</item>
        /// <item><b>中间格</b>必须本桶安全——它们是在桶 b 的时间区间内被跨越的。</item>
        /// </list>
        /// </summary>
        static bool IntermediateCellsClear(int x0, int z0, int x1, int z1, int dimX,
                                           byte[] maskHere, int hereOffset)
        {
            var dx = Mathf.Abs(x1 - x0);
            var dz = -Mathf.Abs(z1 - z0);
            var sx = x0 < x1 ? 1 : -1;
            var sz = z0 < z1 ? 1 : -1;
            var err = dx + dz;

            var atEnd = false;
            while (!atEnd)
            {
                var e2 = 2 * err;
                var stepX = e2 >= dz;
                var stepZ = e2 <= dx;
                // 对角步的次序会引入栅格化误差：两条对角线在中间格上的取法不同。
                // 这里取「保守」：先 x 后 z，与 ExpandSuccessors 的欧氏半径判定一致，
                // 且同一条路径在整个传播里都用同一次序（否则不同方向不对称）。
                if (stepX) { err += dz; x0 += sx; }
                if (stepZ) { err += dx; z0 += sz; }
                atEnd = x0 == x1 && z0 == z1;
                if (atEnd) break;                       // 终点格由 ② 负责，此处不查

                var idx = hereOffset + z0 * dimX + x0;
                if (maskHere[idx] != 0) return false;   // 中间格此刻被看见 ⇒ 这一步走不通
            }
            return true;
        }
    }
}
