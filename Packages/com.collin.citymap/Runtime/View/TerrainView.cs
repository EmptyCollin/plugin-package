using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 显示层几何构建（Modules/01 §3.3）。<b>纯函数、只读 View</b>：
    /// 输入 <see cref="TerrainField"/>，输出 chunk 的顶点/三角形数组，不触碰任何 Unity 对象、不引用 UnityEditor。
    /// <para>口径锁死（§0.1 原则3：编辑态与 Play 态同一条构建路径）：
    /// ① 顶点高度 = <see cref="TerrainField.GetHeight"/>（双线性，含平整结果）；
    /// ② 顶点法线 = <see cref="TerrainField.GetNormal"/>，<b>禁止 <c>Mesh.RecalculateNormals</c></b>
    ///    ——后者用三角形几何平均，与差分口径不一致，面板读数与画面会出现两种坡度（01 §3.3 明令）；
    /// ③ UV = 世界 XZ / tileScale（世界映射，接缝不拉伸）；
    /// ④ 顶点色 = 坡度色带（调试着色，不参与任何生成判定）。</para>
    /// <para>chunk 边界对齐 <see cref="CityGrid.DisplayStep"/> 网格线：相邻 chunk 复用<b>同一批世界坐标</b>
    /// （顶点重复但位置逐位相同），因此不会开裂；若各自取整到不同坐标，拼缝处会出现阶梯。</para>
    /// </summary>
    public static class TerrainView
    {
        /// <summary>坡度色带的参考上限（度）：达到即最红。仅作调试着色刻度，非规范阈值。</summary>
        public const float SlopeRefDeg = 25f;

        /// <summary>单个 chunk 的显示几何（SoA 数组；长度为 0 = 该 chunk 无 quad 可建）。</summary>
        public sealed class ChunkGeometry
        {
            public Vector3[] Positions = Array.Empty<Vector3>();
            public Vector3[] Normals = Array.Empty<Vector3>();
            public Vector2[] Uvs = Array.Empty<Vector2>();
            public Color32[] Colors = Array.Empty<Color32>();
            public int[] Triangles = Array.Empty<int>();
            public Vector2Int OriginIndex;      // 首顶点对应的显示网格索引 (i0, j0)
            public Vector2Int VertCount;        // (列, 行)

            public int VertexCount => Positions.Length;
            public bool IsEmpty => Positions.Length == 0 || Triangles.Length == 0;
        }

        /// <summary>显示参数（由宿主钳制后传入；ChunkSize 已是 displayStep 的整数倍）。</summary>
        public readonly struct ViewParams
        {
            public readonly float ChunkSize;
            public readonly float UvTileScale;
            public readonly bool SlopeColors;
            public readonly float SlopeRefDeg;

            public ViewParams(float chunkSize, float uvTileScale, bool slopeColors, float slopeRefDeg)
            {
                ChunkSize = chunkSize;
                UvTileScale = uvTileScale;
                SlopeColors = slopeColors;
                SlopeRefDeg = Mathf.Max(1f, slopeRefDeg);
            }
        }

        /// <summary>
        /// 把请求的 chunk 尺寸钳到 <b>displayStep 的整数倍</b>（单一实现处，面板显示与构建都读它）。
        /// 不取整数倍会让 chunk 边界落不到显示网格线上 ⇒ 顶点被两套规则切分，拼缝处出现台阶。
        /// <para>H-41：对齐方向必须是<b>向上取整</b>（结果 ≥ 请求值）。
        /// 先前用 <c>RoundToInt</c>，100/8=12.5 被舍到 12 ⇒ 实际 chunk 比用户要的更细，
        /// 且"12.5 归整"依赖具体实现（不同 .NET 版本的 midpoint 规则可能给 12 或 13），
        /// 表现是同一份参数在不同机器上切出不同数量的 chunk = 破坏可复现性。
        /// 向上取整是确定性的，且符合"我至少要这么大一块"的直觉。</para>
        /// </summary>
        public static int SnapRatio(float requestedChunkSize, float displayStep)
        {
            if (displayStep <= 0f) return 1;
            var k = Mathf.CeilToInt(Mathf.Max(displayStep, requestedChunkSize) / displayStep);
            return Mathf.Max(1, k);
        }

        public static float EffectiveChunkSize(float requestedChunkSize, float displayStep)
            => SnapRatio(requestedChunkSize, displayStep) * displayStep;

        /// <summary>chunk 总数（单边）。图幅小于一个 chunk 时至少 1。</summary>
        public static int ChunksPerSide(float worldSize, float effectiveChunkSize)
            => Mathf.Max(1, Mathf.CeilToInt(worldSize / Mathf.Max(1f, effectiveChunkSize)));

        /// <summary>
        /// 显示顶点的取样基准角点 = <c>Origin + cellSize</c>（首格中心再内缩半格）。
        /// <para>H-40（两次修正才收敛，原因值得留下）：逻辑<b>格中心</b>与图幅<b>角网格线</b>差半格。
        /// 顶点若取在 <c>Origin + k·displayStep</c>，四周的顶点采样会被
        /// <see cref="TerrainField.Sample"/> 的 clamp 改变 ⇒ 图幅边缘画出一圈"比实际更平"的假地形
        /// （实测顶点法线偏差 16.25°）。
        /// 第一次修正是把基准移到<b>首格中心</b>，偏差降到 7.96° —— 残余没有消失，因为
        /// <see cref="TerrainField.GetNormal"/> 是<b>中心差分</b>（探针 = <code>±cellSize/2</code>），
        /// 顶点即使自己落在域内，它的探针仍然越界。7.96° 可精确解释：
        /// 有效梯度减半后 <code>atan(0.2915) − atan(0.1458) = 16.25° − 8.29° = 7.96°</code>。</para>
        /// <para>因此显示层的安全域必须让<b>探针也在域内</b>：顶点 x ∈ [Origin + cellSize,
        /// Origin + (Columns−1)·cellSize]，即四边各内缩<b>一个逻辑格</b>。
        /// 代价是地形边缘比图幅内缩 ≤ (1 格 + 1 显示步)（8km 图上 ≤ 12m），
        /// 且完全落在 <c>buildableInset</c>（默认 60m）外环不可建带内 —— 视觉上不可见，
        /// 远优于"边缘出现假平地"。导出侧不受影响（10 §5.1 按切块边界独立重切 mesh）。</para>
        /// </summary>
        public static Vector2 DisplayOrigin(in CityGrid grid)
            => grid.Origin + new Vector2(grid.CellSize, grid.CellSize);

        /// <summary>
        /// 单边可用的显示顶点数（在 <see cref="DisplayOrigin"/> 的<b>探针安全域</b>内按显示步长取点）。
        /// &lt;2 = 无法构成 quad。
        /// </summary>
        public static int DisplayIndexCount(in CityGrid grid)
        {
            if (grid.CellSize <= 0f || grid.DisplayStep <= 0f || grid.Columns < 3 || grid.Rows < 3) return 0;
            // 跨度 = (Columns-2)·cellSize：左端内缩一格、右端同样内缩一格（差分探针各需半格）
            var span = (Mathf.Min(grid.Columns, grid.Rows) - 2) * grid.CellSize;
            return Mathf.FloorToInt(span / grid.DisplayStep + 1e-3f) + 1;
        }

        /// <summary>
        /// 某 chunk 覆盖的显示顶点索引区间（闭区间，索引以 <see cref="DisplayOrigin"/> 为原点）。
        /// 返回 false = 该 chunk 无 quad 可建。
        /// <para>末尾 chunk 被数据域截断（索引钳到 <see cref="DisplayIndexCount"/>-1），
        /// 因此图幅右/上边缘会内缩最多一个显示步 —— 这是 H-40 的有意取舍，不是漏画。</para>
        /// </summary>
        public static bool ChunkVertexRect(in CityGrid grid, Vector2Int chunk, float effectiveChunkSize,
                                           out int i0, out int i1, out int j0, out int j1)
        {
            i0 = i1 = j0 = j1 = 0;
            var count = DisplayIndexCount(in grid);
            if (count < 2 || grid.DisplayStep <= 0f || effectiveChunkSize <= 0f) return false;

            var last = count - 1;
            var r = grid.DisplayStep;
            i0 = Mathf.Clamp(Mathf.RoundToInt(chunk.x * effectiveChunkSize / r), 0, last);
            i1 = Mathf.Clamp(Mathf.RoundToInt((chunk.x + 1) * effectiveChunkSize / r), 0, last);
            j0 = Mathf.Clamp(Mathf.RoundToInt(chunk.y * effectiveChunkSize / r), 0, last);
            j1 = Mathf.Clamp(Mathf.RoundToInt((chunk.y + 1) * effectiveChunkSize / r), 0, last);

            return i1 - i0 >= 1 && j1 - j0 >= 1;                // 只剩一条线（0 quad）的 chunk 不产出
        }

        /// <summary>
        /// 构建一个 chunk 的显示几何。世界坐标 = <c>Origin + 索引 × DisplayStep</c>（显示网格<b>线</b>，
        /// 不是逻辑格中心——逻辑格 2m、显示 8m，逐逻辑格建顶点会让 8km 图产生 16M 顶点）。
        /// <para><b>坐标系半格错位（必须知道，否则会把边缘偏差当成 bug）</b>：
        /// 格中心 = <c>Origin + (i+0.5)·cellSize</c>，网格线 = <c>Origin + k·displayStep</c>。
        /// 于是首根网格线 <c>x = Origin</c> 落在首列格中心<b>外侧半格</b>：
        /// <see cref="TerrainField.Sample"/> 里 x0 = -1 被 clamp 到 0、两点重合 ⇒ 高度取成边缘格中心值。
        /// 斜面上表现为固定偏差 <c>A·cellSize/2</c>（M3 实测 0.25m，与坡度成正比），<b>且只出现在
        /// 最左/最下两排顶点</b>；内部网格线都在数据域内，双线性对线性函数无误差。
        /// 处置：保持 clamp（图幅边缘必须画出，否则留半格宽的洞），
        /// 由 <see cref="IsEdgeVertex"/> 供显示层消费者/测试识别这一圈，不静默改采样口径。</para>
        /// </summary>
        public static ChunkGeometry BuildChunk(TerrainField field, in CityGrid grid, Vector2Int chunk,
                                               ViewParams vp)
        {
            var g = new ChunkGeometry();
            if (field == null) return g;
            if (!ChunkVertexRect(in grid, chunk, vp.ChunkSize, out var i0, out var i1, out var j0, out var j1))
                return g;

            var cols = i1 - i0 + 1;
            var rows = j1 - j0 + 1;
            var n = cols * rows;
            var r = grid.DisplayStep;

            g.OriginIndex = new Vector2Int(i0, j0);
            g.VertCount = new Vector2Int(cols, rows);
            g.Positions = new Vector3[n];
            g.Normals = new Vector3[n];
            g.Uvs = new Vector2[n];
            g.Colors = new Color32[n];

            var uvS = Mathf.Max(2f, vp.UvTileScale);
            var do0 = DisplayOrigin(in grid);            // H-40：取样基准 = 首格中心，不是图幅角
            for (var jj = 0; jj < rows; jj++)
            {
                var wz = do0.y + (j0 + jj) * r;
                for (var ii = 0; ii < cols; ii++)
                {
                    var wx = do0.x + (i0 + ii) * r;
                    var idx = jj * cols + ii;

                    g.Positions[idx] = new Vector3(wx, field.GetHeight(wx, wz), wz);
                    g.Normals[idx] = field.GetNormal(wx, wz);              // 01 §3.3：与采样同口径
                    g.Uvs[idx] = new Vector2(wx / uvS, wz / uvS);
                    g.Colors[idx] = vp.SlopeColors
                        ? SlopeColor(field.GetSlopeDeg(wx, wz), vp.SlopeRefDeg)
                        : new Color32(255, 255, 255, 255);
                }
            }

            var quads = (cols - 1) * (rows - 1);
            g.Triangles = new int[quads * 6];
            var k = 0;
            for (var jj = 0; jj < rows - 1; jj++)
            for (var ii = 0; ii < cols - 1; ii++)
            {
                var a = jj * cols + ii;                 // (i,   j)
                var b = a + 1;                          // (i+1, j)
                var c = a + cols;                       // (i,   j+1)
                var d = c + 1;                          // (i+1, j+1)
                // H-38：绕序必须是 (a,c,b)+(c,d,b)。
                // 先前写的 (a,b,d)+(a,d,c) 经右手定则验算：
                //   Cross(b-a, d-a) = Cross((+Δx,·,0), (+Δx,·,+Δz)) 的 y 分量 = -Δx·Δz < 0
                // ⇒ 几何法线朝下 ⇒ 从天空看整片地形被背面剔除（画面"什么都没有"）。
                // 正确式：Cross(c-a, b-a) = Cross((0,·,+Δz), (+Δx,·,0)) 的 y = +Δz·Δx > 0。
                // 顶点布局 (x, height, z) 与 Unity 官方程序化平面范例同构。
                g.Triangles[k++] = a; g.Triangles[k++] = c; g.Triangles[k++] = b;
                g.Triangles[k++] = c; g.Triangles[k++] = d; g.Triangles[k++] = b;
            }
            return g;
        }

        /// <summary>
        /// 脏区域覆盖了哪些 chunk。<paramref name="region"/> 为 null = 全图（冷启动/全量重算）。
        /// <para>H-42：外扩余量必须是一个<b>显示步长</b>，不是"一整个 chunk"。
        /// 先前取 chunk 级余量（128m）的量级理由是错的——阶段上报的包围盒
        /// （<see cref="PlaneConstraint.GetAffectedBounds"/>）<b>已经包含</b> ExtendMeters + 半格容差，
        /// 显示顶点需要的额外余量只是几何影响半径：某个逻辑格高度变了，
        /// 双线性采样最多影响到 <see cref="CityGrid.DisplayStep"/> 范围内的显示网格线顶点。
        /// 用 chunk 级余量会把一次小改动放大成 3×3 = 9 块重建（实测：点状盒命中 9 块），
        /// 增量重建的收益当场归零，且表现为"改了没报错但明显卡"。</para>
        /// <para>退化盒（点）只命中它自己所在的那一块。</para>
        /// </summary>
        public static List<Vector2Int> ChunksTouching(in CityGrid grid, float effectiveChunkSize, Bounds? region)
        {
            var list = new List<Vector2Int>(16);
            if (grid.DisplayColumns <= 0 || effectiveChunkSize <= 0f) return list;
            var per = ChunksPerSide(grid.WorldSize, effectiveChunkSize);

            if (region == null)
            {
                for (var cz = 0; cz < per; cz++)
                for (var cx = 0; cx < per; cx++) list.Add(new Vector2Int(cx, cz));
                return list;
            }

            var b = region.Value;
            var m = Mathf.Max(grid.CellSize, grid.DisplayStep);
            var x0 = Mathf.Max(0, Mathf.FloorToInt((b.min.x - m) / effectiveChunkSize));
            var x1 = Mathf.Min(per - 1, Mathf.FloorToInt((b.max.x + m) / effectiveChunkSize));
            var z0 = Mathf.Max(0, Mathf.FloorToInt((b.min.z - m) / effectiveChunkSize));
            var z1 = Mathf.Min(per - 1, Mathf.FloorToInt((b.max.z + m) / effectiveChunkSize));
            for (var cz = z0; cz <= z1; cz++)
            for (var cx = x0; cx <= x1; cx++) list.Add(new Vector2Int(cx, cz));
            return list;
        }

        /// <summary>坡度→颜色（绿→土黄→红棕）。公开是为了让测试直接断言色带端点与单调性。</summary>
        public static Color32 SlopeColor(float slopeDeg, float refDeg)
        {
            var t = Mathf.Clamp01(slopeDeg / Mathf.Max(1f, refDeg));
            Color a, b;
            float f;
            if (t < 0.5f)
            {
                a = new Color(0.32f, 0.45f, 0.28f);      // 平：深绿
                b = new Color(0.72f, 0.62f, 0.40f);      // 中：土黄
                f = t * 2f;
            }
            else
            {
                a = new Color(0.72f, 0.62f, 0.40f);
                b = new Color(0.62f, 0.22f, 0.16f);      // 陡：红棕
                f = (t - 0.5f) * 2f;
            }
            // Color32 无 Lerp 静态方法（只有 Color 有）：线性插值后经隐式转换降为 8bit
            return Color.Lerp(a, b, f);
        }

        /// <summary>
        /// 把几何写进已有 mesh（顶点数相同则走局部更新，01 §5.4「UpdateVertexData 优先于重建数组」）。
        /// <para>H-39：局部更新只能用 <c>SetVertices/SetNormals/SetUVs/SetColors(arr, start, length)</c>。
        /// 先前写的 <c>SetVertexBufferData</c> 操作的是<b>裸顶点缓冲流</b>，必须先 <c>SetVertexBufferParams</c>
        /// 声明布局；用 <c>mesh.vertices</c> 一类常规 API 建的 mesh 没有该布局，实测抛
        /// 「SetVertexData() with out-of-bounds arguments; would need to copy 1452 bytes at offset 0 into a
        /// <b>0 byte buffer</b>」——0 字节缓冲正是"流未声明"的证据。这类错误只在增量路径触发，
        /// 冷启动看不出来，所以必须由"第二次同拓扑写入"的用例覆盖。</para>
        /// <para>默认 16bit 索引；单 chunk 顶点 ≈ (chunk/显示步+1)² ≪ 65k。超限时显式升 UInt32
        /// ——否则索引被截断，表现为大面积破面。</para>
        /// </summary>
        public static void ApplyToMesh(ChunkGeometry geom, Mesh mesh)
        {
            if (geom == null || mesh == null) return;

            if (mesh.vertexCount == geom.VertexCount && geom.VertexCount > 0)
            {
                mesh.SetVertices(geom.Positions, 0, geom.Positions.Length);
                mesh.SetNormals(geom.Normals, 0, geom.Normals.Length);
                mesh.SetUVs(0, geom.Uvs, 0, geom.Uvs.Length);
                mesh.SetColors(geom.Colors, 0, geom.Colors.Length);
                mesh.triangles = geom.Triangles;
                mesh.RecalculateBounds();
                return;
            }

            mesh.Clear(false);
            mesh.indexFormat = geom.VertexCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = geom.Positions;
            mesh.normals = geom.Normals;
            mesh.uv = geom.Uvs;
            mesh.colors32 = geom.Colors;
            mesh.triangles = geom.Triangles;
            mesh.RecalculateBounds();
            mesh.MarkDynamic();
        }
    }
}
