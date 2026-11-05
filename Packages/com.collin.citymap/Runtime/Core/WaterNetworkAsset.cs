using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 水体种子（Modules/02 第二次改版，2026-11-05 用户裁定：「海平面不用单独做了，
    /// 加一个高度略低于常规地面的水体种子就可以了」）。
    /// <para>一片水 = 一个种子 ＋ 它自己的<b>水面高度</b> ＋ 它的<b>扩散半径</b>。
    /// 水域 = 从种子出发 4 邻接连通、地面低于该高度、且沿谷延伸不超过半径的格。</para>
    /// <para><b>为什么必须有半径</b>（我在上一轮向用户指出、用户接受的代价）：
    /// 只有"低于水面高度"这一个判据时，在山脊上点一下会顺着单调下坡把整个山谷淹掉——
    /// 因为没有东西限制它翻不过去。半径就是那个刹车：默认给个小值，点哪里都只成一个小水塘；
    /// 想做湖或海湾，把那颗种子的半径设成 0（不限）即可。</para>
    /// <para><b>删掉的东西</b>：全局海平面（连带"点高处先询问"的整套分支）、
    /// 以及"不设局部水位"的 <c>+∞</c> 哨兵。哨兵是上一版最容易出隐性 bug 的地方——
    /// <c>Mathf.Approximately(∞,∞)</c> 返回 false，会把两个"无覆盖"判成不相等而反复重洪泛。
    /// 现在每颗种子必然有一个真实水位，比较全部走普通浮点。</para>
    /// </summary>
    [Serializable]
    public struct WaterSeed
    {
        /// <summary>稳定 id（撤销、面板定位都靠它，同 delta 层纪律）。</summary>
        public string stableId;

        /// <summary>种子格坐标（逻辑格号，<b>不是世界米</b> —— 换 worldSize/格档位时米坐标会漂）。</summary>
        public int cx;
        public int cz;

        /// <summary>
        /// 水面高度（米）。水域里的格必须<b>严格低于</b>它。
        /// <para>建种子时取"点击点地面高程 + 默认水深"，所以点低洼就成水塘、点山顶就成天池，
        /// 不再需要一个全局基准去判断"这算河还是算湖"。</para>
        /// </summary>
        public float level;

        /// <summary>
        /// 沿谷扩散半径（米）。<b>0 = 不限</b>（做海湾/大湖用）。
        /// <para>限的是<b>洪泛的步数</b>（沿水连通域走多少格），不是直线距离——
        /// 河道是弯的，直线半径会把"绕山而来的下游"错误截断。</para>
        /// </summary>
        public float radiusMeters;

        /// <summary>启用位：关掉≠删除（保留参数便于反复试验，面板可勾除）。</summary>
        public bool enabled;

        /// <summary>
        /// 半径换算成格数（<b>向上取整</b>：宁多不少，与缓冲带同一口径）。
        /// 0 / 负 / <b>非有限值</b> = 不限 ⇒ <see cref="int.MaxValue"/>。
        /// <para>NaN 必须单独兜：<c>Mathf.CeilToInt(NaN)</c> 的返回值不可依赖（实测可能是 0，
        /// 那就是"半径 0 格 = 一滴水都没有"，用户看到的现象会是"改了半径水整个消失"）。
        /// 这里把它归入"不限"而不是"0 格"——因为 NaN 来自输入框被清空，语义上更接近"没限制"，
        /// 绝不能让它变成一个巨大的格数把整张图淹掉。</para>
        /// </summary>
        public int RadiusCells(float cellSize)
        {
            // 显式判"有限且为正"：0/负/NaN/+∞ 一律归入"不限"。
            // 用 `!IsFinite || !IsPositive` 而不是 `x <= 0`：后者对 NaN 恒为 false，会把 NaN
            // 送进 CeilToInt（返回值不可依赖，实测可能是 0 ⇒ "改了半径水整个消失"）。
            if (!float.IsFinite(radiusMeters) || radiusMeters <= 0f) return int.MaxValue;
            if (!float.IsFinite(cellSize) || cellSize <= 1e-5f) return int.MaxValue;
            return Mathf.Max(1, Mathf.CeilToInt(radiusMeters / cellSize));
        }
    }

    /// <summary>
    /// 水体意图层（SO，持久化，入库）。
    /// <para>与 <see cref="SculptDeltaAsset"/>／<see cref="SculptFilterAsset"/> 一样"一物一资产"：
    /// 三者语义不同（手绘地形 / 自动滤镜 / 水意图），混存会让撤销与版本迁移互相污染。</para>
    /// <para><b>本类同时是意图登记对象</b>（<see cref="IIntentTracked"/>，2026-11-06 加）：
    /// 上一版水面的脏位记在场景组件 <see cref="CityTerrain"/> 的账本上，而 <c>Undo.RecordObject</c>
    /// 还原的是本资产 ⇒ 撤销把种子数据改回去了、序列号却没变 ⇒ 扫描器看不见差异 ⇒
    /// "改了种子/撤了种子，画面都不动"（用户反馈②）。记账必须<b>落在被撤销的那个对象上</b>，
    /// 这与 09 H-9 的教训同源：账本不序列化或不在被还原的对象上，撤销就必然漏检。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/CityMap/Water Network", fileName = "WaterNetwork")]
    public sealed class WaterNetworkAsset : ScriptableObject, IIntentTracked
    {
        [SerializeField] int schemaVersion = 2;
        // ⚠️ **绝不能加 readonly**：Unity 的序列化管线会<b>跳过 readonly 字段</b>（带 [SerializeField] 也一样跳）。
        // 本轮取证在磁盘上抓到实证：本资产写进 YAML 后<b>只有 schemaVersion 一行</b>，seeds 整个不见了
        // ⇒ 用户加的水种子重开工程就没了。delta / filter 两个资产当时是同一写法，一并改掉。
        // 可失败断言：收录性见 AssetPersistenceTests.Layer_Assets_List_And_Ledger_Fields_Are_Serialized；
        // 落盘文本见 WaterFloodTests.Seed_And_Ledger_Actually_Write_Into_Asset_Yaml_On_Disk；
        // 撤销往返见 AssetPersistenceTests.Undo_Restores_Water_Seeds_And_Their_Ledger。
        [SerializeField] System.Collections.Generic.List<WaterSeed> seeds
            = new System.Collections.Generic.List<WaterSeed>(8);

        [Header("意图记账（撤销可见的唯一依据）")]
        [SerializeField] string stableId;
        [SerializeField] IntentChangeLedger ledger = new IntentChangeLedger();

        public int SchemaVersion => schemaVersion;
        public int Count => seeds.Count;
        public bool IsEmpty => seeds.Count == 0;

        public string StableId => stableId;
        public IntentChangeLedger Ledger => ledger;
        public long ManualVersion => ledger != null ? ledger.ManualVersion : 0L;

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

        // ───────────── 意图登记 ─────────────

        private void OnEnable() => EnsureRegistered();

        private void OnDisable() => IntentRegistry.Unregister(this);

        void EnsureRegistered()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = IntentRegistry.NewStableId("waterNet");
            ledger ??= new IntentChangeLedger();
            IntentRegistry.Register(this);
        }

        /// <summary>
        /// 一次水意图提交：<b>调用方给影响范围</b>，本方法只负责 bump 通道。
        /// <para>范围为什么由调用方给：种子存的是<b>逻辑格号</b>，换成世界米需要格网，
        /// 而本资产不持有格网（换 worldSize/档位时同一份种子要能重解释）。
        /// 面板与笔刷都现成有 <see cref="CityGrid"/>，由它们算世界包围盒。</para>
        /// <para>传 <c>default</c>（零尺寸）会被上下文当"无脏区"丢掉 ⇒ 增量重建退化成全图，
        /// 所以这里对零尺寸兜底成最小盒（与 <see cref="CityTerrain"/> 同一处置）。</para>
        /// </summary>
        public void CommitChange(Bounds affected)
        {
            EnsureRegistered();
            if (affected.size.x <= 0f && affected.size.z <= 0f)
                affected = new Bounds(affected.center, Vector3.one * MinWaterBounds);
            ledger.Bump(IntentKind.RiverStroke, affected);
        }

        const float MinWaterBounds = 32f;

        /// <summary>单颗种子格的世界包围盒（面板/笔刷共用的范围口径）。</summary>
        public static Bounds SeedBounds(in CityGrid grid, int cx, int cz)
        {
            if (grid.CellCount <= 0) return default;
            var w = grid.CellCenterWorld(cx, cz);
            var s = Mathf.Max(MinWaterBounds, grid.CellSize * 2f);
            return new Bounds(new Vector3(w.x, 0f, w.y), new Vector3(s, 1f, s));
        }

        // ───────────── 写入口（全部走 CommitChange 才算提交）─────────────

        /// <summary>
        /// 添加种子。同格已存在则<b>就地更新而非追加</b>并返回 false ——
        /// 同格两个种子只让洪泛结果重复，却会让用户在列表里看到两条"看不出差别"的条目。
        /// </summary>
        public bool AddSeed(int cx, int cz, float level, float radiusMeters, out string id)
        {
            for (var i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].cx != cx || seeds[i].cz != cz) continue;
                var hit = seeds[i];                       // List<struct> 索引器返回值，须读-改-写回
                hit.level = level;                        // （直接 seeds[i].x = y 是 CS1612）
                hit.radiusMeters = radiusMeters;
                hit.enabled = true;
                seeds[i] = hit;
                id = hit.stableId;
                return false;
            }
            id = IntentRegistry.NewStableId("water");
            seeds.Add(new WaterSeed
            {
                stableId = id, cx = cx, cz = cz, level = level,
                radiusMeters = radiusMeters, enabled = true,
            });
            return true;
        }

        public bool RemoveSeed(string id)
        {
            for (var i = 0; i < seeds.Count; i++)
                if (seeds[i].stableId == id) { seeds.RemoveAt(i); return true; }
            return false;
        }

        /// <summary>改坐标（面板可编辑）；目标格已被别的种子占用时拒绝，理由同 <see cref="AddSeed"/>。</summary>
        public bool MoveSeed(string id, int cx, int cz)
        {
            for (var i = 0; i < seeds.Count; i++)
                if (seeds[i].stableId != id && seeds[i].cx == cx && seeds[i].cz == cz) return false;
            for (var i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].stableId != id) continue;
                var s = seeds[i];                         // List<struct> 索引器是返回值：必须读-改-写回（CS1612）
                s.cx = cx; s.cz = cz;
                seeds[i] = s;
                return true;
            }
            return false;
        }

        /// <summary>改水面高度（用户裁定「高度可调节」）。</summary>
        public bool SetSeedLevel(string id, float level)
        {
            if (float.IsNaN(level) || float.IsInfinity(level)) return false;   // 脏值会让比较全废、水面无声消失
            for (var i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].stableId != id) continue;
                if (Mathf.Approximately(seeds[i].level, level)) return false;
                var s = seeds[i];
                s.level = level;
                seeds[i] = s;
                return true;
            }
            return false;
        }

        /// <summary>改扩散半径。传 0 = 不限（做海湾/大湖）。负数按 0 处理。</summary>
        public bool SetSeedRadius(string id, float meters)
        {
            if (float.IsNaN(meters) || float.IsPositiveInfinity(meters)) return false;
            var v = Mathf.Max(0f, meters);
            for (var i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].stableId != id) continue;
                if (Mathf.Approximately(seeds[i].radiusMeters, v)) return false;
                var s = seeds[i];
                s.radiusMeters = v;
                seeds[i] = s;
                return true;
            }
            return false;
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
        /// 值指纹（面板第二检用）。<b>不含 stableId</b>：id 是身份不是内容，
        /// 若把它算进指纹，"删一条再加回同样参数"会因为 id 变了而判成改动。
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
                    h = CityRng.Mix(h, (ulong)Mathf.RoundToInt(s.level * 10000f));
                    h = CityRng.Mix(h, (ulong)Mathf.RoundToInt(s.radiusMeters * 100f));
                    h = CityRng.Mix(h, (ulong)(s.enabled ? 1 : 0));
                }
                return h;
            }
        }
    }

    /// <summary>
    /// 洪泛派生（02 第二次改版的核心算法）：从每个种子出发，把<b>沿谷连通、低于该种子水面高度、
    /// 且在洪泛步数半径内</b>的格标成水。
    /// <para>这是"什么算水"的<b>唯一定义</b>。它取代草案里的 D8 填洼 + 汇流阈值 + Strahler 一整套，
    /// 删掉的不是功能而是<b>正确性负担</b>：洪泛定义下「断头河」「水往高处流」「内陆盆地没有出口」
    /// 这三类失败模式<b>在数学上不可能发生</b> —— 水只能待在低于水面高度的地方。</para>
    /// <para>五条纪律：
    /// ① <b>4 邻接</b> —— 8 邻接会让两个隔着一道对角山脊的湖"对角泄漏"合并成一个，极反直觉；
    /// ② 判据用<b>最终生效地表</b>（base → delta → 滤镜）⇒ 所见即所得；
    /// ③ 每个种子按<b>自己的</b>水面高度洪泛，所有结果取<b>并集</b>（天池/水库与海湾天然共存）；
    /// ④ <see cref="CellFlag.NoWater"/> 是 S0 烘焙的负约束意图 ⇒ 洪水遇之<b>不得越过</b>，
    ///    且派生侧无权清除它（不许删别人的意图位）；
    /// ⑤ 半径限的是<b>洪泛轮数</b>（沿连通域走几格），不是直线距离 —— 河道是弯的。</para>
    /// </summary>
    public static class WaterFlood
    {
        /// <summary>4 邻接偏移。顺序固定 ⇒ 遍历结果与实现无关，可重放。</summary>
        static readonly int[] Dx = { 1, -1, 0, 0 };
        static readonly int[] Dz = { 0, 0, 1, -1 };

        /// <summary>8 邻域偏移（仅缓冲带与距离场用；洪泛<b>绝不</b>用 8 邻接，见纪律①）。</summary>
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
        /// <para>⚠️ generation 每轮都从 1 重新数 ⇒ 必须先清零。否则上一轮留下的标记会被当成
        /// "本种子已访问"，表现为<b>第二次洪泛后水面莫名缩小</b>（典型的派生态污染）。</para>
        /// <para><b>半径按轮数展开</b>（与 <see cref="ApplyBuffer"/> 同一写法）：
        /// 每轮只扩展"上一轮刚标上的格"，跑满 <c>radiusCells</c> 轮即停 ⇒ 限的是洪泛步数、
        /// 与弯度无关，且<b>不需要再给一张距离数组</b>（省 16MB）。</para>
        /// </summary>
        /// <param name="surfaceHeights">最终生效地表（含滤镜），长度 = grid.CellCount。</param>
        /// <param name="scratchQueue">调用方复用的 BFS 队列（≥ <see cref="RequiredQueueCapacity"/>）；
        /// 每种子新分配会在 400 万格场景造成大对象堆抖动（M4-j 同族教训）。</param>
        /// <param name="visited">逐种子访问标记（复用数组，写入当前种子的 generation = s+1）。</param>
        /// <param name="seedsFlooded">本轮<b>真正产水</b>的种子数（其余 = 种子格本身已不低于水面高度）。</param>
        /// <returns>被标为水的格数（多种子并集去重后的真实数）。</returns>
        public static int Flood(WaterNetworkAsset water, in CityGrid grid, float[] surfaceHeights,
                                ushort[] mask, int[] scratchQueue, int[] visited,
                                out int seedsFlooded, float[] cellLevel = null)
        {
            seedsFlooded = 0;
            if (water == null || grid.CellCount <= 0) return 0;
            var cols = grid.Columns; var rows = grid.Rows;
            var waterBit = (ushort)CellFlag.Water;
            var noWaterBit = (ushort)CellFlag.NoWater;
            var total = 0;

            System.Array.Clear(visited, 0, visited.Length);

            for (var s = 0; s < water.Count; s++)
            {
                var seed = water[s];
                if (!seed.enabled) continue;
                if (seed.cx < 0 || seed.cz < 0 || seed.cx >= cols || seed.cz >= rows) continue;

                var level = seed.level;
                var reach = seed.RadiusCells(grid.CellSize);
                var start = seed.cz * cols + seed.cx;

                // 种子本身不低于水面高度 ⇒ 这个种子本轮<b>不产水</b>（不是错误）：用户抬了地形，
                // 水就自然退去 —— 这正是"水随地形走"的语义。计数里区分开，面板才不至于
                // 把"有 3 个种子但都退干了"显示成"有 3 片水"。
                if (!(surfaceHeights[start] < level)) continue;

                var gen = s + 1;                          // generation 从 1 起（0 = 从未访问）
                var head = 0; var tail = 0;
                scratchQueue[tail++] = start;
                visited[start] = gen;
                seedsFlooded++;
                if (Mark(mask, cellLevel, start, level, waterBit)) total++;

                // 逐轮展开：round 从 1 到 reach，每轮只处理"上一轮刚入队的格"。
                // reach = int.MaxValue（不限半径）时循环条件里的 head<tail 会先耗尽 ⇒ 正常收敛。
                for (var round = 1; round <= reach && head < tail; round++)
                {
                    var roundEnd = tail;
                    while (head < roundEnd)
                    {
                        var cur = scratchQueue[head++];
                        var cx = cur % cols; var cz = cur / cols;
                        for (var k = 0; k < 4; k++)
                        {
                            var nx = cx + Dx[k]; var nz = cz + Dz[k];
                            if (nx < 0 || nz < 0 || nx >= cols || nz >= rows) continue;
                            var ni = nz * cols + nx;
                            if (visited[ni] == gen) continue;             // 本种子已访问
                            if ((mask[ni] & noWaterBit) != 0) continue;   // 硬禁水：不得越过
                            if (!(surfaceHeights[ni] < level)) continue;  // 不低于水面高度 = 岸
                            visited[ni] = gen;
                            scratchQueue[tail++] = ni;
                            if (Mark(mask, cellLevel, ni, level, waterBit)) total++;
                        }
                    }
                }
            }
            return total;
        }

        /// <summary>写掩码与逐格水面高度；返回 true = 本格<b>首次</b>被标成水（调用方据此计数，多种子重叠不重复计）。</summary>
        static bool Mark(ushort[] mask, float[] cellLevel, int idx, float level, ushort waterBit)
        {
            var fresh = (mask[idx] & waterBit) == 0;
            mask[idx] |= waterBit;
            if (cellLevel != null)
            {
                // 逐格水面高度：<b>取更高水位</b>。多种子淹到同一格时物理上就是更高的那片盖住更低的
                // （低于低水位的格必然也低于高水位）。渲染必须要这个值：天池 160m 若一律画在 0m
                // 就直接埋进地形里看不见。
                var old = cellLevel[idx];
                if (!(old > level)) cellLevel[idx] = level;   // NaN/负初值都走赋值支
            }
            return fresh;
        }

        /// <summary>
        /// 水格外扩 <paramref name="ringCells"/> 圈写 <see cref="CellFlag.WaterBuffer"/>（不可建筑带）。
        /// <para>必须与 <see cref="Flood"/> 分成两步：缓冲要按<b>最终水掩码</b>（多种子并集）算，
        /// 在洪泛内部就地写会让先算的缓冲被后到的水吞成水域。</para>
        /// <para>⚠️ <b>缓冲用 8 邻域生长，洪泛用 4 邻域 —— 两者刻意不同</b>：
        /// 洪泛是<b>水的连通性</b>，4 邻接防止对角泄漏合并两湖（纪律①）；
        /// 缓冲是<b>到水的距离带</b>，几何上就是"切比雪夫距离 ≤ R"＝方窗，必须 8 邻域生长才等价。
        /// （我第一版这里写 4 邻域，与朴素方窗参考实现不等价，被自己新写的对照测试逼出来。）</para>
        /// <para>已水域格不写缓冲位（Water 与 WaterBuffer 互斥：<see cref="CellFlag.NoBuildAny"/>
        /// 里两者都禁建，重叠只会让统计与面板读数虚高）。</para>
        /// <para><b>逐轮边界 BFS</b>：每格只被弹出一次、扫 8 邻 ⇒ O(缓冲面积)，
        /// 与全图和水总面积都无关（实测数字见 ACCEPTANCE，不在此写未量测的数）。</para>
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
        /// <para>无水的格保持大值；调用方负责衰减映射，本函数不做业务判断。</para>
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
