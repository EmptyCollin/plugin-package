using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Collin.CityMap.Tests
{
    /// <summary>
    /// 水体（02 改版：水 = 低于水位的连通域）核心判据。
    /// <para>用户 2026-11-05 裁定的四条在这里逐条钉住：
    /// ①海平面默认 0；②4 邻接（不是 8 邻接）；③判据用最终生效地表；
    /// ④陆上水体＝种子带局部水位，且要先问"是否生成陆上水体"。</para>
    /// <para>纪律同本包其他测试：断言用<b>独立参考实现或解析事实</b>，不自反调一遍被测代码；
    /// 每条"通过"都配一条能证明它<b>真的会失败</b>的对照。</para>
    /// </summary>
    public class WaterFloodTests
    {
        const float Cell = 2f;
        const int N = 40;                       // 40×40 格 @2m = 80m 见方，小到能手写期望值

        CityGrid _grid;
        readonly Object[] _track = new Object[8];

        [SetUp]
        public void SetUp()
        {
            _grid = new CityGrid(N * Cell, Cell, Cell * 4f, N * Cell, N * Cell * 2f, 0f, Vector2.zero);
            Assert.AreEqual(N, _grid.Columns, "前提：测试网格必须是 40×40");
            Assert.AreEqual(N, _grid.Rows);
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _track.Length; i++)
                if (_track[i] != null) Object.DestroyImmediate(_track[i], true);
        }

        WaterNetworkAsset NewWater()
        {
            var w = ScriptableObject.CreateInstance<WaterNetworkAsset>();
            for (var i = 0; i < _track.Length; i++)
                if (_track[i] == null) { _track[i] = w; break; }
            return w;
        }

        static float[] Flat(int n, float h)
        {
            var a = new float[n];
            for (var i = 0; i < n; i++) a[i] = h;
            return a;
        }

        static void Bufs(CityGrid g, out ushort[] mask, out int[] queue, out int[] visited)
        {
            mask = new ushort[g.CellCount];
            queue = new int[g.CellCount];
            visited = new int[g.CellCount];
        }

        bool IsWater(ushort[] mask, int cx, int cz)
            => (mask[cz * _grid.Columns + cx] & (ushort)CellFlag.Water) != 0;

        // ───────────────────────── ① 基本判据 ─────────────────────────

        /// <summary>
        /// 只有<b>严格低于水位</b>的格成水。等于水位不算：水面与地面齐平时那是水陆交界，
        /// 判成水会让"刚好填到水面"的高地莫名被淹。
        /// </summary>
        [Test]
        public void Flood_Marks_Only_Cells_Strictly_Below_Level()
        {
            var w = NewWater();
            w.AddSeed(20, 20, WaterSeed.NoLocalLevel, out _);
            Bufs(_grid, out var mask, out var queue, out var visited);

            var h = Flat(_grid.CellCount, 10f);
            for (var cz = 15; cz <= 25; cz++)
                for (var cx = 15; cx <= 25; cx++)
                    h[cz * _grid.Columns + cx] = 1f;

            var cells = WaterFlood.Flood(w, _grid, h, mask, 5f, queue, visited, out var active);
            Assert.Greater(active, 0, "种子在低地，必须产水");
            Assert.AreEqual(11 * 11, cells, "低地 11×11 必须全淹、且不得越出到 10m 高地");
            Assert.IsTrue(IsWater(mask, 20, 20));
            Assert.IsFalse(IsWater(mask, 20, 10), "高地（10m > 水位 5m）不得成水");

            // 恰好等于水位：一滴水都没有
            Bufs(_grid, out var m2, out var q2, out var v2);
            var hEq = Flat(_grid.CellCount, 5f);
            var c2 = WaterFlood.Flood(w, _grid, hEq, m2, 5f, q2, v2, out var a2);
            Assert.AreEqual(0, a2, "全图高程==水位 ⇒ 等于不算低于");
            Assert.AreEqual(0, c2);
        }

        /// <summary>
        /// <b>4 邻接，绝不对角泄漏</b>（用户裁定②）。
        /// <para>这是本定义里最容易写错、后果最坏的一条：8 邻接会让两片被<b>对角山脊</b>隔开的湖
        /// 从角缝"渗"成一片，现象是"我挖的两个湖自己合并了"，且只在角部相邻时发生、极难归因。</para>
        /// </summary>
        [Test]
        public void Flood_Uses_4_Neighbors_And_Never_Leaks_Through_Diagonal_Ridge()
        {
            var w = NewWater();
            w.AddSeed(5, 8, WaterSeed.NoLocalLevel, out _);      // 种子在对角山脊的左下侧
            Bufs(_grid, out var mask, out var queue, out var visited);

            var h = Flat(_grid.CellCount, 1f);                   // 全图低地
            for (var d = 0; d < N; d++) h[d * _grid.Columns + d] = 100f;   // 主对角山脊（cx==cz）

            WaterFlood.Flood(w, _grid, h, mask, 5f, queue, visited, out _);

            Assert.IsTrue(IsWater(mask, 5, 8), "种子所在侧必须成水");
            Assert.IsTrue(IsWater(mask, 3, 30), "山脊左下侧（cx<cz，4 邻可达）必须被淹");
            Assert.IsTrue(IsWater(mask, 20, 35), "左下侧深处同样可达");
            Assert.IsFalse(IsWater(mask, 30, 3),
                "山脊右上侧（cx>cz）只能穿<b>对角角缝</b>抵达 ⇒ 4 邻接下必须<b>无</b>水");

            // 防恒真对照：山脊开一个 4 邻缺口 ⇒ 另一侧必须被淹
            Bufs(_grid, out var m2, out var q2, out var v2);
            var h2 = (float[])h.Clone();
            h2[12 * _grid.Columns + 12] = 1f;
            WaterFlood.Flood(w, _grid, h2, m2, 5f, q2, v2, out _);
            Assert.IsTrue(IsWater(m2, 30, 3),
                "山脊开了 4 邻缺口后水必须贯通到另一侧（否则上面那条「不成水」是空过的）");
        }

        // ─────────────────── ② 局部水位＝陆上水体 ───────────────────

        /// <summary>
        /// 高原天池：整片地形都远高于海平面时，<b>不带</b>局部水位的种子产不出水
        /// （全局单水位的固有局限），<b>带了</b>就能在高原上做出湖（用户裁定的"陆上水体"）。
        /// </summary>
        [Test]
        public void Local_Level_Makes_Landlocked_Lake_Above_Sea_Level()
        {
            var cx = N / 2; var cz = N / 2;
            var h = Flat(_grid.CellCount, 200f);                                  // 高原
            for (var z = cz - 6; z <= cz + 6; z++)
                for (var x = cx - 6; x <= cx + 6; x++)
                    h[z * _grid.Columns + x] = 150f;                              // 盆

            Bufs(_grid, out var m1, out var q1, out var v1);
            var wGlobal = NewWater();
            wGlobal.AddSeed(cx, cz, WaterSeed.NoLocalLevel, out _);
            var c1 = WaterFlood.Flood(wGlobal, _grid, h, m1, 0f, q1, v1, out var a1);
            Assert.AreEqual(0, a1, "只用海平面 0：高原盆底 150m 不低于 ⇒ 不该有水");
            Assert.AreEqual(0, c1);

            Bufs(_grid, out var m2, out var q2, out var v2);
            var wLocal = NewWater();
            wLocal.AddSeed(cx, cz, 160f, out _);                                   // 局部水位
            var c2 = WaterFlood.Flood(wLocal, _grid, h, m2, 0f, q2, v2, out var a2);
            Assert.Greater(a2, 0, "带局部水位的种子必须在高原盆地里成湖");
            Assert.AreEqual(13 * 13, c2, "湖只淹盆内 13×13，不得漫过 200m 盆沿");
            Assert.IsFalse(IsWater(m2, cx + 10, cz), "盆沿外（200m > 160m）不该有水");
        }

        /// <summary>
        /// 多种子各自按<b>自己的</b>水位扩展：低水位种子淹过的格子，高水位种子必须能<b>越过它</b>继续扩。
        /// <para>存在理由：我第一版拿掩码的 Water 位当 visited，低水位先淹的格会挡住高水位种子向外扩展
        /// （"湖里有高地基、四周各淹各的"直接算错）。这条断言专门钉它。</para>
        /// </summary>
        [Test]
        public void Higher_Seed_Can_Spread_Past_Cells_Already_Wet_From_Lower_Seed()
        {
            // 阶梯：高程只随 cx 递增 ⇒ 水位决定东界
            var h = new float[_grid.CellCount];
            for (var cz = 0; cz < N; cz++)
                for (var cx = 0; cx < N; cx++)
                    h[cz * _grid.Columns + cx] = 10f + 2f * cx;

            // 只有低水位种子时：东界 cx≤1
            Bufs(_grid, out var mA, out var qA, out var vA);
            var wA = NewWater();
            wA.AddSeed(1, 5, 13f, out _);
            WaterFlood.Flood(wA, _grid, h, mA, 0f, qA, vA, out var aA);
            Assert.AreEqual(1, aA);
            Assert.IsTrue(IsWater(mA, 1, 5));
            Assert.IsFalse(IsWater(mA, 5, 25), "单种子对照：低水位 13 不该淹到 cx=5（h=20）");

            // 加一个高水位种子（放在已被 A 淹过的列上）⇒ 必须能向东越过 cx=1 继续扩到 cx≤5
            Bufs(_grid, out var mB, out var qB, out var vB);
            var wB = NewWater();
            wB.AddSeed(1, 5, 13f, out _);
            wB.AddSeed(1, 25, 21f, out _);
            WaterFlood.Flood(wB, _grid, h, mB, 0f, qB, vB, out var aB);
            Assert.AreEqual(2, aB, "两个种子都在各自水位之下，都必须产水");
            Assert.IsTrue(IsWater(mB, 5, 25),
                "高水位种子必须淹到 cx=5（20<21）——即使 cx≤1 已被低水位种子先淹过");
            Assert.IsFalse(IsWater(mB, 6, 25), "但不得越过自己的水位界（22>21）");
        }

        /// <summary>
        /// 重新洪泛<b>必须与上一轮无关</b>。visited 用 generation 标记，若不清零就会把上一轮的
        /// 标记当"本轮已访问" ⇒ 表现为<b>第二次洪泛后水面莫名缩小</b>（水掩码清了不等于访问标记清了）。
        /// </summary>
        [Test]
        public void Reflood_Produces_Identical_Area_Without_Stale_Visited_Marks()
        {
            var w = NewWater();
            w.AddSeed(10, 10, WaterSeed.NoLocalLevel, out _);
            var h = Flat(_grid.CellCount, 9f);
            for (var cz = 0; cz < 20; cz++)
                for (var cx = 0; cx < 20; cx++)
                    h[cz * _grid.Columns + cx] = 1f;              // 左下 20×20 低地

            Bufs(_grid, out var mask, out var queue, out var visited);
            var first = WaterFlood.Flood(w, _grid, h, mask, 5f, queue, visited, out var a1);
            Assert.AreEqual(20 * 20, first, "前提：首轮淹了 20×20");

            // 只清水掩码、复用同一批 visited ⇒ 必须与首轮完全一致
            WaterFlood.ClearWaterMask(mask);
            var second = WaterFlood.Flood(w, _grid, h, mask, 5f, queue, visited, out var a2);
            Assert.AreEqual(first, second, "重跑洪泛面积必须逐格相同（visited 未清零就会偏小）");
            Assert.AreEqual(a1, a2);
        }

        // ─────────────────── ③ 负约束与缓冲带 ───────────────────

        /// <summary><see cref="CellFlag.NoWater"/>（S0 烘焙的禁水负约束）必须<b>挡住洪水</b>。</summary>
        [Test]
        public void NoWater_Flag_Blocks_Flood_From_Crossing()
        {
            var w = NewWater();
            w.AddSeed(5, 20, WaterSeed.NoLocalLevel, out _);
            var h = Flat(_grid.CellCount, 1f);                    // 全图低地

            Bufs(_grid, out var mask, out var queue, out var visited);
            for (var cz = 0; cz < N; cz++) mask[cz * _grid.Columns + 12] |= (ushort)CellFlag.NoWater;
            WaterFlood.Flood(w, _grid, h, mask, 5f, queue, visited, out _);

            Assert.IsTrue(IsWater(mask, 5, 20));
            Assert.IsTrue(IsWater(mask, 11, 20), "墙左侧（4 邻可达）必须成水");
            Assert.IsFalse(IsWater(mask, 12, 20), "禁水格本身不得成水");
            Assert.IsFalse(IsWater(mask, 13, 20), "墙右侧虽同为低地，但只能穿墙抵达 ⇒ 不得成水");

            // 防恒真对照：墙开一个缺口 ⇒ 右侧必须进水
            Bufs(_grid, out var m2, out var q2, out var v2);
            m2[20 * _grid.Columns + 12] = 0;
            WaterFlood.Flood(w, _grid, h, m2, 5f, q2, v2, out _);
            Assert.IsTrue(IsWater(m2, 20, 20), "墙开缺口后右侧必须进水（否则上面那条是空过的）");
        }

        /// <summary>
        /// 缓冲带必须与<b>朴素方窗参考实现</b>逐格一致。
        /// <para>我把"每水格扫 (2r+1)²"的 O(水面积×r²) 版改成了逐轮边界 BFS 的 O(缓冲面积)，
        /// <b>改了算法就必须证明结果相同</b>，不能只说"更快了"。</para>
        /// <para>缓冲用 <b>8 邻域生长</b>（=切比雪夫方窗），与洪泛的 4 邻域<b>刻意不同</b>：
        /// 洪泛是水的连通性（防对角泄漏），缓冲是"离水多远"的距离带（几何上就是方窗）。
        /// 我第一版缓冲也写 4 邻域，与方窗参考不等价，正是被这条对照断言逼出来的。</para>
        /// </summary>
        [Test]
        public void Buffer_Matches_Naive_Square_Window_Reference()
        {
            const int R = 3;
            var h = Flat(_grid.CellCount, 20f);
            for (var cz = 12; cz <= 24; cz++)
                for (var cx = 8; cx <= 22; cx++)
                    h[cz * _grid.Columns + cx] = 1f;
            for (var cz = 16; cz <= 19; cz++)
                for (var cx = 13; cx <= 16; cx++)
                    h[cz * _grid.Columns + cx] = 20f;             // 水中的"岛"，让形状不规则

            var w = NewWater();
            w.AddSeed(10, 14, WaterSeed.NoLocalLevel, out _);
            Bufs(_grid, out var mask, out var queue, out var visited);
            WaterFlood.Flood(w, _grid, h, mask, 5f, queue, visited, out var active);
            Assert.Greater(active, 0, "前提：确有水");

            var mine = (ushort[])mask.Clone();
            var addedMine = WaterFlood.ApplyBuffer(mine, _grid, R, queue);
            Assert.Greater(addedMine, 0, "前提：确有缓冲带产生");

            // 参考实现：每个水格的 (2R+1)² 方窗内、非水格 ⇒ 缓冲
            var refer = (ushort[])mask.Clone();
            var cols = _grid.Columns; var rows = _grid.Rows;
            var waterBit = (ushort)CellFlag.Water;
            var bufferBit = (ushort)CellFlag.WaterBuffer;
            for (var cz = 0; cz < rows; cz++)
                for (var cx = 0; cx < cols; cx++)
                {
                    var i = cz * cols + cx;
                    if ((mask[i] & waterBit) == 0) continue;
                    for (var dz = -R; dz <= R; dz++)
                        for (var dx = -R; dx <= R; dx++)
                        {
                            var nx = cx + dx; var nz = cz + dz;
                            if (nx < 0 || nz < 0 || nx >= cols || nz >= rows) continue;
                            var ni = nz * cols + nx;
                            if ((mask[ni] & waterBit) != 0) continue;
                            if ((refer[ni] & bufferBit) != 0) continue;
                            refer[ni] |= bufferBit;
                        }
                }

            for (var i = 0; i < mine.Length; i++)
                Assert.AreEqual(refer[i], mine[i],
                    $"第 {i} 格（x={i % cols},z={i / cols}）缓冲位与方窗参考实现不符 ⇒ 逐轮 BFS 有错");

            // 水格与缓冲格必须互斥（两者都在 NoBuildAny 里，重叠会让统计与面板读数虚高）
            for (var i = 0; i < mine.Length; i++)
                if ((mine[i] & waterBit) != 0)
                    Assert.AreEqual(0, (int)(mine[i] & bufferBit),
                        $"水格 {i} 同时带缓冲位 ⇒ 两者必须互斥");
        }

        /// <summary>
        /// 缓冲带是<b>纯距离带</b>：水被禁水墙挡住后，墙的另一侧（离水 &gt; R）不得被划成缓冲。
        /// 否则"水没淹到"的地块会莫名变成禁建带，用户完全查不出原因。
        /// </summary>
        [Test]
        public void Buffer_Does_Not_Extend_Past_A_Water_Barrier()
        {
            var w = NewWater();
            w.AddSeed(5, 20, WaterSeed.NoLocalLevel, out _);
            var h = Flat(_grid.CellCount, 1f);
            Bufs(_grid, out var mask, out var queue, out var visited);
            for (var cz = 0; cz < N; cz++)
            {
                mask[cz * _grid.Columns + 12] |= (ushort)CellFlag.NoWater;
                mask[cz * _grid.Columns + 13] |= (ushort)CellFlag.NoWater;
            }
            WaterFlood.Flood(w, _grid, h, mask, 5f, queue, visited, out _);
            var added = WaterFlood.ApplyBuffer(mask, _grid, 1, queue);

            Assert.Greater(added, 0, "对照前提：墙前的水边必须产生缓冲带");
            Assert.IsFalse((mask[20 * _grid.Columns + 13] & (ushort)CellFlag.WaterBuffer) != 0,
                "距水 2 格处（R=1）不得被缓冲带污染");
            Assert.IsFalse((mask[20 * _grid.Columns + 14] & (ushort)CellFlag.WaterBuffer) != 0,
                "墙后更远处不得被缓冲带污染");
        }

        // ─────────────────── ④ 意图层与脏通道 ───────────────────

        /// <summary>种子列表语义：同格覆盖而非追加、目标被占时拒绝移动、<b>+∞ 哨兵</b>的比较陷阱、指纹认内容不认 id。</summary>
        [Test]
        public void Seed_List_Overwrites_Rejects_And_Treats_Infinity_Sentinel_Correctly()
        {
            var w = NewWater();
            Assert.IsTrue(w.IsEmpty);

            // 同格覆盖
            Assert.IsTrue(w.AddSeed(30, 30, WaterSeed.NoLocalLevel, out var idX), "新格算新增");
            Assert.IsFalse(w.AddSeed(30, 30, 9f, out var idX2), "同格再点必须<b>覆盖</b>而非追加");
            Assert.AreEqual(idX, idX2, "覆盖必须沿用原 id（否则面板出现两条看不出差别的条目）");
            Assert.AreEqual(1, w.Count);

            // 移动与冲突
            w.AddSeed(4, 4, WaterSeed.NoLocalLevel, out var id1);
            w.AddSeed(6, 6, WaterSeed.NoLocalLevel, out var id2);
            Assert.AreEqual(3, w.Count);
            Assert.IsTrue(w.MoveSeed(id1, 7, 7));
            Assert.IsFalse(w.MoveSeed(id2, 7, 7), "目标格已被占 ⇒ 拒绝");
            Assert.IsTrue(w.TryGet(id1, out var s1));
            Assert.AreEqual(7, s1.cx);

            // ★ 哨兵陷阱：Mathf.Approximately(+∞, +∞) 走 |a-b|<eps ⇒ NaN ⇒ 返回 false
            //   ⇒ 会把"重复设无覆盖"误判成改动，每次刷新都空重算一遍全图洪泛。
            Assert.IsFalse(w.SetSeedLevel(id1, WaterSeed.NoLocalLevel),
                "已是无覆盖再设无覆盖 ⇒ 不得判为改动");
            Assert.IsTrue(w.SetSeedLevel(id1, 33f), "真改值必须判为改动");
            Assert.IsFalse(w.SetSeedLevel(id1, 33f), "同值重复设不算改动");
            Assert.IsTrue(w.SetSeedLevel(id1, WaterSeed.NoLocalLevel),
                "从有覆盖取消覆盖必须算改动（与上面那条方向相反，两个都得对）");

            // 指纹只认内容：删一个再原样加回 ⇒ 指纹必须相同（id 不同）。
            // ⚠️ keyBefore 必须取在<b>删除之前</b>：取在删除之后就变成"2 个种子 vs 3 个种子"，
            //    测的是数量而非身份，我第一版正是这样写错的（自查纠正）。
            var keyBefore = w.ComputeValueKey();
            Assert.IsTrue(w.RemoveSeed(id2));
            Assert.AreNotEqual(keyBefore, w.ComputeValueKey(), "少一个种子，指纹必须变");
            Assert.IsTrue(w.AddSeed(6, 6, WaterSeed.NoLocalLevel, out var id2b));
            Assert.AreNotEqual(id2, id2b, "新种子拿新 id（身份是新生成的）");
            Assert.AreEqual(keyBefore, w.ComputeValueKey(),
                "内容相同 ⇒ 指纹必须相同（id 是身份不是内容，算进指纹会让『重新加回』被当成改动）");

            Assert.IsTrue(w.RemoveSeed(idX));
            Assert.IsFalse(w.RemoveSeed(idX), "重复删除必须返回 false");
            Assert.IsTrue(w.SetSeedEnabled(id1, false));
            Assert.IsFalse(w.SetSeedEnabled(id1, false), "重复禁用不算改动");
        }

        /// <summary>
        /// <b>改海平面绝不允许重跑噪声基底</b>（用户裁定：改水位只动水面与下游）。
        /// <para>若走 WorldParam 会命中传播表首行「Seed|World ⇒ All」⇒ S1a 重跑
        /// （M4-j 实测 4860ms）。这条断言钉的是<b>通道选择</b>这个决策本身。</para>
        /// </summary>
        [Test]
        public void Sea_Level_Change_Uses_River_Channel_And_Never_Reruns_Noise()
        {
            // ① 纯表判据
            Assert.AreEqual(DirtyBits.River, IntentChangePlanner.DirtyOf(IntentKind.RiverStroke),
                "RiverStroke 的直接脏位必须恰为 River");
            var exp = IntentChangePlanner.Expand(DirtyBits.River);
            Assert.AreEqual(0, (int)(exp & DirtyBits.Terrain),
                "改水展开后<b>不得</b>含 Terrain ⇒ 否则重跑噪声基底（用户会立刻感到卡）");
            Assert.AreEqual(0, (int)(exp & (DirtyBits.Seed | DirtyBits.World)),
                "改水不得触及 Seed/World（那会命中「⇒ All」首行）");
            Assert.IsTrue((exp & DirtyBits.Flatten) != 0,
                "水界参与平整约束 ⇒ Flatten 必须在闭包里");

            // ② S2 必须认领 Flatten：否则"地表变了水面不跟着变" ⇒ 水悬在半坡
            var claims = StageClaims.Of(StageId.S2_River);
            Assert.IsTrue((claims & DirtyBits.Flatten) != 0,
                "S2 必须认领 Flatten，否则违背用户裁定③（判据用最终生效地表）");
            Assert.IsTrue((claims & DirtyBits.River) != 0);

            // ③ 实例侧：一次改动只 bump 一次；同值/非法值不 bump
            var proj = ScriptableObject.CreateInstance<CityProject>();
            _track[7] = proj;
            proj.EnsureRegistered();
            var v0 = proj.ManualVersion;
            Assert.AreEqual(0f, proj.SeaLevel, 1e-6f, "海平面默认必须是 0（用户指定）");
            Assert.IsTrue(proj.SetSeaLevel(6f));
            Assert.AreEqual(1, (int)(proj.ManualVersion - v0), "一次改动只 bump 一次");
            Assert.IsFalse(proj.SetSeaLevel(6f), "重复设同值不得 bump（防空重算风暴）");
            Assert.IsFalse(proj.SetSeaLevel(float.PositiveInfinity), "无穷必须拒收（会让全部比较失效、水面无声消失）");
            Assert.IsFalse(proj.SetSeaLevel(float.NaN), "NaN 必须拒收");
            Assert.AreEqual(6f, proj.SeaLevel, 1e-4f, "非法值不得污染已存的合法值");

            // 本测试网格是 2m/格（CityProject 默认缓冲 10m）⇒ CeilToInt(10/2)=5 格。
            // 我第一版按正式档 4m/格写成 3 ⇒ 期望值错、实现无错（自查纠正）。
            Assert.AreEqual(5, proj.WaterBufferCells(_grid), "缓冲 10m @2m 格 ⇒ 向上取整 5 格");
            Assert.IsTrue(proj.SetWaterBuffer(0f));
            Assert.AreEqual(0, proj.WaterBufferCells(_grid), "缓冲可归零 = 不设带");
        }

        /// <summary>清理派生位时<b>不得</b>动 NoWater（那是 S0 的意图位；Road 等其余位同样不得被波及）。</summary>
        [Test]
        public void ClearWaterMask_Keeps_Other_Intent_Bits()
        {
            var mask = new ushort[_grid.CellCount];
            mask[0] = (ushort)(CellFlag.Water | CellFlag.WaterBuffer | CellFlag.NoWater);
            mask[1] = (ushort)(CellFlag.Road | CellFlag.NoWater);
            mask[2] = (ushort)(CellFlag.Water | CellFlag.Buildable);
            WaterFlood.ClearWaterMask(mask);
            Assert.AreEqual((ushort)CellFlag.NoWater, mask[0], "Water/Buffer 清掉，NoWater 原样留下");
            Assert.AreEqual((ushort)(CellFlag.Road | CellFlag.NoWater), mask[1],
                "Road 与其余位不得被水的清理波及");
            Assert.AreEqual((ushort)CellFlag.Buildable, mask[2]);
        }

        // ─────────────────── ⑤ 距离场与"无水也是合法态" ───────────────────

        /// <summary>距离场：水格 0；沿远离方向单调不减；远处得到相应量级值（不是 NaN/∞）。</summary>
        [Test]
        public void Distance_Field_Is_Monotone_And_Finite()
        {
            var w = NewWater();
            w.AddSeed(3, 20, WaterSeed.NoLocalLevel, out _);
            var h = new float[_grid.CellCount];
            for (var i = 0; i < h.Length; i++) h[i] = (i % N) < 5 ? 1f : 20f;   // 最左 5 列低地

            Bufs(_grid, out var mask, out var queue, out var visited);
            WaterFlood.Flood(w, _grid, h, mask, 5f, queue, visited, out _);
            Assert.IsTrue(IsWater(mask, 3, 20));

            var dist = new float[_grid.CellCount];
            WaterFlood.BuildDistanceField(mask, _grid, dist);

            Assert.AreEqual(0f, dist[20 * N + 3], 1e-4f, "水格自身距离必须为 0");
            var prev = 0f;
            for (var cx = 4; cx < N; cx++)
            {
                var d = dist[20 * N + cx];
                Assert.IsTrue(float.IsFinite(d), $"第 {cx} 列距离不得为 NaN/∞");
                Assert.GreaterOrEqual(d, prev - 1e-4f, $"远离水必须单调不减（cx={cx}: {prev}→{d}）");
                prev = d;
            }
            Assert.Greater(dist[20 * N + (N - 1)], 10f * Cell,
                "离水 10 格以上必须给出相应量级（恒为小值说明两遍扫描漏了传播）");
        }

        /// <summary>
        /// <b>没有种子就没有水</b>：水不是"凡低处自动成水"，必须有用户意图。
        /// 这条把定义的第二半（"连通"）与前半（"低于水位"）分开钉住。
        /// </summary>
        [Test]
        public void Empty_Seed_List_Produces_No_Water_Even_Where_Terrain_Is_Below_Level()
        {
            var w = NewWater();
            Bufs(_grid, out var mask, out var queue, out var visited);
            var h = Flat(_grid.CellCount, -5f);                     // 全图都远低于海平面
            var cells = WaterFlood.Flood(w, _grid, h, mask, 0f, queue, visited, out var active);
            Assert.AreEqual(0, cells);
            Assert.AreEqual(0, active);
            var any = 0;
            for (var i = 0; i < mask.Length; i++)
                if ((mask[i] & (ushort)CellFlag.Water) != 0) any++;
            Assert.AreEqual(0, any, "没有种子就没有水：低洼不会自动成河（否则改一次噪声参数就凭空长出湖）");
        }

        /// <summary>禁用种子 ⇒ 水完全退去，但数据仍在（"移除水"的另一半语义，不删用户的东西）。</summary>
        [Test]
        public void Disabling_A_Seed_Retreats_Its_Water_Without_Deleting_Data()
        {
            var h = new float[_grid.CellCount];
            for (var i = 0; i < h.Length; i++) h[i] = (i / N) > 20 ? 20f : 1f;
            var w = NewWater();
            w.AddSeed(10, 10, WaterSeed.NoLocalLevel, out var id);

            Bufs(_grid, out var mask, out var queue, out var visited);
            Assert.Greater(WaterFlood.Flood(w, _grid, h, mask, 5f, queue, visited, out _), 0);

            Assert.IsTrue(w.SetSeedEnabled(id, false));
            WaterFlood.ClearWaterMask(mask);
            Assert.AreEqual(0, WaterFlood.Flood(w, _grid, h, mask, 5f, queue, visited, out var active2));
            Assert.AreEqual(0, active2, "关掉种子后一滴水都不该留下");
            Assert.AreEqual(1, w.Count, "但种子数据仍在（可再勾回）");
        }

        /// <summary>越界的种子格必须被忽略（不崩、也不写水），且不影响同列表内其他种子。</summary>
        [Test]
        public void Out_Of_Bounds_Seed_Is_Ignored_Without_Corrupting_Others()
        {
            var w = NewWater();
            w.AddSeed(-5, 1000, WaterSeed.NoLocalLevel, out _);         // 越界
            w.AddSeed(10, 10, WaterSeed.NoLocalLevel, out _);           // 合法
            Bufs(_grid, out var mask, out var queue, out var visited);
            var cells = WaterFlood.Flood(w, _grid, Flat(_grid.CellCount, 1f), mask,
                                         5f, queue, visited, out var active);
            Assert.AreEqual(1, active, "只有合法种子产水");
            Assert.AreEqual(_grid.CellCount, cells, "合法那颗应淹满全图低地");
        }
    }
}
