using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Collin.CityMap.Tests
{
    /// <summary>
    /// 水面几何（<see cref="WaterView"/>）判据。<b>不重复测算法，只测"画出来对不对"</b>：
    /// 水面画错的现象是"整片地被染蓝""加了水却看不见水面"，或"水面上一条从原点射出的蓝带"，
    /// 三者都不会报错、也不会被算法测试抓到。
    /// <para>期望值只用两类<b>与被测实现无关</b>的事实：
    /// ① 极端情形的纯网格算术（全水 ⇒ 顶点数 = 显示网格顶点数、quad 数 = 格数²；全不水 ⇒ 空）；
    /// ② 几何自身的硬不变量（绕序朝上、高度 = 该格水面高程 + 抬升、索引合法、无孤立顶点）。
    /// <b>刻意不复刻"显示顶点 → 逻辑格"的映射来推期望值</b>——那是把被测代码再抄一遍，恒真。</para>
    /// </summary>
    public class WaterViewTests
    {
        const float Cell = 4f;
        const float Display = 8f;         // 2 个逻辑格 = 1 个显示步
        const float World = 64f;          // 16 逻辑格
        const int Cols = 16;              // World / Cell
        /// <summary>
        /// 单边显示顶点数 = <c>floor((Columns−2)·cell / display) + 1</c> = floor(56/8)+1 = <b>8</b>。
        /// <para>我第一版按"64/8+1=9"算 ⇒ 全错。<b>两端要各内缩一个逻辑格</b>：显示层的法线用
        /// 中心差分、探针需要半格，越界就被 clamp 成假平地（H-40 的定案，代价就是边各少一格）。
        /// 教训与本轮反复出现的同一个坑一致：<b>不读实现就写期望值，写出来的必然是猜的。</b></para>
        /// </summary>
        const int DisplayCount = 8;
        const int Quads = (DisplayCount - 1) * (DisplayCount - 1);   // 7×7 = 49
        const float Level = 3.5f;

        CityGrid _grid;

        [SetUp]
        public void SetUp()
        {
            _grid = new CityGrid(World, Cell, Display, World, World * 2f, 0f, Vector2.zero);
            // 前提校验必须<b>可失败</b>。我第一版写成 `_grid.CellSize * (World / Cell) == World`——
            // 那是 a*(b/a)==b 的代数恒等式，永真，等于没断言（本包已三次栽在这种空断言上）。
            Assert.AreEqual(Cols, _grid.Columns, "前提：逻辑格必须是 64/4 = 16 列");
            Assert.AreEqual(DisplayCount, TerrainView.DisplayIndexCount(_grid),
                "前提：显示顶点数必须是 8（公式见 DisplayCount 注释）");
            Assert.AreEqual(2, (int)(Display / Cell), "前提：一个显示步 = 2 个逻辑格");
        }

        /// <summary>造一份水体派生：整张要么全水、要么全不水。</summary>
        WaterField MakeField(bool allWater, float level = Level)
        {
            var mask = new ushort[_grid.CellCount];
            var cellLevel = new float[_grid.CellCount];
            for (var i = 0; i < mask.Length; i++)
            {
                if (allWater) mask[i] |= (ushort)CellFlag.Water;
                cellLevel[i] = allWater ? level : float.NegativeInfinity;
            }
            return new WaterField(allWater ? mask.Length : 0, 0, allWater ? 1 : 0, 1,
                                  mask, cellLevel, _grid, null);
        }

        TerrainView.ChunkGeometry Build(WaterField wf)
            => WaterView.BuildChunk(wf, _grid, Vector2Int.zero, World);

        // ───────────────────── 极端情形：纯网格算术 ─────────────────────

        [Test]
        public void No_Water_Produces_Empty_Geometry()
        {
            var g = Build(MakeField(false));
            Assert.IsTrue(g.IsEmpty,
                "无水必须是<b>空几何</b>：画一片零高度的水面会把整片地盖住（用户看到的正是'到处都是蓝膜'）");
            Assert.AreEqual(0, g.VertexCount);
            Assert.AreEqual(0, g.Triangles.Length);
        }

        [Test]
        public void All_Water_Paves_Every_Display_Quad()
        {
            var g = Build(MakeField(true));
            Assert.AreEqual(DisplayCount * DisplayCount, g.VertexCount,
                "全水时顶点数必须等于整张显示网格的顶点数（纯算术，不依赖映射细节）");
            Assert.AreEqual(Quads * 6, g.Triangles.Length,
                $"全水时 quad 数必须 = 7×7 = {Quads} ⇒ {Quads * 6} 个索引");
        }

        [Test]
        public void Every_Vertex_Sits_At_Level_Plus_Lift_With_Upward_Normal()
        {
            var g = Build(MakeField(true));
            var want = Level + WaterView.SurfaceLift;
            for (var i = 0; i < g.Positions.Length; i++)
            {
                Assert.AreEqual(want, g.Positions[i].y, 1e-4f,
                    $"顶点 {i} 高度必须是<b>该格水面高程</b>+抬升（实得 {g.Positions[i].y:0.###}）：" +
                    "取地形高度会让水面随坡度皱起来，取 0 会让它埋进地里看不见");
                Assert.AreEqual(0f, g.Normals[i].x, 1e-5f);
                Assert.AreEqual(1f, g.Normals[i].y, 1e-5f,
                    "水面法线必须恒朝上：跟地形法线会把水面染成坡形明暗");
                Assert.AreEqual(0f, g.Normals[i].z, 1e-5f);
            }
        }

        // ───────────────────── 硬不变量：绕序与索引 ─────────────────────

        [Test]
        public void Triangles_Wind_Upward_And_Indices_Are_InRange()
        {
            var g = Build(MakeField(true));
            Assert.Greater(g.Triangles.Length, 0, "前提：必须有三角形可验");

            for (var t = 0; t + 2 < g.Triangles.Length; t += 3)
            {
                var i0 = g.Triangles[t]; var i1 = g.Triangles[t + 1]; var i2 = g.Triangles[t + 2];
                foreach (var idx in new[] { i0, i1, i2 })
                    Assert.GreaterOrEqual(idx, 0, "负索引 ⇒ Unity 直接拒绝上传 mesh");

                Assert.Less(i0, g.VertexCount, "索引越界 = 破面（H-38 同族：越界时 Unity 静默不清 mesh）");
                Assert.Less(i1, g.VertexCount);
                Assert.Less(i2, g.VertexCount);

                var a = g.Positions[i0]; var b = g.Positions[i1]; var c = g.Positions[i2];
                // 三角形法线 = <b>Cross(v1-v0, v2-v0)</b>（右手定则）。
                // 我第一版写成 Cross(c-a, b-a) ⇒ 符号相反，把<b>正确的</b>绕序判成"朝下"，
                // 差点去改一个不存在的 bug（H-67 同族：断言先红，先查自己口径）。
                var n = Vector3.Cross(b - a, c - a);
                Assert.Greater(n.y, 0f,
                    $"三角形 #{t / 3} 绕序朝下（Cross 的 y = {n.y:0.##}）⇒ 从天空看不到水面。" +
                    "地形当年正是这个错导致整片消失（H-38），水面必须同口径。");
            }
        }

        /// <summary>压缩索引后不得留孤立顶点（顶点数组里每个都必须被至少一个三角形引用）。</summary>
        [Test]
        public void No_Orphan_Vertices_After_Compression()
        {
            var g = Build(MakeField(true));
            var referenced = new HashSet<int>();
            foreach (var i in g.Triangles) referenced.Add(i);

            // ⚠️ 不能断言"索引不重复"：规则网格里相邻 quad <b>本就共享顶点</b>，
            //    一个内部顶点会被引用最多 6 次。真正的不变量是"顶点数 == 被引用的不同索引数"。
            Assert.AreEqual(g.VertexCount, referenced.Count,
                $"有孤立顶点：顶点数组 {g.VertexCount} 个、实际被引用 {referenced.Count} 个 " +
                "⇒ 压缩索引时多留了，浪费上传带宽（且说明 keep 标记与 remap 不同源）");
        }

        // ─────────── 塌陷三角形防护（"算水顶点"与"取高程"必须同源） ───────────

        /// <summary>
        /// 掩码说是水、但<b>取不到水面高程</b>的格，绝不能留下一个落在世界原点的顶点。
        /// <para>存在理由（本轮我自己写出来的一个真 bug，被这条用例逼住）：
        /// 第一版把"跳过无高程顶点"的 <c>continue</c> 写在<b>填顶点</b>那一遍，
        /// 而"是不是水顶点"在<b>第一遍</b>就已经按掩码定了 ⇒ 该顶点仍被算进 keep/remap 与 quad，
        /// 结果 quad 里有一个顶点停在 (0,0,0) ⇒ <b>水面上射出一条通往世界原点的蓝带</b>。
        /// 现在两遍用同一个判据（第一遍同时存下高程），所以这种顶点根本不成立。</para>
        /// </summary>
        [Test]
        public void Cell_Flagged_Water_But_Without_Surface_Height_Draws_No_Collapsed_Vertex()
        {
            var mask = new ushort[_grid.CellCount];
            var cellLevel = new float[_grid.CellCount];
            for (var i = 0; i < cellLevel.Length; i++) cellLevel[i] = float.NegativeInfinity;

            // 整张都标成水，但高程全取不到 ⇒ 必须一张都不铺（而不是铺出塌陷面）
            for (var i = 0; i < mask.Length; i++) mask[i] |= (ushort)CellFlag.Water;
            var wf = new WaterField(_grid.CellCount, 0, 1, 1, mask, cellLevel, _grid, null);

            var g = Build(wf);
            Assert.IsTrue(g.IsEmpty,
                "取不到水面高程时必须是空几何：留顶点在 (0,0,0) 会画出一条射向世界原点的蓝带");

            // 对照：同样全水、但高程取得到 ⇒ 必须铺满（证明上面那条不是"夹具本身画不出面"）
            var ok = MakeField(true);
            var g2 = Build(ok);
            Assert.AreEqual(Quads * 6, g2.Triangles.Length,
                "对照：有高程时必须铺满 49 个 quad，否则『空几何』那条是空过的");
        }

        /// <summary>
        /// 半张有高程、半张取不到高程时，铺出来的每个顶点都必须停在水面高程上。
        /// <para>这是上一条的"有面可验"版本：<c>Cell_Flagged_Water_But_Without_Surface_Height</c>
        /// 构造出的是空几何，顶点数组为空 ⇒ 任何逐顶点断言都会<b>空过</b>（本包反复栽过的假绿）。
        /// 所以这里刻意让部分 quad 四角全有高程 ⇒ 真有顶点可验，再逐个数落点。</para>
        /// </summary>
        [Test]
        public void No_Vertex_Falls_Back_To_World_Origin()
        {
            var mask = new ushort[_grid.CellCount];
            var cellLevel = new float[_grid.CellCount];
            for (var i = 0; i < cellLevel.Length; i++)
            {
                mask[i] |= (ushort)CellFlag.Water;                 // 全图都是水
                cellLevel[i] = float.NegativeInfinity;             // 但先都取不到高程
            }
            // 只给第一个显示 quad 的四角格高程（1,1)(3,1)(1,3)(3,3)，其余仍是"有水无高程"
            foreach (var (px, pz) in new[] { (1, 1), (3, 1), (1, 3), (3, 3) })
                cellLevel[pz * Cols + px] = Level;

            var wf = new WaterField(_grid.CellCount, 0, 1, 1, mask, cellLevel, _grid, null);
            var g = Build(wf);

            Assert.AreEqual(6, g.Triangles.Length,
                "四角有高程的那一个 quad 必须铺出来（其余取不到高程的 quad 必须不铺）");
            Assert.AreEqual(4, g.VertexCount, "只留这 4 个顶点 ⇒ 多留任何一个就是塌陷面的来源");
            for (var i = 0; i < g.Positions.Length; i++)
                Assert.AreEqual(Level + WaterView.SurfaceLift, g.Positions[i].y, 1e-4f,
                    $"顶点 {i} 的 y = {g.Positions[i].y:0.###}：停在原点量级说明有个「是水但没高程」的顶点被留下了");
        }

        // ───────────────────── 孤立单格水：岸线误差登记点 ─────────────────────

        /// <summary>
        /// 孤立单格水铺不出面 —— 这是"岸线误差 = 一个显示步"这一<b>刻意取舍</b>的登记点。
        /// <para>写死它不是为了固化缺陷，而是因为 WaterView 的注释里承诺了这个代价；
        /// 将来若引入透明材质做亚格羽化，必须同步改这里，否则测试会替旧口径背书。</para>
        /// </summary>
        [Test]
        public void Isolated_Single_Cell_Water_Produces_No_Quad()
        {
            var mask = new ushort[_grid.CellCount];
            var cellLevel = new float[_grid.CellCount];
            for (var i = 0; i < cellLevel.Length; i++) cellLevel[i] = float.NegativeInfinity;

            // ★ 显示顶点<b>不是</b>落在偶数格上：DisplayOrigin = Origin + 一格 = 4m，
            //   步长 8m ⇒ 顶点世界 x = 4,12,20,…，经 floor((x−0)/4) 得<b>奇数格 1,3,5,…,15</b>。
            //   （我第一版按"偶数格 2,4,6…"取样，对照里铺不出面 —— 又是一个不核实就写的期望值。）
            //   所以第一个显示 quad 的四角 = 逻辑格 (1,1)(3,1)(1,3)(3,3)。
            const int s0 = 1, s1 = 3;                      // 相邻两个显示顶点的逻辑格
            mask[s0 * Cols + s0] = (ushort)CellFlag.Water;
            cellLevel[s0 * Cols + s0] = Level;
            var wf = new WaterField(1, 0, 1, 1, mask, cellLevel, _grid, null);
            Assert.IsTrue(wf.IsWater(s0, s0), "前提：该格确被判为水");

            var g = WaterView.BuildChunk(wf, _grid, Vector2Int.zero, World);
            Assert.IsTrue(g.IsEmpty, "四角不全水的 quad 不得铺面（否则无水角会实心染蓝）");

            // ★ 防恒真对照：把第一个显示 quad 的<b>四个角格</b>都设成水 ⇒ 必须铺得出面。
            //   缺了这条，"空几何"可能只是因为那格根本不在采样范围内而白过。
            var mask2 = (ushort[])mask.Clone();
            var lvl2 = (float[])cellLevel.Clone();
            foreach (var (px, pz) in new[] { (s0, s0), (s1, s0), (s0, s1), (s1, s1) })
            {
                mask2[pz * Cols + px] = (ushort)CellFlag.Water;
                lvl2[pz * Cols + px] = Level;
            }
            var wf2 = new WaterField(4, 0, 1, 1, mask2, lvl2, _grid, null);
            var g2 = WaterView.BuildChunk(wf2, _grid, Vector2Int.zero, World);
            Assert.IsFalse(g2.IsEmpty,
                "一个显示 quad 的四角全水时必须铺出至少一个面 —— 否则上面那条『空几何』是空过的");
            Assert.AreEqual(6, g2.Triangles.Length, "四角全水只凑出 1 个 quad ⇒ 恰好 6 个索引");
        }

        // ───────────────────── 增量写入路径（H-39 同源） ─────────────────────

        /// <summary>
        /// 同一 mesh 第二次写入必须走"同拓扑局部更新"且不抛。
        /// <para>H-39 的教训：局部更新路径冷启动永远走不到，此前用裸缓冲流写 API 在第一次增量时抛
        /// 「copy N bytes into a <b>0 byte buffer</b>」。水面复用 <see cref="TerrainView.ApplyToMesh"/>，
        /// 所以那个坑对它同样成立 —— 地形测试钉的是地形，水面必须<b>自己</b>钉一次。</para>
        /// </summary>
        [Test]
        public void Second_Write_Reuses_Topology_Without_Error()
        {
            var mesh = new Mesh { name = "t_water" };
            try
            {
                var g = Build(MakeField(true));
                TerrainView.ApplyToMesh(g, mesh);
                var firstVerts = mesh.vertexCount;
                Assert.AreEqual(g.VertexCount, firstVerts, "首次全量写入必须真把顶点交上去");

                Assert.DoesNotThrow(() => TerrainView.ApplyToMesh(g, mesh),
                    "第二次同拓扑写入不得抛（增量路径的经典崩点）");
                Assert.AreEqual(firstVerts, mesh.vertexCount, "同拓扑局部更新不得改变顶点数");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // ─────────── 每颗种子自己的水面高程必须被尊重（高原天池） ───────────

        [Test]
        public void Per_Cell_Surface_Height_Drives_Vertex_Not_Any_Global_Reference()
        {
            // 天池高程 160m：画成 0m 就整片埋进地形，用户读成"加了水但没有水面"。
            var wf = MakeField(true, 160f);
            Assert.AreEqual(160f, wf.WaterSurfaceAt(3, 3), 1e-4f,
                "WaterSurfaceAt 必须返回<b>该格</b>的水面高程");
            Assert.AreEqual(160f, wf.MaxWaterLevel, 1e-4f, "统计的最高水面高程必须与逐格值一致");
            Assert.AreEqual(160f, wf.MinWaterLevel, 1e-4f);

            var g = Build(wf);
            Assert.Greater(g.VertexCount, 0, "前提：铺出了水面");
            Assert.AreEqual(160f + WaterView.SurfaceLift, g.Positions[0].y, 1e-3f,
                "顶点必须按该格水面高程铺出，否则高原天池不可见");
        }

        /// <summary>
        /// 水面高程区间与覆盖比例（右上角分层统计的读数来源）。
        /// <para>刻意做成<b>可失败</b>的区间断言：只改种子高程而面积不变时，
        /// 引用与格数都不变 —— 视图靠 MaxWaterLevel 才能发现"水面该重铺了"，
        /// 所以这个读数不是装饰，是刷新判据的一部分。</para>
        /// </summary>
        [Test]
        public void Water_Field_Reports_Level_Range_And_Area_Ratio()
        {
            var mask = new ushort[_grid.CellCount];
            var cellLevel = new float[_grid.CellCount];
            for (var i = 0; i < cellLevel.Length; i++) cellLevel[i] = float.NegativeInfinity;

            var half = _grid.CellCount / 2;                    // 恰好一半格是水，面积比例可解析
            for (var i = 0; i < half; i++)
            {
                mask[i] |= (ushort)CellFlag.Water;
                cellLevel[i] = 10f + 20f * (i % 2);            // 10 或 30 ⇒ 区间 [10,30]
            }
            var wf = new WaterField(half, 0, 2, 2, mask, cellLevel, _grid, null);

            Assert.AreEqual(10f, wf.MinWaterLevel, 1e-4f, "最低水面高程");
            Assert.AreEqual(30f, wf.MaxWaterLevel, 1e-4f, "最高水面高程");
            Assert.AreEqual(0.5f, wf.WaterAreaRatio, 1e-4f,
                $"覆盖比例必须是 {half}/{_grid.CellCount}（面板直接读它，不容许再换算一遍）");
        }

        [Test]
        public void Non_Water_Cell_Reports_Negative_Infinity_Not_Zero()
        {
            var mask = new ushort[_grid.CellCount];
            var cellLevel = new float[_grid.CellCount];
            for (var i = 0; i < cellLevel.Length; i++) cellLevel[i] = 0f;    // 故意全填 0
            var wf = new WaterField(0, 0, 0, 1, mask, cellLevel, _grid, null);

            // 0 是<b>合法水面高程</b>（谷底水塘就该是 0m），绝不能拿来当"没有水面"的哨兵（H-31 同族）。
            Assert.IsTrue(float.IsNegativeInfinity(wf.WaterSurfaceAt(5, 5)),
                "非水格必须返回 -∞，不能返回 0 或 cellLevel 里的残留值");
        }

        /// <summary>没水时高程区间必须是"无值"而不是 ±∞ 量级，否则面板读数会显示成乱码。</summary>
        [Test]
        public void No_Water_Field_Reports_Invalid_Level_Range()
        {
            var wf = MakeField(false);
            Assert.AreEqual(0, wf.WaterCells);
            Assert.IsTrue(float.IsNegativeInfinity(wf.MinWaterLevel),
                "无水时 Min 必须是 -∞（面板据此显示「—」，不能显示 -1.2e38）");
            Assert.AreEqual(0f, wf.WaterAreaRatio, 1e-6f);
        }

        /// <summary>没跑出 S2（槽位 null）时不得抛，也不得画出任何几何。</summary>
        [Test]
        public void Null_Water_Field_Is_Tolerated()
        {
            Assert.DoesNotThrow(() =>
            {
                var g = WaterView.BuildChunk(null, _grid, Vector2Int.zero, World);
                Assert.IsTrue(g.IsEmpty);
            }, "没加水是常态：S2 无槽位时视图必须安静地什么都不画");
        }
    }
}
