using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 水面几何（02 改版）。与地形<b>同一套显示网格</b>（同 chunk 边界、同绕序、同材质回退链）。
    /// <para>刻意复用 <see cref="TerrainView.ChunkVertexRect"/> 与 <see cref="TerrainView.ApplyToMesh"/>：
    /// 若水面自造一套网格，两层的 chunk 边界与顶点数会分叉，出现"水面与地形错位、边缘漏缝"，
    /// 而它在编辑器里的现象是"水面上有一道斜缝"，几乎无法归因。</para>
    /// <para><b>只铺"四角全是水"的 quad</b>，而不是"整块网格 + 顶点 alpha 羽化"：
    /// 本包自带的地形着色器是<b>不透明</b>的（01 §3.3，单 pass 顶点色×兰伯特），
    /// 拿 alpha=0 表达"这里没水"根本不会变透明 ⇒ 无水顶点会以<b>实心蓝</b>盖在地形上，
    /// 用户看到的就是"只要有点水，整片地都染蓝"。宁可让岸线粗一个显示步，也不能画错。
    /// 代价如实记录：岸线误差 = 一个 <see cref="CityGrid.DisplayStep"/>（8km 档 16m）；
    /// 要更细的岸线需引入透明 shader 或按逻辑格(4m)加密水面网格，留到水面材质正式做时一起处理。</para>
    /// <para><b>绕序与地形完全一致</b>（H-38 同源纪律）：写反会从天空看不到水——地形当年正是这样整片消失过一次。</para>
    /// </summary>
    public static class WaterView
    {
        /// <summary>水面顶点色。走的是与地形同一套顶点色材质，所以必须是<b>不透明</b>的 a=255。</summary>
        public static readonly Color32 WaterColor = new Color32(38, 86, 122, 255);

        /// <summary>水面略抬这么多米，避免与地表 co-planar 闪面（z-fighting）。</summary>
        public const float SurfaceLift = 0.02f;

        /// <summary>
        /// 构建一个 chunk 的水面几何。<paramref name="wf"/> 为 null 或该 chunk 无水 ⇒ 返回空几何
        /// （宿主据此 <c>Mesh.Clear</c> 并关渲染器；"无水"必须是空几何，不能画一片零高水面）。
        /// </summary>
        public static TerrainView.ChunkGeometry BuildChunk(WaterField wf, in CityGrid grid,
                                                           Vector2Int chunk, float effectiveChunkSize)
        {
            var g = new TerrainView.ChunkGeometry();
            if (wf == null) return g;
            if (!TerrainView.ChunkVertexRect(in grid, chunk, effectiveChunkSize,
                                             out var i0, out var i1, out var j0, out var j1)) return g;

            var cols = i1 - i0 + 1;
            var rows = j1 - j0 + 1;
            var r = grid.DisplayStep;
            var do0 = TerrainView.DisplayOrigin(in grid);   // 与地形同一取样基准（H-40 同口径）
            var colsLogic = grid.Columns; var rowsLogic = grid.Rows;

            // 第一遍：标记哪些显示顶点是"水顶点"（其所属逻辑格为水），并数出可铺的 quad
            var vertWater = new bool[cols * rows];
            var quadOk = new bool[(cols - 1) * (rows - 1)];
            var quadCount = 0;
            for (var jj = 0; jj < rows; jj++)
            {
                var cz = CellIndex(do0.y + (j0 + jj) * r, grid, true);
                for (var ii = 0; ii < cols; ii++)
                    vertWater[jj * cols + ii] = cz >= 0 && cz < rowsLogic
                        && wf.IsWater(Clamp(CellIndex(do0.x + (i0 + ii) * r, grid, false), colsLogic), cz);
            }
            for (var jj = 0; jj < rows - 1; jj++)
                for (var ii = 0; ii < cols - 1; ii++)
                {
                    var a = jj * cols + ii;
                    var ok = vertWater[a] && vertWater[a + 1]
                             && vertWater[a + cols] && vertWater[a + cols + 1];
                    quadOk[jj * (cols - 1) + ii] = ok;
                    if (ok) quadCount++;
                }
            if (quadCount == 0) return g;                     // 本 chunk 无水：早退，不分配任何数组

            // 顶点只保留"至少属于一个水 quad"的那些 ⇒ 压缩索引
            var keep = new bool[cols * rows];
            for (var jj = 0; jj < rows - 1; jj++)
                for (var ii = 0; ii < cols - 1; ii++)
                {
                    if (!quadOk[jj * (cols - 1) + ii]) continue;
                    var a = jj * cols + ii;
                    keep[a] = keep[a + 1] = keep[a + cols] = keep[a + cols + 1] = true;
                }

            var remap = new int[cols * rows];
            var n = 0;
            for (var i = 0; i < keep.Length; i++) { remap[i] = keep[i] ? n++ : -1; }
            if (n == 0) return g;

            g.OriginIndex = new Vector2Int(i0, j0);
            // ⚠️ VertCount <b>刻意置 (0,0)</b>：水面是<b>稀疏</b>网格（只铺"四角全水"的 quad），
            // 顶点数与 cols×rows 不再相等 ⇒ 任何按 `Positions[y*VertCount.x + x]` 索引的消费者都会越界或读错。
            // 保持 0 让这类误用<b>立刻暴露</b>（越界抛异常），而不是悄悄读到错顶点 ——
            // 地形的 VertCount 语义（TerrainViewTests 依赖它推行列）不适用于水面，消费者只能走
            // Positions / Triangles 两个数组。ApplyToMesh 正是只用这两个，所以水面复用它安全。
            g.VertCount = Vector2Int.zero;
            g.Positions = new Vector3[n];
            g.Normals = new Vector3[n];
            g.Uvs = new Vector2[n];
            g.Colors = new Color32[n];

            for (var jj = 0; jj < rows; jj++)
            {
                var wz = do0.y + (j0 + jj) * r;
                for (var ii = 0; ii < cols; ii++)
                {
                    var src = jj * cols + ii;
                    if (remap[src] < 0) continue;
                    var wx = do0.x + (i0 + ii) * r;
                    var cz = Clamp(CellIndex(wz, grid, true), rowsLogic);
                    var cx = Clamp(CellIndex(wx, grid, false), colsLogic);
                    // 水面高度取<b>该格的水面</b>（含局部水位）：高原湖必须画在自己的水位上，
                    // 画到全局海平面就会埋进地形里看不见（用户会读成"加了水但没水面"）。
                    var level = wf.WaterSurfaceAt(cx, cz);
                    if (float.IsNegativeInfinity(level)) level = wf.SeaLevelUsed;

                    var idx = remap[src];
                    g.Positions[idx] = new Vector3(wx, level + SurfaceLift, wz);
                    g.Normals[idx] = Vector3.up;               // 水面恒水平（不跟地形法线，否则被染成坡形）
                    g.Uvs[idx] = new Vector2(wx / r, wz / r);
                    g.Colors[idx] = WaterColor;
                }
            }

            g.Triangles = new int[quadCount * 6];
            var k = 0;
            for (var jj = 0; jj < rows - 1; jj++)
                for (var ii = 0; ii < cols - 1; ii++)
                {
                    if (!quadOk[jj * (cols - 1) + ii]) continue;
                    var a = remap[jj * cols + ii];
                    var b = remap[jj * cols + ii + 1];
                    var c2 = remap[(jj + 1) * cols + ii];
                    var d = remap[(jj + 1) * cols + ii + 1];
                    if (a < 0 || b < 0 || c2 < 0 || d < 0) continue;   // 理论不会发生（quadOk 已保证）
                    // H-38：(a,c,b)+(c,d,b) ⇒ Cross(c-a, b-a) 的 y = +Δz·Δx > 0 ⇒ 法线朝上、从天空可见
                    g.Triangles[k++] = a; g.Triangles[k++] = c2; g.Triangles[k++] = b;
                    g.Triangles[k++] = c2; g.Triangles[k++] = d; g.Triangles[k++] = b;
                }
            if (k == 0) return new TerrainView.ChunkGeometry();   // 空几何 ⇒ 宿主清 mesh；
            if (k != g.Triangles.Length) Array.Resize(ref g.Triangles, k);   // 压缩后可能少几个
            return g;
        }

        /// <summary>世界坐标（X 或 Z，靠 <paramref name="isZ"/> 区分）→ 逻辑格号。floor 语义同 CityGrid。</summary>
        static int CellIndex(float v, in CityGrid grid, bool isZ)
            => Mathf.FloorToInt((v - (isZ ? grid.Origin.y : grid.Origin.x)) / grid.CellSize);

        static int Clamp(int v, int max) => v < 0 ? 0 : (v >= max ? max - 1 : v);
    }
}
