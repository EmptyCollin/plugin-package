using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 水体种子（Modules/02 改版，2026-11-05 用户裁定的「水位洪泛」定义）。
    /// <para>只存<b>意图</b>（点了哪里、是否启用、可选的局部水位），水面本身是派生量 ——
    /// 于是"水与地形不一致"（悬在坡上的半截河）在数学上不可能出现：水只能待在低于水位的地方。</para>
    /// <para>不存每格水属性：400 万格掩码入库会造成三个问题 —— 改水位后旧掩码与新地形对不上、
    /// 资产体积爆炸、"移除水"要跟着一堆格子走。种子列表才是用户意图的最小表达（同 D-02-1 的取舍）。</para>
    /// </summary>
    [Serializable]
    public struct WaterSeed
    {
        /// <summary>稳定 id（撤销、面板定位、adopt 都靠它，同 delta 层纪律）。</summary>
        public string stableId;

        /// <summary>种子格坐标（逻辑格号，<b>不是世界米</b> —— 换 worldSize/格档位时米坐标会漂）。</summary>
        public int cx;
        public int cz;

        /// <summary>
        /// 局部水位（米）。<see cref="NoLocalLevel"/> = 不覆盖，用全局海平面。
        /// <para>存在理由（用户裁定的「陆上水体」）：只有一个全局水位时，内陆高原上永远不会有湖
        /// —— 除非把盆底挖到低于全球海平面，那不合理。种子自带水位即可在山头做天池/水库。</para>
        /// </summary>
        public float localLevel;

        /// <summary>启用位：关掉≠删除（保留坐标便于反复试验，面板可勾除）。</summary>
        public bool enabled;

        /// <summary>
        /// 哨兵值：不设局部水位。取 +∞ 而<b>不是 0</b> —— 0 是合法水位（海平面默认值就是 0），
        /// 拿 0 当哨兵会让"把局部水位设成 0"变成不可能（H-31 同族教训）。
        /// </summary>
        public const float NoLocalLevel = float.PositiveInfinity;

        /// <summary>本种子的有效水位（局部优先，否则用全局海平面）。</summary>
        public float ResolveLevel(float globalSeaLevel)
            => float.IsPositiveInfinity(localLevel) ? globalSeaLevel : localLevel;
    }

    /// <summary>
    /// 水体意图层（SO，持久化，入库）。
    /// <para>与 <see cref="SculptDeltaAsset"/>／<see cref="SculptFilterAsset"/> 一样"一物一资产"：
    /// 三者语义不同（手绘地形 / 自动滤镜 / 水意图），混存会让撤销与版本迁移互相污染。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/CityMap/Water Network", fileName = "WaterNetwork")]
    public sealed class WaterNetworkAsset : ScriptableObject
    {
        [SerializeField] int schemaVersion = 1;
        [SerializeField] readonly System.Collections.Generic.List<WaterSeed> seeds
            = new System.Collections.Generic.List<WaterSeed>(8);

        public int SchemaVersion => schemaVersion;
        public int Count => seeds.Count;
        public bool IsEmpty => seeds.Count == 0;

        /// <summary>只读枚举（派生与面板都经此，禁止外部改列表）。</summary>
        public System.Collections.Generic.IReadOnlyList<WaterSeed> Seeds => seeds;

        public WaterSeed this[int i] => seeds[i];

        /// <summary>按 id 取种子。返回 false = 不存在（撤销或他人删除后 id 会消失，调用方不得假设存在）。</summary>
        public bool TryGet(string id, out WaterSeed seed)
        {
            seed = default;
            if (string.IsNullOrEmpty(id)) return false;
            for (var i = 0; i < seeds.Count; i++)
                if (seeds[i].stableId == id) { seed = seeds[i]; return true; }
            return false;
        }

        /// <summary>
        /// 添加种子。同格已存在则<b>就地更新而非追加</b>并返回 false ——
        /// 同格两个种子只让洪泛结果重复，却会让用户在列表里看到两条"看不出差别"的条目。
        /// </summary>
        public bool AddSeed(int cx, int cz, float localLevel, out string id)
        {
            for (var i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].cx != cx || seeds[i].cz != cz) continue;
                var hit = seeds[i];                       // List<struct> 索引器返回值，须读-改-写回
                hit.localLevel = localLevel;              // （直接 seeds[i].x = y 是 CS1612）
                hit.enabled = true;
                seeds[i] = hit;
                id = hit.stableId;
                return false;
            }
            id = IntentRegistry.NewStableId("water");
            seeds.Add(new WaterSeed
            {
                stableId = id, cx = cx, cz = cz, localLevel = localLevel, enabled = true,
            });
            return true;
        }

        public bool RemoveSeed(string id)
        {
            for (var i = 0; i < seeds.Count; i++)
                if (seeds[i].stableId == id) { seeds.RemoveAt(i); return true; }
            return false;
        }

        /// <summary>改坐标（面板可编辑，用户裁定第 4 条）；目标格已被别的种子占用时拒绝，理由同 <see cref="AddSeed"/>。</summary>
        public bool MoveSeed(string id, int cx, int cz)
        {
            for (var i = 0; i < seeds.Count; i++)
                if (seeds[i].stableId != id && seeds[i].cx == cx && seeds[i].cz == cz) return false;
            for (var i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].stableId != id) continue;
                var s = seeds[i];                         // List&lt;struct&gt; 索引器是返回值：必须读-改-写回（CS1612）
                s.cx = cx; s.cz = cz;
                seeds[i] = s;
                return true;
            }
            return false;
        }

        /// <summary>改局部水位（用户裁定第 4 条「高度可调节」）。传 <see cref="WaterSeed.NoLocalLevel"/> = 取消覆盖、回落全局海平面。</summary>
        public bool SetSeedLevel(string id, float level)
        {
            for (var i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].stableId != id) continue;
                // 哨兵不能用 Mathf.Approximately 比：Approximately 走 |a-b|<eps，
                // 而 Infinity-Infinity = NaN ⇒ 两个"无覆盖"会被判成<b>不相等</b>，
                // 于是每次设同一无覆盖水位都 bump 版本（空重算风暴，正是各 Set 入口在防的事）。
                if (SameLevel(seeds[i].localLevel, level)) return false;
                var s = seeds[i];
                s.localLevel = level;
                seeds[i] = s;
                return true;
            }
            return false;
        }

        static bool SameLevel(float a, float b)
        {
            if (float.IsPositiveInfinity(a) || float.IsPositiveInfinity(b)) return a == b;
            return Mathf.Approximately(a, b);
        }

        public bool SetSeedEnabled(string id, bool on)
        {
            for (var i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].stableId != id) continue;
                if (seeds[i].enabled == on) return false;
                var s = seeds[i];
                s.enabled = on;
                seeds[i] = s;
                return true;
            }
            return false;
        }

        public void ClearAll() => seeds.Clear();

        /// <summary>
        /// 值指纹（Tracker 第二检用）。<b>不含 stableId</b>：id 是身份不是内容，
        /// 若把它算进指纹，"删一条再加回同样坐标"会因为 id 变了而判成改动。
        /// </summary>
        public ulong ComputeValueKey()
        {
            unchecked
            {
                var h = 14695981039346656037UL;
                for (var i = 0; i < seeds.Count; i++)
                {
                    var s = seeds[i];
                    h = CityRng.Mix(h, (ulong)s.cx);
                    h = CityRng.Mix(h, (ulong)s.cz);
                    h = CityRng.Mix(h, (ulong)Mathf.RoundToInt(s.localLevel * 10000f));
                    h = CityRng.Mix(h, (ulong)(s.enabled ? 1 : 0));
                }
                return h;
            }
        }
    }

    /// <summary>
    /// 洪泛派生（02 改版的核心算法）：从每个种子出发，把<b>连通且低于该种子有效水位</b>的格标成水。
    /// <para>这就是"什么是河／湖"的<b>唯一定义</b>，取代草案里的 D8 填洼 + 汇流阈值 + Strahler 一整套。
    /// 被删掉的不是功能，而是<b>正确性负担</b>：洪泛定义下「断头河」「水往高处流」「没有出口」
    /// 这三类失败模式<b>在数学上不可能发生</b> —— 水只能待在低于水位的地方。</para>
    /// <para>四条纪律（均由用户 2026-11-05 裁定）：
    /// ① <b>4 邻接</b> —— 8 邻接会让两个隔着一道对角山脊的湖"对角泄漏"合并成一个，极反直觉；
    /// ② 判据用<b>最终生效地表</b>（含已烘焙滤镜叠加）⇒ 所见即所得；
    /// ③ 每个种子按<b>自己的</b>水位洪泛，所有结果取<b>并集</b>（陆上水体靠这条支持）；
    /// ④ <see cref="CellFlag.NoWater"/> 是 S0 烘焙的负约束意图 ⇒ 洪水遇之<b>不得越过</b>，
    ///    且派生侧无权清除它（不许删别人的意图位）。</para>
    /// </summary>
    public static class WaterFlood
    {
        /// <summary>4 邻接偏移。顺序固定 ⇒ 遍历结果与实现无关，可重放。</summary>
        static readonly int[] Dx = { 1, -1, 0, 0 };
        static readonly int[] Dz = { 0, 0, 1, -1 };

        /// <summary>8 邻域偏移（仅距离场用；洪泛<b>绝不</b>用 8 邻接，见纪律①）。</summary>
        static readonly int[] Dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dz8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>队列容量下限：单个连通域最坏 = 全图格数，故调用方须给 <c>grid.CellCount</c> 容量。</summary>
        public static int RequiredQueueCapacity(in CityGrid grid) => grid.CellCount;

        /// <summary>
        /// 洪泛并写入 <see cref="CellFlag.Water"/>。<b>调用前必须先 <see cref="ClearWaterMask"/>
        /// 清掉上一轮的位</b> —— 水掩码是纯派生量，跨轮累积会让「移除水」永远移不掉。
        /// <para>⚠️ visited 必须是<b>逐种子的 generation 标记</b>，不能拿掩码的 Water 位当 visited：
        /// 那样低水位种子先淹过的格子会挡住高水位种子向<b>更高处</b>扩展，"湖里有座高地基、
        /// 基座周围应各淹各的"这类场景直接算错（我第一版就是这么写的，自查时发现）。
        /// 代价是调用方要再给一张 <c>int[CellCount]</c>（8km 档 16MB）——比算错水界便宜。</para>
        /// </summary>
        /// <param name="surfaceHeights">最终生效地表（含滤镜），长度 = grid.CellCount。</param>
        /// <param name="scratchQueue">调用方复用的 BFS 队列（≥ <see cref="RequiredQueueCapacity"/>）；
        /// 每种子新分配会在 400 万格场景造成大对象堆抖动（M4-j 同族教训）。</param>
        /// <param name="visited">逐种子访问标记（复用数组，写入当前种子的 generation = s+1）。</param>
        /// <param name="seedsFlooded">本轮<b>真正产水</b>的种子数（其余为"种子所在地已高于水位"= 水已退去）。</param>
        /// <returns>被标为水的格数（多种子并集去重后的真实数）。</returns>
        public static int Flood(WaterNetworkAsset water, in CityGrid grid, float[] surfaceHeights,
                                ushort[] mask, float globalSeaLevel, int[] scratchQueue, int[] visited,
                                out int seedsFlooded, float[] cellLevel = null)
        {
            seedsFlooded = 0;
            if (water == null || grid.CellCount <= 0) return 0;
            var cols = grid.Columns; var rows = grid.Rows;
            var waterBit = (ushort)CellFlag.Water;
            var noWaterBit = (ushort)CellFlag.NoWater;
            var total = 0;

            // generation 每轮都从 1 重新数 ⇒ 必须先清零。否则上一轮留下的标记会被当成
            // "本种子已访问"，表现为<b>第二次洪泛后水面莫名缩小</b>（典型的派生态污染，
            // 与"烘焙不先 ClearAll 就越烘越平"同族：任何累加式缓冲都必须显式定初值）。
            System.Array.Clear(visited, 0, visited.Length);

            for (var s = 0; s < water.Count; s++)
            {
                var seed = water[s];
                if (!seed.enabled) continue;
                if (seed.cx < 0 || seed.cz < 0 || seed.cx >= cols || seed.cz >= rows) continue;

                var level = seed.ResolveLevel(globalSeaLevel);
                var start = seed.cz * cols + seed.cx;

                // 种子本身不低于水位 ⇒ 这个种子本轮<b>不产水</b>（不是错误）：用户抬了地形或降了
                // 海平面，水就自然退去 —— 这正是"水随地形走"的语义。计数里区分开，面板才不至于
                // 把"有 3 个种子但都退干了"显示成"有 3 片水"。
                if (surfaceHeights[start] >= level) continue;

                var gen = s + 1;                          // generation 从 1 起（0 = 从未访问）
                var head = 0; var tail = 0;
                scratchQueue[tail++] = start;
                visited[start] = gen;
                seedsFlooded++;

                while (head < tail)
                {
                    var cur = scratchQueue[head++];
                    if ((mask[cur] & waterBit) == 0)
                    {
                        mask[cur] |= waterBit;            // 只在这里计一次，多种子重叠不重复计数
                        total++;
                    }
                    // 逐格水面高度：<b>取更高水位</b>。两种子淹到同一格时物理上就是更高的那片盖住
                    // 更低的（低于低水位的格必然也低于高水位），且<b>渲染必须有这个值</b>：
                    // 高原湖（局部水位 160m）若画在海平面 0 上，水面就埋在地形底下，
                    // 用户看到"种子加了、面板说有 169 格水、但画面什么都没有"。
                    if (cellLevel != null)
                    {
                        var old = cellLevel[cur];
                        if (!(old > level)) cellLevel[cur] = level;   // NaN/负初值都走赋值支
                    }
                    var cx = cur % cols; var cz = cur / cols;
                    for (var k = 0; k < 4; k++)
                    {
                        var nx = cx + Dx[k]; var nz = cz + Dz[k];
                        if (nx < 0 || nz < 0 || nx >= cols || nz >= rows) continue;
                        var ni = nz * cols + nx;
                        if (visited[ni] == gen) continue;             // 本种子已访问
                        if ((mask[ni] & noWaterBit) != 0) continue;   // 硬禁水：不得越过
                        if (surfaceHeights[ni] >= level) continue;    // 不低于该种子水位 = 岸
                        visited[ni] = gen;
                        scratchQueue[tail++] = ni;
                    }
                }
            }
            return total;
        }

        /// <summary>
        /// 水格外扩 <paramref name="ringCells"/> 圈写 <see cref="CellFlag.WaterBuffer"/>（不可建筑带）。
        /// <para>必须与 <see cref="Flood"/> 分成两步：缓冲要按<b>最终水掩码</b>（多种子并集）算，
        /// 在洪泛内部就地写会让先算的缓冲被后到的水吞成水域。</para>
        /// <para>⚠️ <b>缓冲用 8 邻域生长，洪泛用 4 邻域 —— 两者刻意不同</b>：
        /// 洪泛是<b>水的连通性</b>，4 邻接防止对角泄漏合并两湖（用户裁定②）；
        /// 缓冲是<b>到水的距离带</b>，几何上就是"切比雪夫距离 ≤ R"＝方窗，必须 8 邻域生长才等价。
        /// （我第一版这里写 4 邻域，与朴素方窗参考实现不等价，被自己新写的对照测试逼出来。）</para>
        /// <para>已水域格不写缓冲位（Water 与 WaterBuffer 互斥：<see cref="CellFlag.NoBuildAny"/>
        /// 里两者都禁建，重叠只会让统计与面板读数虚高）。</para>
        /// <para><b>逐轮边界 BFS，不是每水格扫 (2r+1)² 方窗</b>：后者是 O(水面积 × r²)。
        /// 本写法每格只被弹出一次、扫 8 邻 ⇒ O(缓冲面积)。缓冲只在水的 R 邻域内产生，
        /// 与全图和水总面积都无关。实测数字见 ACCEPTANCE（不在此写未量测的数）。</para>
        /// </summary>
        public static int ApplyBuffer(ushort[] mask, in CityGrid grid, int ringCells, int[] scratchQueue)
        {
            if (ringCells <= 0 || grid.CellCount <= 0) return 0;
            var cols = grid.Columns; var rows = grid.Rows;
            var waterBit = (ushort)CellFlag.Water;
            var bufferBit = (ushort)CellFlag.WaterBuffer;
            var taken = (ushort)(waterBit | bufferBit);
            var head = 0; var tail = 0;
            var added = 0;

            // 第 1 圈：水格的 8 邻非水格
            for (var cz = 0; cz < rows; cz++)
            {
                var rowBase = cz * cols;
                for (var cx = 0; cx < cols; cx++)
                {
                    if ((mask[rowBase + cx] & waterBit) == 0) continue;
                    for (var k = 0; k < 8; k++)
                    {
                        var nx = cx + Dx8[k]; var nz = cz + Dz8[k];
                        if (nx < 0 || nz < 0 || nx >= cols || nz >= rows) continue;
                        var ni = nz * cols + nx;
                        if ((mask[ni] & taken) != 0) continue;
                        mask[ni] |= bufferBit;
                        scratchQueue[tail++] = ni;
                        added++;
                    }
                }
            }

            // 第 2..r 圈：只扩展"上一圈刚标上的格" ⇒ 每格至多入队一次
            for (var ring = 1; ring < ringCells && head < tail; ring++)
            {
                var roundEnd = tail;
                while (head < roundEnd)
                {
                    var cur = scratchQueue[head++];
                    var cx = cur % cols; var cz = cur / cols;
                    for (var k = 0; k < 8; k++)
                    {
                        var nx = cx + Dx8[k]; var nz = cz + Dz8[k];
                        if (nx < 0 || nz < 0 || nx >= cols || nz >= rows) continue;
                        var ni = nz * cols + nx;
                        if ((mask[ni] & taken) != 0) continue;
                        mask[ni] |= bufferBit;
                        scratchQueue[tail++] = ni;
                        added++;
                    }
                }
            }
            return added;
        }

        /// <summary>
        /// 清除本轮派生的 Water / WaterBuffer 位。<b>绝不动 NoWater</b> —— 那是 S0 烘焙的负约束
        /// 意图，归 S0 所有；派生层删别人的意图位是"规则互相覆盖不可追溯"的典型起点（00 §9）。
        /// </summary>
        public static void ClearWaterMask(ushort[] mask)
        {
            // 必须先落成<b>变量</b>再取反：`(ushort)~(CellFlag.Water | CellFlag.WaterBuffer)` 是
            // 常量表达式，编译期算出 -7 而 ushort 不接受 ⇒ CS0221（本工程首次遇到，记此留痕）。
            var bits = (ushort)(CellFlag.Water | CellFlag.WaterBuffer);
            var keep = (ushort)~bits;
            for (var i = 0; i < mask.Length; i++) mask[i] &= keep;
        }

        /// <summary>
        /// 到水距离场（米）：供 05 的 <c>WaterProximity</c> 派生场与 07 的临水偏好使用。
        /// <para>两遍扫描是<b>切比雪夫</b>距离的线性算法（8 邻域），比洪泛的 4 邻接更贴近
        /// "我离水有多远"的观感。距离是近似值不是欧氏精确值 —— 首版够用，且必须一致：
        /// 07 用它打分时，任何"更精确"的实现都得同步改判据，否则临水偏好会静默漂移。</para>
        /// <para>无水的格保持 <see cref="float.MaxValue"/> 量级的大值；调用方负责衰减映射，
        /// 本函数不做业务判断。</para>
        /// </summary>
        public static void BuildDistanceField(ushort[] mask, in CityGrid grid, float[] outMeters)
        {
            var cols = grid.Columns; var rows = grid.Rows;
            var inf = grid.WorldSize * 4f;          // 有限大值（不用 float.MaxValue：后面要 +CellSize 会溢出成 ∞）
            var waterBit = (ushort)CellFlag.Water;

            for (var i = 0; i < outMeters.Length; i++) outMeters[i] = inf;

            for (var cz = 0; cz < rows; cz++)
                for (var cx = 0; cx < cols; cx++)
                {
                    var i = cz * cols + cx;
                    if ((mask[i] & waterBit) != 0) { outMeters[i] = 0f; continue; }
                    var best = outMeters[i];
                    for (var k = 0; k < 8; k++)
                    {
                        var nx = cx + Dx8[k]; var nz = cz + Dz8[k];
                        if (nx < 0 || nz < 0 || nx >= cols || nz >= rows) continue;
                        var v = outMeters[nz * cols + nx];
                        if (v < best) best = v;
                    }
                    outMeters[i] = best + grid.CellSize;
                }

            for (var cz = rows - 1; cz >= 0; cz--)
                for (var cx = cols - 1; cx >= 0; cx--)
                {
                    var i = cz * cols + cx;
                    if (outMeters[i] == 0f) continue;
                    var best = outMeters[i];
                    for (var k = 0; k < 8; k++)
                    {
                        var nx = cx - Dx8[k]; var nz = cz - Dz8[k];
                        if (nx < 0 || nz < 0 || nx >= cols || nz >= rows) continue;
                        var v = outMeters[nz * cols + nx] + grid.CellSize;
                        if (v < best) best = v;
                    }
                    outMeters[i] = best;
                }
        }
    }
}
