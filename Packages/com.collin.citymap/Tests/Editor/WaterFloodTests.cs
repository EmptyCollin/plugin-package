using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Collin.CityMap.Tests
{
    /// <summary>
    /// 水体（02 <b>第二次改版</b>：水 = 种子水面高程以下、扩散半径以内的连通域）核心判据。
    /// <para>用户 2026-11-05 裁定的五条在这里逐条钉住：
    /// ①每颗种子自带水面高程（<b>没有全局海平面</b>）；②4 邻接（不是 8 邻接）；
    /// ③判据用最终生效地表；④结果取多种子并集；⑤<b>扩散半径</b>兜住"点山脊淹掉整个山谷"。</para>
    /// <para>纪律同本包其他测试：断言用<b>独立参考实现或解析事实</b>，不自反调一遍被测代码；
    /// 每条"通过"都配一条能证明它<b>真的会失败</b>的对照。</para>
    /// </summary>
    public class WaterFloodTests
    {
        const float Cell = 2f;
        const int N = 40;                       // 40×40 格 @2m = 80m 见方，小到能手写期望值

        CityGrid _grid;
        readonly List<Object> _track = new List<Object>(32);

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
            for (var i = _track.Count - 1; i >= 0; i--)
                if (_track[i] != null) Object.DestroyImmediate(_track[i], true);
            _track.Clear();
        }

        WaterNetworkAsset NewWater()
        {
            var w = ScriptableObject.CreateInstance<WaterNetworkAsset>();
            _track.Add(w);
            return w;
        }

        /// <summary>不限半径的种子（做"整片低地都淹"这类期望时用它）。</summary>
        void AddUnlimited(WaterNetworkAsset w, int cx, int cz, float level)
            => w.AddSeed(cx, cz, level, 0f, out _);

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
        /// 只有<b>严格低于水面高程</b>的格成水。等于不算：水面与地面齐平时那是水陆交界，
        /// 判成水会让"刚好填到水面"的高地莫名被淹。
        /// </summary>
        [Test]
        public void Flood_Marks_Only_Cells_Strictly_Below_Seed_Level()
        {
            var w = NewWater();
            AddUnlimited(w, 20, 20, 5f);
            Bufs(_grid, out var mask, out var queue, out var visited);

            var h = Flat(_grid.CellCount, 10f);
            for (var cz = 15; cz <= 25; cz++)
                for (var cx = 15; cx <= 25; cx++)
                    h[cz * _grid.Columns + cx] = 1f;

            var cells = WaterFlood.Flood(w, _grid, h, mask, queue, visited, out var active);
            Assert.Greater(active, 0, "种子在低地，必须产水");
            Assert.AreEqual(11 * 11, cells, "低地 11×11 必须全淹、且不得越出到 10m 高地");
            Assert.IsTrue(IsWater(mask, 20, 20));
            Assert.IsFalse(IsWater(mask, 20, 10), "高地（10m > 水面 5m）不得成水");

            // 恰好等于水面高程：一滴水都没有
            Bufs(_grid, out var m2, out var q2, out var v2);
            var c2 = WaterFlood.Flood(w, _grid, Flat(_grid.CellCount, 5f), m2, q2, v2, out var a2);
            Assert.AreEqual(0, a2, "全图高程==水面 ⇒ 等于不算低于");
            Assert.AreEqual(0, c2);
        }

        /// <summary>
        /// <b>扩散半径限的是洪泛轮数</b>（纪律⑤，也是本次改版换来"点哪里都不用问"的那道刹车）。
        /// <para>期望值走解析事实：4 邻接 BFS 走 r 轮覆盖的是<b>曼哈顿距离 ≤ r 的菱形</b>，
        /// 格数 = 2r²+2r+1（r=3 ⇒ 1+4+8+12 = 25）。
        /// ⚠️ 我第一版按方窗 (2r+1)²=49 写期望 ⇒ 红。那是<b>8 邻接</b>的形状（缓冲带才用它），
        /// 洪泛按纪律①是 4 邻接 —— 又一次"不回到定义就写期望值"（H-67 同族）。</para>
        /// <para>对照必须做：同一地形、半径 0（不限）⇒ 必须淹满全图。缺了对照，"25"
        /// 可能只是因为半径实现根本没生效。</para>
        /// </summary>
        [Test]
        public void Radius_Limits_Flood_Rounds_And_Unlimited_Spreads_Farther()
        {
            const int R = 3;
            var h = Flat(_grid.CellCount, 1f);                       // 全图低地：能淹的地方一律够低

            Bufs(_grid, out var maskR, out var qR, out var vR);
            var limited = NewWater();
            limited.AddSeed(20, 20, 5f, R * Cell, out _);            // 6m @2m 格 = 3 轮
            var cellsR = WaterFlood.Flood(limited, _grid, h, maskR, qR, vR, out _);
            Assert.AreEqual(2 * R * R + 2 * R + 1, cellsR,
                $"半径 {R} 轮 ⇒ 必须是 4 邻菱形 2r²+2r+1 = 25 格，不得更远");
            Assert.IsTrue(IsWater(maskR, 20 + R, 20), "半径边界上那格该淹（正好 r 步）");
            Assert.IsFalse(IsWater(maskR, 20 + R + 1, 20), "r+1 步必须是岸");

            // 防恒真对照：同地形、半径 0（不限）⇒ 全图都淹
            Bufs(_grid, out var maskU, out var qU, out var vU);
            var free = NewWater();
            AddUnlimited(free, 20, 20, 5f);
            var cellsU = WaterFlood.Flood(free, _grid, h, maskU, qU, vU, out _);
            Assert.AreEqual(_grid.CellCount, cellsU,
                "半径 0 = 不限 ⇒ 同一片低地必须淹满（否则上面那条 25 是半径没生效造成的假绿）");
        }

        /// <summary>
        /// 半径限<b>洪泛步数</b>（= 4 邻接的曼哈顿距离），<b>不是欧氏直线距离</b>。
        /// <para>为什么这条值得单独钉：按欧氏圆裁剪是"看起来更自然"的写法，但它会让水在河湾处
        /// 比实际能走到的位置<b>更远</b>地停住 —— 弯河道下游会被错误截断。实现按轮数展开，
        /// 所以"欧氏近但步数远"的格必须<b>不</b>成水。</para>
        /// <para>三条断言互相兜住（全图同高、无障碍，故步数就是曼哈顿距离，期望值可手算）：
        /// ① 步数 10（曼哈顿）在半径上 ⇒ 淹；② 欧氏 7.6 但步数 10 ⇒ 也淹（说明不是按欧氏排除）；
        /// ③ <b>欧氏 8.5 &lt; 10 但步数 12 &gt; 10 ⇒ 不淹</b>（这才是"步数不是直线"的正面证据）。</para>
        /// </summary>
        [Test]
        public void Radius_Counts_Flood_Steps_Not_Straight_Line_Distance()
        {
            const int R = 10;
            const int sx = 20, sz = 20;
            var h = Flat(_grid.CellCount, 1f);                       // 全图低地 ⇒ 只有半径能挡住水

            var w = NewWater();
            w.AddSeed(sx, sz, 5f, R * Cell, out _);
            Bufs(_grid, out var mask, out var queue, out var visited);
            WaterFlood.Flood(w, _grid, h, mask, queue, visited, out _);

            Assert.IsTrue(IsWater(mask, sx + R, sz), "正东 10 步（恰在半径上）必须成水");
            Assert.IsTrue(IsWater(mask, sx + 7, sz + 3),
                "欧氏 7.6、步数 10 ⇒ 必须成水（若实现按欧氏圆裁剪，这条与下一条会一起绿，就没法分辨）");
            Assert.IsFalse(IsWater(mask, sx + 6, sz + 6),
                "欧氏 8.49 < 半径 10，但步数 12 > 10 ⇒ 必须<b>不</b>成水：半径限的是步数不是直线距离");
            Assert.IsTrue(IsWater(mask, sx + 6, sz + 4),
                "对照：同方向族里步数 10 的那格必须淹 ⇒ 上面那条 False 是真的被步数挡住");
        }

        /// <summary>
        /// <b>4 邻接，绝不对角泄漏</b>（纪律①）。
        /// <para>这是本定义里最容易写错、后果最坏的一条：8 邻接会让两片被<b>对角山脊</b>隔开的湖
        /// 从角缝"渗"成一片，现象是"我挖的两个湖自己合并了"，且只在角部相邻时发生、极难归因。</para>
        /// </summary>
        [Test]
        public void Flood_Uses_4_Neighbors_And_Never_Leaks_Through_Diagonal_Ridge()
        {
            var w = NewWater();
            AddUnlimited(w, 5, 8, 5f);                              // 种子在对角山脊的左下侧
            Bufs(_grid, out var mask, out var queue, out var visited);

            var h = Flat(_grid.CellCount, 1f);                       // 全图低地
            for (var d = 0; d < N; d++) h[d * _grid.Columns + d] = 100f;   // 主对角山脊（cx==cz）

            WaterFlood.Flood(w, _grid, h, mask, queue, visited, out _);

            Assert.IsTrue(IsWater(mask, 5, 8), "种子所在侧必须成水");
            Assert.IsTrue(IsWater(mask, 3, 30), "山脊左下侧（cx<cz，4 邻可达）必须被淹");
            Assert.IsTrue(IsWater(mask, 20, 35), "左下侧深处同样可达");
            Assert.IsFalse(IsWater(mask, 30, 3),
                "山脊右上侧（cx>cz）只能穿<b>对角角缝</b>抵达 ⇒ 4 邻接下必须<b>无</b>水");

            // 防恒真对照：山脊开一个 4 邻缺口 ⇒ 另一侧必须被淹
            Bufs(_grid, out var m2, out var q2, out var v2);
            var h2 = (float[])h.Clone();
            h2[12 * _grid.Columns + 12] = 1f;
            WaterFlood.Flood(w, _grid, h2, m2, q2, v2, out _);
            Assert.IsTrue(IsWater(m2, 30, 3),
                "山脊开了 4 邻缺口后水必须贯通到另一侧（否则上面那条「不成水」是空过的）");
        }

        // ─────────────── ② 种子自带高程＝高原湖天然成立 ───────────────

        /// <summary>
        /// 高原天池：整片地形都远高于 0m 时也能有水 —— 因为<b>没有全局基准</b>了，
        /// 种子自己的高程就是判据（这正是本次删掉海平面后交互退化成"点一下就出水"的依据）。
        /// </summary>
        [Test]
        public void Plateau_Basin_Holds_Water_With_No_Global_Reference()
        {
            var cx = N / 2; var cz = N / 2;
            var h = Flat(_grid.CellCount, 200f);                     // 高原
            for (var z = cz - 6; z <= cz + 6; z++)
                for (var x = cx - 6; x <= cx + 6; x++)
                    h[z * _grid.Columns + x] = 150f;                 // 盆

            Bufs(_grid, out var mask, out var queue, out var visited);
            var w = NewWater();
            w.AddSeed(cx, cz, 160f, 0f, out _);                      // 水面高程 160m，不限半径
            var cells = WaterFlood.Flood(w, _grid, h, mask, queue, visited, out var active);
            Assert.Greater(active, 0, "种子在高原盆底，必须成湖");
            Assert.AreEqual(13 * 13, cells, "湖只淹盆内 13×13，不得漫过 200m 盆沿");
            Assert.IsFalse(IsWater(mask, cx + 10, cz), "盆沿外（200m > 160m）不该有水");
        }

        /// <summary>
        /// 点在山脊上时，<b>默认小半径</b>不会让水顺坡淹掉整个山谷（本次改版唯一的风险，由半径兜住）。
        /// <para>地形是一条从山脊单调下坡到谷底的路径：只判"低于高程"会一路淹到底。</para>
        /// </summary>
        [Test]
        public void Default_Radius_Stops_Ridge_Seed_From_Drowning_The_Whole_Valley()
        {
            var h = new float[_grid.CellCount];
            for (var cz = 0; cz < N; cz++)
                for (var cx = 0; cx < N; cx++)
                    h[cz * _grid.Columns + cx] = 100f - 2f * cx;     // 沿 cx 单调下坡到 22m

            Bufs(_grid, out var mask, out var queue, out var visited);
            var w = NewWater();
            w.AddSeed(0, 20, 100f + 4f, 4 * Cell, out _);            // 山脊顶点，水深 4m、半径 4 格
            WaterFlood.Flood(w, _grid, h, mask, queue, visited, out _);

            Assert.IsTrue(IsWater(mask, 0, 20), "种子自己那格必须成水（天池）");
            Assert.IsTrue(IsWater(mask, 4, 20), "半径内（4 步）的坡地该淹");
            Assert.IsFalse(IsWater(mask, 20, 20),
                "坡下远处（直线与步数都远在半径外）必须不被淹 ⇒ 点山脊不得淹掉整个山谷");
            Assert.IsFalse(IsWater(mask, 39, 20), "谷底不得被淹");
        }

        /// <summary>
        /// 多种子各自按<b>自己的</b>水面高程扩展：低水位种子淹过的格子，高水位种子必须能<b>越过它</b>继续扩。
        /// <para>存在理由：我第一版拿掩码的 Water 位当 visited，低水位先淹的格会挡住高水位种子向外扩展
        /// （"湖里有高地基、四周各淹各的"直接算错）。这条断言专门钉它。</para>
        /// </summary>
        [Test]
        public void Higher_Seed_Can_Spread_Past_Cells_Already_Wet_From_Lower_Seed()
        {
            // 阶梯：高程只随 cx 递增 ⇒ 水面高程决定东界（h = 10 + 2·cx）
            var h = new float[_grid.CellCount];
            for (var cz = 0; cz < N; cz++)
                for (var cx = 0; cx < N; cx++)
                    h[cz * _grid.Columns + cx] = 10f + 2f * cx;

            // 只有低水位种子时：东界 cx≤1
            Bufs(_grid, out var mA, out var qA, out var vA);
            var wA = NewWater();
            AddUnlimited(wA, 1, 5, 13f);
            WaterFlood.Flood(wA, _grid, h, mA, qA, vA, out var aA);
            Assert.AreEqual(1, aA);
            Assert.IsTrue(IsWater(mA, 1, 5));
            Assert.IsFalse(IsWater(mA, 5, 25), "单种子对照：低水位 13 不该淹到 cx=5（h=20）");

            // 加一个高水位种子（放在已被 A 淹过的列上）⇒ 必须能向东越过 cx=1 继续扩到 cx≤5
            Bufs(_grid, out var mB, out var qB, out var vB);
            var lvlB = new float[_grid.CellCount];
            for (var i = 0; i < lvlB.Length; i++) lvlB[i] = float.NegativeInfinity;
            var wB = NewWater();
            AddUnlimited(wB, 1, 5, 13f);
            AddUnlimited(wB, 1, 25, 21f);
            WaterFlood.Flood(wB, _grid, h, mB, qB, vB, out var aB, lvlB);
            Assert.AreEqual(2, aB, "两个种子都在各自水面高程之下，都必须产水");
            Assert.IsTrue(IsWater(mB, 5, 25),
                "高水位种子必须淹到 cx=5（20<21）——即使 cx≤1 已被低水位种子先淹过");
            Assert.IsFalse(IsWater(mB, 6, 25), "但不得越过自己的水面界（22>21）");

            // 重叠格的水面高程必须<b>取更高那片</b>：cx=1 同时被 13 与 21 覆盖 ⇒ 必须是 21
            Assert.AreEqual(21f, lvlB[5 * _grid.Columns + 1], 1e-4f,
                "多种子重叠格的水面高程必须取更高值（画低了会被地形埋掉）");
        }

        /// <summary>
        /// 重新洪泛<b>必须与上一轮无关</b>。visited 用 generation 标记，若不清零就会把上一轮的
        /// 标记当"本轮已访问" ⇒ 表现为<b>第二次洪泛后水面莫名缩小</b>（清水掩码不等于清访问标记）。
        /// </summary>
        [Test]
        public void Reflood_Produces_Identical_Area_Without_Stale_Visited_Marks()
        {
            var w = NewWater();
            AddUnlimited(w, 10, 10, 5f);
            var h = Flat(_grid.CellCount, 9f);
            for (var cz = 0; cz < 20; cz++)
                for (var cx = 0; cx < 20; cx++)
                    h[cz * _grid.Columns + cx] = 1f;                 // 左下 20×20 低地

            Bufs(_grid, out var mask, out var queue, out var visited);
            var first = WaterFlood.Flood(w, _grid, h, mask, queue, visited, out var a1);
            Assert.AreEqual(20 * 20, first, "前提：首轮淹了 20×20");

            // 只清水掩码、复用同一批 visited ⇒ 必须与首轮完全一致
            WaterFlood.ClearWaterMask(mask);
            var second = WaterFlood.Flood(w, _grid, h, mask, queue, visited, out var a2);
            Assert.AreEqual(first, second, "重跑洪泛面积必须逐格相同（visited 未清零就会偏小）");
            Assert.AreEqual(a1, a2);
        }

        // ─────────────────── ③ 负约束与缓冲带 ───────────────────

        /// <summary><see cref="CellFlag.NoWater"/>（S0 烘焙的禁水负约束）必须<b>挡住洪水</b>。</summary>
        [Test]
        public void NoWater_Flag_Blocks_Flood_From_Crossing()
        {
            var w = NewWater();
            AddUnlimited(w, 5, 20, 5f);
            var h = Flat(_grid.CellCount, 1f);                       // 全图低地

            Bufs(_grid, out var mask, out var queue, out var visited);
            for (var cz = 0; cz < N; cz++) mask[cz * _grid.Columns + 12] |= (ushort)CellFlag.NoWater;
            WaterFlood.Flood(w, _grid, h, mask, queue, visited, out _);

            Assert.IsTrue(IsWater(mask, 5, 20));
            Assert.IsTrue(IsWater(mask, 11, 20), "墙左侧（4 邻可达）必须成水");
            Assert.IsFalse(IsWater(mask, 12, 20), "禁水格本身不得成水");
            Assert.IsFalse(IsWater(mask, 13, 20), "墙右侧虽同为低地，但只能穿墙抵达 ⇒ 不得成水");

            // 防恒真对照：墙开一个缺口 ⇒ 右侧必须进水
            Bufs(_grid, out var m2, out var q2, out var v2);
            m2[20 * _grid.Columns + 12] = 0;
            WaterFlood.Flood(w, _grid, h, m2, q2, v2, out _);
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
                    h[cz * _grid.Columns + cx] = 20f;                // 水中的"岛"，让形状不规则

            var w = NewWater();
            AddUnlimited(w, 10, 14, 5f);
            Bufs(_grid, out var mask, out var queue, out var visited);
            WaterFlood.Flood(w, _grid, h, mask, queue, visited, out var active);
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
            AddUnlimited(w, 5, 20, 5f);
            var h = Flat(_grid.CellCount, 1f);
            Bufs(_grid, out var mask, out var queue, out var visited);
            for (var cz = 0; cz < N; cz++)
            {
                mask[cz * _grid.Columns + 12] |= (ushort)CellFlag.NoWater;
                mask[cz * _grid.Columns + 13] |= (ushort)CellFlag.NoWater;
            }
            WaterFlood.Flood(w, _grid, h, mask, queue, visited, out _);
            var added = WaterFlood.ApplyBuffer(mask, _grid, 1, queue);

            Assert.Greater(added, 0, "对照前提：墙前的水边必须产生缓冲带");
            Assert.IsFalse((mask[20 * _grid.Columns + 13] & (ushort)CellFlag.WaterBuffer) != 0,
                "距水 2 格处（R=1）不得被缓冲带污染");
            Assert.IsFalse((mask[20 * _grid.Columns + 14] & (ushort)CellFlag.WaterBuffer) != 0,
                "墙后更远处不得被缓冲带污染");
        }

        // ─────────────────── ④ 意图层与脏通道 ───────────────────

        /// <summary>种子列表语义：同格覆盖而非追加、目标被占时拒绝移动、非法值拒收、指纹认内容不认 id。</summary>
        [Test]
        public void Seed_List_Overwrites_Rejects_Invalid_And_Fingerprints_Content()
        {
            var w = NewWater();
            Assert.IsTrue(w.IsEmpty);

            // 同格覆盖
            Assert.IsTrue(w.AddSeed(30, 30, 9f, 100f, out var idX), "新格算新增");
            Assert.IsFalse(w.AddSeed(30, 30, 5f, 20f, out var idX2), "同格再点必须<b>覆盖</b>而非追加");
            Assert.AreEqual(idX, idX2, "覆盖必须沿用原 id（否则面板出现两条看不出差别的条目）");
            Assert.AreEqual(1, w.Count);
            Assert.IsTrue(w.TryGet(idX, out var overwritten));
            Assert.AreEqual(5f, overwritten.level, 1e-4f, "覆盖必须真的写进高程");
            Assert.AreEqual(20f, overwritten.radiusMeters, 1e-4f, "覆盖必须真的写进半径");

            // 移动与冲突
            w.AddSeed(4, 4, 9f, 0f, out var id1);
            w.AddSeed(6, 6, 9f, 0f, out var id2);
            Assert.AreEqual(3, w.Count);
            Assert.IsTrue(w.MoveSeed(id1, 7, 7));
            Assert.IsFalse(w.MoveSeed(id2, 7, 7), "目标格已被占 ⇒ 拒绝");
            Assert.IsTrue(w.TryGet(id1, out var s1));
            Assert.AreEqual(7, s1.cx);

            // 水面高程：非法值必须拒收（脏值会让"低于高程"比较全废、水面无声消失且查不出原因）
            Assert.IsFalse(w.SetSeedLevel(id1, 9f), "同值重复设不算改动");
            Assert.IsTrue(w.SetSeedLevel(id1, 33f), "真改值必须判为改动");
            Assert.IsFalse(w.SetSeedLevel(id1, float.NaN), "NaN 必须拒收");
            Assert.IsFalse(w.SetSeedLevel(id1, float.PositiveInfinity), "无穷必须拒收");
            Assert.IsTrue(w.TryGet(id1, out var kept));
            Assert.AreEqual(33f, kept.level, 1e-4f, "非法值不得污染已存的合法值");

            // 半径：0 = 不限；负数按 0 处理；NaN/无穷拒收
            Assert.IsTrue(w.SetSeedRadius(id1, 120f), "从无限制改成有限制必须算改动");
            Assert.IsTrue(w.SetSeedRadius(id1, 0f), "改回不限（0）也必须算改动");
            Assert.IsFalse(w.SetSeedRadius(id1, 0f), "已经是 0 再设 0 不算改动（防空重算风暴）");
            Assert.IsFalse(w.SetSeedRadius(id1, -5f), "负数按 0 处理 ⇒ 与当前值等 ⇒ 不算改动");
            Assert.IsFalse(w.SetSeedRadius(id1, float.NaN), "NaN 必须拒收");

            // 指纹只认内容：删一个再原样加回 ⇒ 指纹必须相同（id 不同）。
            // ⚠️ keyBefore 必须取在<b>删除之前</b>：取在删除之后就变成"3 个 vs 2 个"，测的是数量而非身份。
            var keyBefore = w.ComputeValueKey();
            Assert.IsTrue(w.RemoveSeed(id2));
            Assert.AreNotEqual(keyBefore, w.ComputeValueKey(), "少一个种子，指纹必须变");
            Assert.IsTrue(w.AddSeed(6, 6, 9f, 0f, out var id2b));
            Assert.AreNotEqual(id2, id2b, "新种子拿新 id（身份是新生成的）");
            Assert.AreEqual(keyBefore, w.ComputeValueKey(),
                "内容相同 ⇒ 指纹必须相同（id 是身份不是内容，算进指纹会让『重新加回』被当成改动）");

            // 半径也必须进指纹：它是可调内容，漏掉会让"只改半径"不触发重算（＝改了没反应）
            var keyR = w.ComputeValueKey();
            Assert.IsTrue(w.SetSeedRadius(id2b, 50f), "改半径判为改动");
            Assert.AreNotEqual(keyR, w.ComputeValueKey(), "半径未进指纹 ⇒ 面板改了水面不动（本次改版要防的那类）");

            Assert.IsTrue(w.RemoveSeed(idX));
            Assert.IsFalse(w.RemoveSeed(idX), "重复删除必须返回 false");
            Assert.IsTrue(w.SetSeedEnabled(id1, false));
            Assert.IsFalse(w.SetSeedEnabled(id1, false), "重复禁用不算改动");
        }

        /// <summary><see cref="WaterSeed.RadiusCells"/> 的换算口径：向上取整、0/负/NaN = 不限。</summary>
        [Test]
        public void Radius_Cells_Converts_Up_And_Treats_NonPositive_As_Unlimited()
        {
            Assert.AreEqual(3, new WaterSeed { radiusMeters = 5f }.RadiusCells(2f),
                "5m @2m 格 ⇒ 向上取整 3 格（宁多不一格漏，与缓冲带同口径）");
            Assert.AreEqual(3, new WaterSeed { radiusMeters = 6f }.RadiusCells(2f), "整除时就是 3");
            Assert.AreEqual(1, new WaterSeed { radiusMeters = 0.5f }.RadiusCells(2f),
                "半径小于格宽时不得归零（归零会被判成「不限」，把山谷淹掉）");

            Assert.AreEqual(int.MaxValue, new WaterSeed { radiusMeters = 0f }.RadiusCells(2f), "0 = 不限");
            Assert.AreEqual(int.MaxValue, new WaterSeed { radiusMeters = -3f }.RadiusCells(2f), "负 = 不限");
            Assert.AreEqual(int.MaxValue, new WaterSeed { radiusMeters = float.NaN }.RadiusCells(2f),
                "NaN 必须走不限：CeilToInt(NaN) 不可依赖，否则会算出一个巨大值把整图淹掉");
            Assert.AreEqual(int.MaxValue, new WaterSeed { radiusMeters = 10f }.RadiusCells(0f),
                "格宽非法时不得除零");
        }

        /// <summary>
        /// <b>改水相关参数绝不允许重跑噪声基底</b>（只动水面与下游）。
        /// <para>若走 WorldParam 会命中传播表首行「Seed|World ⇒ All」⇒ S1a 重跑
        /// （M4-j 实测 4860ms）。这条断言钉的是<b>通道选择</b>这个决策本身。</para>
        /// </summary>
        [Test]
        public void Water_Changes_Use_River_Channel_And_Never_Rerun_Noise()
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
                "S2 必须认领 Flatten，否则违背纪律②（判据用最终生效地表）");
            Assert.IsTrue((claims & DirtyBits.River) != 0);

            // ③ 项目侧（缓冲带仍是全局参数）：一次改动只 bump 一次；同值/非法值不 bump
            var proj = ScriptableObject.CreateInstance<CityProject>();
            _track.Add(proj);
            proj.EnsureRegistered();
            var v0 = proj.ManualVersion;
            Assert.IsTrue(proj.SetWaterBuffer(6f));
            Assert.AreEqual(1, (int)(proj.ManualVersion - v0), "一次改动只 bump 一次");
            Assert.IsFalse(proj.SetWaterBuffer(6f), "重复设同值不得 bump（防空重算风暴）");
            Assert.IsFalse(proj.SetWaterBuffer(float.PositiveInfinity), "无穷必须拒收");
            Assert.IsFalse(proj.SetWaterBuffer(float.NaN), "NaN 必须拒收");
            Assert.AreEqual(6f, proj.WaterBufferMeters, 1e-4f, "非法值不得污染已存的合法值");

            // 本测试网格是 2m/格 ⇒ CeilToInt(6/2)=3 格。
            // （上一版我按正式档 4m/格写期望值，撞红过一次：期望值必须现算，不能凭档位记忆。）
            Assert.AreEqual(3, proj.WaterBufferCells(_grid), "缓冲 6m @2m 格 ⇒ 向上取整 3 格");
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
            AddUnlimited(w, 3, 20, 5f);
            var h = new float[_grid.CellCount];
            for (var i = 0; i < h.Length; i++) h[i] = (i % N) < 5 ? 1f : 20f;   // 最左 5 列低地

            Bufs(_grid, out var mask, out var queue, out var visited);
            WaterFlood.Flood(w, _grid, h, mask, queue, visited, out _);
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
        /// 这条把定义的第二半（"连通"）与前半（"低于水面高程"）分开钉住。
        /// </summary>
        [Test]
        public void Empty_Seed_List_Produces_No_Water_Even_Where_Terrain_Is_Low()
        {
            var w = NewWater();
            Bufs(_grid, out var mask, out var queue, out var visited);
            var cells = WaterFlood.Flood(w, _grid, Flat(_grid.CellCount, -5f), mask,
                                        queue, visited, out var active);
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
            AddUnlimited(w, 10, 10, 5f);
            w.AddSeed(10, 12, 5f, 0f, out var id);

            Bufs(_grid, out var mask, out var queue, out var visited);
            Assert.Greater(WaterFlood.Flood(w, _grid, h, mask, queue, visited, out _), 0);

            Assert.IsTrue(w.SetSeedEnabled(id, false));
            WaterFlood.ClearWaterMask(mask);
            var second = NewWater();
            second.AddSeed(10, 12, 5f, 0f, out var disabled);
            second.SetSeedEnabled(disabled, false);
            Assert.AreEqual(0, WaterFlood.Flood(second, _grid, h, mask, queue, visited, out var active2));
            Assert.AreEqual(0, active2, "关掉种子后一滴水都不该留下");
            Assert.AreEqual(1, second.Count, "但种子数据仍在（可再勾回）");
            Assert.AreEqual(5f, second[0].level, 1e-4f, "禁用不改变参数");
        }

        /// <summary>越界的种子格必须被忽略（不崩、也不写水），且不影响同列表内其他种子。</summary>
        [Test]
        public void Out_Of_Bounds_Seed_Is_Ignored_Without_Corrupting_Others()
        {
            var w = NewWater();
            AddUnlimited(w, -5, 1000, 5f);                           // 越界
            AddUnlimited(w, 10, 10, 5f);                             // 合法
            Bufs(_grid, out var mask, out var queue, out var visited);
            var cells = WaterFlood.Flood(w, _grid, Flat(_grid.CellCount, 1f), mask,
                                         queue, visited, out var active);
            Assert.AreEqual(1, active, "只有合法种子产水");
            Assert.AreEqual(_grid.CellCount, cells, "合法那颗应淹满全图低地");
        }

        // ─────────── ⑥ 记账必须长在种子上（撤销可见的前提，用户反馈⑤） ───────────

        /// <summary>
        /// 水资产<b>自己是意图登记对象</b>，且提交点把脏位记在自己的账本上。
        /// <para>存在理由（用户反馈②「种子变了没刷新」/⑤「撤销不生效」的共同根因）：
        /// 上一版把脏位记在 <see cref="CityTerrain"/>（场景组件）上，而
        /// <c>Undo.RecordObject</c> 还原的是<b>种子资产</b> ⇒ 撤销后数据回去了、组件账本不动 ⇒
        /// Tracker 两条检（通道序列号差 + 集合哈希）全都看不见。</para>
        /// <para>所以这里钉三件事：①资产在登记表里（集合哈希检能覆盖它）；
        /// ②<b>同一序列号跟着数据一起序列化</b>（撤销会还原它）；③零尺寸脏盒必须兜底成可用盒。</para>
        /// </summary>
        [Test]
        public void Water_Asset_Is_Intent_Registered_And_Its_Ledger_Carries_The_Dirty_Channel()
        {
            var w = NewWater();
            Assert.IsFalse(string.IsNullOrEmpty(w.StableId), "资产必须有 stableId（否则登记表里找不到它）");
            Assert.IsNotNull(w.Ledger, "账本不得为 null");
            Assert.IsTrue(IntentRegistry.TryGet(w.StableId, out var found), "资产必须在意图登记表里");
            Assert.AreSame(w, found, "登记表里的必须是同一个对象（否则扫的是别的账本）");

            var s0 = w.Ledger.SequenceOf(IntentKind.RiverStroke);
            w.AddSeed(3, 4, 9f, 10f, out var id);
            w.CommitChange(WaterNetworkAsset.SeedBounds(_grid, 3, 4));
            Assert.Greater(w.Ledger.SequenceOf(IntentKind.RiverStroke), s0,
                "提交点必须把 RiverStroke 记在<b>资产自己的</b>账本上（记在别处撤销就看不见）");

            // 脏盒必须是可用尺寸：零尺寸会被下游当"无脏区"丢掉 ⇒ 静默不重建（H-63 同族）
            var b = w.Ledger.BoundsOf(IntentKind.RiverStroke);
            Assert.Greater(b.size.x, 0f, "种子脏盒尺寸不得为 0");
            Assert.Greater(b.size.z, 0f);

            // 退化输入兜底：传 default 也不能产出零尺寸盒
            w.CommitChange(default);
            var fb = w.Ledger.BoundsOf(IntentKind.RiverStroke);
            Assert.Greater(fb.size.x, 0f, "零尺寸入参必须兜底成最小盒，否则增量重建退化成全图");

            // 账本与种子必须是<b>被 Unity 资产序列化管线收录</b>的字段：
            // Unity Undo 只还原序列化字段，序列号不跟着资产走 ⇒ 撤销还原了数据但扫描器看不见（09 H-9）。
            // ⚠️ 这条断言当场抓出一个<b>数据丢失级</b>缺陷：三张层资产的列表字段当时都写成
            //   `[SerializeField] readonly List<T>`，而 Unity 的序列化管线<b>会跳过 readonly 字段</b>
            //   （带特性也一样跳）⇒ 磁盘上的 .asset 里只有 schemaVersion 一行，种子/雕刻/滤镜内容从来没落过盘。
            //   探针必须用 <c>SerializedObject</c>（走的就是写 YAML 那条管线）；
            //   我先前用 JsonUtility.ToJson 得到同样"字段不见了"的结果，却把现象错误解释成
            //   "JsonUtility 不可靠、实际入库正常"——那是<b>编造解释</b>，真信了就会把 bug 放过去。
            var so = new UnityEditor.SerializedObject(w);
            Assert.IsNotNull(so.FindProperty("seeds"), "种子列表必须进资产序列化，否则重开工程水就没了");
            var ledProp = so.FindProperty("ledger");
            Assert.IsNotNull(ledProp, "账本必须进资产序列化 ⇒ Undo 还原资产时序列号一起回滚");
            Assert.IsNotNull(ledProp.FindPropertyRelative("channels"),
                "通道数组必须逐字段序列化（只序列化账本对象引用等于没序列化）");

            Assert.IsTrue(w.RemoveSeed(id), "清理：用例内的种子删除应成功");
        }

        /// <summary>
        /// 种子与账本必须<b>真的写进磁盘上的 .asset 文本</b>。
        /// <para>存在理由：本轮发现 <c>[SerializeField] readonly List&lt;T&gt;</c> 会被 Unity 序列化管线
        /// <b>整字段跳过</b>，磁盘上的资产只剩 <c>schemaVersion</c> 一行 —— 水种子、雕刻增量、
        /// 平整滤镜三张层资产全部从未真正存过盘（我直接把 .asset cat 出来才看见）。
        /// 上面的 <c>SerializedObject</c> 断言能查"字段有没有被收录"，但查不出"收录了却没写出内容"，
        /// 所以这里断言的是<b>落盘文本</b>本身。</para>
        /// <para>⚠️ <b>不要用"存完再 LoadAssetAtPath 读回来比对"当证据</b>：
        /// 那拿回的是<b>同一个内存实例</b>（Unity 按路径缓存已加载资产），值当然相等 ——
        /// 它只能证明"内存里赋过值"，对是否落盘<b>零信息量</b>，是一眼看着像往返、实则恒真的假测试。
        /// 真要验"重开进程后还在"，只能读文件文本；本包已四次栽在恒真断言上（H-43/H-62/H-67 同族）。</para>
        /// <para>资产落在<b>包内固定探针路径</b>并在 finally 删除（同 D-35 的"固定路径 + LoadAssetAtPath"
        /// 纪律；不用 <c>GenerateUniqueAssetPath</c>，那正是副本滋生的来源）。</para>
        /// </summary>
        [Test]
        public void Seed_And_Ledger_Actually_Write_Into_Asset_Yaml_On_Disk()
        {
            const string path = "Packages/com.collin.citymap/Sample/Sculpt/_persist_probe.asset";
            const float wantLevel = 137.5f;
            const float wantRadius = 250f;
            const int wantCx = 7, wantCz = 9;

            // 清掉可能残留的上轮探针（异常中断会留）
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<WaterNetworkAsset>(path) != null)
                UnityEditor.AssetDatabase.DeleteAsset(path);

            var created = ScriptableObject.CreateInstance<WaterNetworkAsset>();
            try
            {
                created.AddSeed(wantCx, wantCz, wantLevel, wantRadius, out var probeId);
                created.CommitChange(WaterNetworkAsset.SeedBounds(_grid, wantCx, wantCz));
                UnityEditor.EditorUtility.SetDirty(created);
                UnityEditor.AssetDatabase.CreateAsset(created, path);
                UnityEditor.AssetDatabase.SaveAssets();

                var abs = System.IO.Path.GetFullPath(path);
                Assert.IsTrue(System.IO.File.Exists(abs), "资产文件没落盘 = CreateAsset 就没成功");
                var yaml = System.IO.File.ReadAllText(abs);

                // ① 列表本身必须出现在 YAML 里（readonly 写法会让这一行整个消失）
                StringAssert.Contains("seeds:", yaml,
                    "磁盘资产里没有 seeds ⇒ 不入库的字段写法又回来了（本轮已栽过一次，见上方注释）");

                // ② 逐字段核对内容：只看到 "seeds:" 一行不够，空数组也长这样
                StringAssert.Contains($"cx: {wantCx}", yaml, "种子格号没落盘");
                StringAssert.Contains($"cz: {wantCz}", yaml, "种子格号没落盘");
                StringAssert.Contains($"level: {wantLevel}", yaml, "水面高程没落盘：重开工程水的位置会全变");
                StringAssert.Contains($"radiusMeters: {wantRadius}", yaml, "扩散半径没落盘");
                StringAssert.Contains(probeId, yaml,
                    "stableId 必须随种子一起入库，否则面板定位与撤销都会失效");

                // ③ 记账必须跟着数据一起入库：撤销还原资产时序列号才会一起回滚（用户反馈⑤的根因）
                StringAssert.Contains("ledger:", yaml, "账本没进资产 ⇒ Undo 还原数据后扫描器看不见差异");
                StringAssert.Contains("channels:", yaml,
                    "通道数组必须逐字段序列化（只有账本对象、没有 channels，等于序列化了个空壳）");
                StringAssert.Contains("manualVersion:", yaml, "累计版本号必须入库，否则新鲜度判据每次重开都归零");

                // ④ 反例守卫：确认断言真的会红——把内容清空后 YAML 里就不该再有这些行
                //    （不是测实现，是测<b>这条断言本身有没有信息量</b>）
                var empty = ScriptableObject.CreateInstance<WaterNetworkAsset>();
                const string emptyPath = "Packages/com.collin.citymap/Sample/Sculpt/_persist_probe_empty.asset";
                if (UnityEditor.AssetDatabase.LoadAssetAtPath<WaterNetworkAsset>(emptyPath) != null)
                    UnityEditor.AssetDatabase.DeleteAsset(emptyPath);
                try
                {
                    UnityEditor.AssetDatabase.CreateAsset(empty, emptyPath);
                    UnityEditor.AssetDatabase.SaveAssets();
                    var emptyYaml = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(emptyPath));
                    StringAssert.DoesNotContain($"cx: {wantCx}", emptyYaml,
                        "空资产里竟然有种子数据 ⇒ 上面那些断言根本分辨不了存没存，是恒真的");
                }
                finally
                {
                    Object.DestroyImmediate(empty, true);
                    UnityEditor.AssetDatabase.DeleteAsset(emptyPath);
                }
            }
            finally
            {
                if (created != null) Object.DestroyImmediate(created, true);
                UnityEditor.AssetDatabase.DeleteAsset(path);
                UnityEditor.AssetDatabase.Refresh();
            }
        }
    }
}
