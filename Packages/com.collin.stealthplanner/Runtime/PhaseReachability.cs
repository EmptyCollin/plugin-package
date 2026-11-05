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
        /// </summary>
        public static Field Compute(VisibilityTable table, int startCell, int buckets,
                                    int stepCells, out Failure failure)
        {
            failure = Failure.Good();
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

            // 复用的两张危险掩码：本桶 + 下一桶。走 table.BuildDangerMask ⇒ 与查询同一口径。
            var maskHere = new byte[n];
            var maskNext = new byte[n];
            var cur = new byte[n];
            var nxt = new byte[n];

            // 「无数据格」（墙内、掩体内、地图外——烘焙时压根没打过射线）掩码。
            // 刻意<b>或进危险掩码</b>而不是另传一个参数：这样终点判定、中间格判定、原地等待
            // 三条路径<b>全部自动</b>把墙当障碍，不会有「某处忘了查」的漏洞。
            // 反例：只查危险位 ⇒ 墙体掩码为 0 = 「安全」⇒ 玩家穿墙，且 UI 把墙涂成可走。
            var noData = new byte[n];
            var hasNoData = table.HasFootprint && table.BuildNoDataMask(noData);

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

            // 桶 0：出发格此刻必须安全。否则整次传播无解——这是最常见的「起点没挑好」，
            // 要给明确原因而不是回一张全 0 的表。
            table.BuildDangerMask(0f, maskHere);
            if (hasNoData) MergeNoData(maskHere, noData, n);
            if (maskHere[startCell] != 0)
            {
                failure = Failure.Fail("出发格在 t=0 已被看见，无法起步（换个起点，或把它挪到掩体后）");
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

                if (b == buckets - 1) break;         // 最后一桶只记录，不再往外扩

                // 下一桶的危险掩码
                table.BuildDangerMask((b + 1) * dt, maskNext);
                if (hasNoData) MergeNoData(maskNext, noData, n);
                for (var i = 0; i < n; i++) nxt[i] = 0;

                for (var c = 0; c < n; c++)
                {
                    if (cur[c] == 0) continue;
                    ExpandFrom(table, c, stepCells, maskHere, maskNext, n, nxt);
                }

                var swap = cur; cur = nxt; nxt = swap;

                // 推进时间轴：本桶掩码换成下一桶的结果，供下一步的「沿途格」判定复用
                var tmp = maskHere; maskHere = maskNext; maskNext = tmp;
            }
            return f;
        }

        /// <summary>
        /// 从格 <paramref name="from"/> 展开一步：原地等待 + 半径 <paramref name="stepCells"/> 内的直线移动。
        /// 单独抽出来给 M3 的 A* 复用（同一套邻居与安全性判定）。
        /// </summary>
        static void ExpandFrom(VisibilityTable table, int from, int stepCells,
                               byte[] maskHere, byte[] maskNext, int n, byte[] nxt)
        {
            var fx = from % table.DimX;
            var fz = from / table.DimX;

            for (var dz = -stepCells; dz <= stepCells; dz++)
            {
                for (var dx = -stepCells; dx <= stepCells; dx++)
                {
                    var tz = fz + dz;
                    var tx = fx + dx;
                    if (tx < 0 || tz < 0 || tx >= table.DimX || tz >= table.DimZ) continue;
                    var to = tz * table.DimX + tx;
                    if (to < 0 || to >= n) continue;
                    if (nxt[to] != 0) continue;                       // 已被别的路径覆盖，省一次判定

                    // 欧氏半径：切比雪夫会把斜角那一步算得比直着走更快（对角多走 41% 的路
                    // 却花同样的时间），可达域会虚胖成方框。
                    if (dx * dx + dz * dz > stepCells * stepCells) continue;

                    // ① 原地等待：潜行的命脉。只需下一桶仍安全。
                    if (dx == 0 && dz == 0)
                    {
                        if (maskNext[to] == 0) nxt[to] = 1;
                        continue;
                    }

                    // ② 终点格只查<b>下一桶</b>（到达时刻 = 下一桶起点）。
                    //    刻意<b>不查本桶</b>：潜行的核心动作是「躲在掩体后等哨兵转身」——
                    //    若要求终点在本桶也安全，则「障碍刚解除的那一格」永远踏不进去，
                    //    等待就失去意义（实测这条会把 EarliestSeconds 全推成 ∞）。
                    if (maskNext[to] != 0) continue;

                    // ③ <b>严格中间格</b>须在<b>本桶</b>安全：一步耗时恰为一个 Δt，途中经过的格子
                    //    是在本桶的时间区间内被跨越的。漏掉这步＝玩家能穿掩体「跳」过去。
                    //    起点格不必再查（上一层 cur 已保证它在安全桶内），终点格按 ② 的口径处理。
                    if (!IntermediateCellsClear(fx, fz, tx, tz, table.DimX, maskHere)) continue;

                    nxt[to] = 1;
                }
            }
        }

        /// <summary>把「无数据格」或进危险掩码：未探测过的格一律当障碍（不可通行、不可等待）。</summary>
        static void MergeNoData(byte[] mask, byte[] noData, int n)
        {
            for (var i = 0; i < n; i++)
            {
                if (noData[i] != 0) mask[i] = 1;
            }
        }

        /// <summary>
        /// 线段栅格化（整数超行算法）：<b>只检查严格中间格</b>是否在 <paramref name="maskHere"/> 下安全。
        /// <para>口径推导：一步耗时恰为一个 Δt（桶 b 起点 → 桶 b+1 起点）。</para>
        /// <list type="bullet">
        /// <item><b>起点格</b>不查——上一层 <c>cur</c> 已保证它在桶 b 安全；</item>
        /// <item><b>终点格</b>不查本桶——它是在桶 b+1 <b>起点</b>才被到达，途中从未停留，
        ///       由 <see cref="ExpandFrom"/> 的 ② 只校验 <c>maskNext</c>；
        ///       若这里也要求本桶安全，「掩体后方刚解除危险的那一格」就永远踏不进去，
        ///       <b>等待</b>这一潜行动作会彻底失效（实测表现为最远格时刻恒为无穷）；</item>
        /// <item><b>中间格</b>必须本桶安全——它们是在桶 b 的时间区间内被跨越的。</item>
        /// </list>
        /// </summary>
        static bool IntermediateCellsClear(int x0, int z0, int x1, int z1, int dimX, byte[] maskHere)
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
                // 这里取「保守」：先 x 后 z，与 ExpandFrom 的欧氏半径判定一致，
                // 且同一条路径在整个传播里都用同一次序（否则不同方向不对称）。
                if (stepX) { err += dz; x0 += sx; }
                if (stepZ) { err += dx; z0 += sz; }
                atEnd = x0 == x1 && z0 == z1;
                if (atEnd) break;                       // 终点格由 ② 负责，此处不查

                var idx = z0 * dimX + x0;
                if (maskHere[idx] != 0) return false;   // 中间格此刻被看见 ⇒ 这一步走不通
            }
            return true;
        }
    }
}
