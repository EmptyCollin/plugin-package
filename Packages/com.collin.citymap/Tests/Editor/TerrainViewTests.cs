using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Collin.CityMap.Tests
{
    /// <summary>
    /// Modules/01 §3.3 显示层（chunk mesh）的 EditMode 断言。
    /// <para>显示层最危险的失败是"画面看着对、口径其实错"：顶点取错坐标系、法线用了几何平均、
    /// 绕序反了导致背面剔除、chunk 拼缝错位 —— 这些在肉眼下全都是"一片地形"。
    /// 所以断言一律锚在<b>可独立解析计算的事实</b>上，而不是调用实现自己的函数再比一遍（自反断言恒真）。</para>
    /// <para>核心手法：把高度场填成<b>解析平面</b> <c>h = A·x + B·z</c>。
    /// 双线性采样对线性函数没有插值误差 ⇒ 顶点 y 必须等于 <c>A·x + B·z</c>（绝对精度，非相对）；
    /// 差分法线也必须等于解析法线 <c>normalize(-A,1,-B)</c>。两者同时成立，才说明显示层
    /// 既取对了世界坐标，又守住了"与采样同口径"的法线约定（01 §3.3）。</para>
    /// </summary>
    public class TerrainViewTests
    {
        /// <summary>与 TerrainDataTests 同纪律：所有 Object 一律 Track，由 TearDown 统一销毁，
        /// 否则 CityTerrain 残留在 IntentRegistry 会污染后续用例的 Tracker 基线。</summary>
        readonly List<Object> _spawned = new List<Object>(16);

        [TearDown]
        public void TearDown()
        {
            // 静态桥必须复原：面板驱动路径依赖 ActiveContext，留着会把下一个用例带进错误分支
            CityRuntime.Deactivate();
            for (var i = _spawned.Count - 1; i >= 0; i--)
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i], true);
            _spawned.Clear();
        }

        T Track<T>(T o) where T : Object { _spawned.Add(o); return o; }

        // ───────────────────── 显示参数对齐（01 §3.3 chunk 边界） ─────────────────────

        [Test]
        public void Chunk_Size_Snaps_To_Display_Step_Multiple()
        {
            // 显示步长 8m（2km 档：逻辑 2m × 4）
            // H-41：对齐必须<b>向上取整</b>。用 RoundToInt 时 100/8=12.5 的归整依赖
            // 具体舍入实现（midpoint 规则可能给 12 也可能给 13），跨机器会切出不同数量 chunk
            // = 破坏"同参数同结果"的地基承诺。向上取整同时符合"至少要这么大一块"的用户直觉。
            Assert.AreEqual(104f, TerrainView.EffectiveChunkSize(100f, 8f), 1e-4f,
                "100m 必须向上对齐到 8 的整数倍（13×8=104），否则 chunk 边界落不到显示网格线上");
            Assert.GreaterOrEqual(TerrainView.EffectiveChunkSize(100f, 8f), 100f,
                "对齐结果不得小于请求值（变小 = 用户要的块被切细）");
            Assert.AreEqual(8f, TerrainView.EffectiveChunkSize(1f, 8f), 1e-4f, "下限不得小于一个显示步");
            Assert.AreEqual(128f, TerrainView.EffectiveChunkSize(128f, 8f), 1e-4f, "已是整数倍时必须原样");

            var ratio = TerrainView.SnapRatio(100f, 8f);
            Assert.AreEqual(13, ratio);
            Assert.AreEqual(16, TerrainView.SnapRatio(121f, 8f), "121/8=15.125 → 16（向上）");
        }

        [Test]
        public void Chunk_Vertex_Rect_Covers_Full_Map_And_Clips_At_Edge()
        {
            var grid = new CityGrid(600f, 2f, 8f, 20f, 100f, 60f);
            // 探针安全域 = 四边各内缩一个逻辑格，跨度 (Columns-2)×cellSize = 298×2 = 596m；
            // 显示点数 = floor(596/8)+1 = 75（H-40 第二次修正后的口径）
            var count = TerrainView.DisplayIndexCount(in grid);
            Assert.AreEqual(75, count, "先自证图幅/档位/显示步长关系，否则后面的断言无意义");

            var chunk = TerrainView.EffectiveChunkSize(128f, grid.DisplayStep);   // 128
            var per = TerrainView.ChunksPerSide(grid.WorldSize, chunk);
            Assert.AreEqual(5, per, "600 / 128 → 向上取整 = 5");

            // 首 chunk：索引 0..16（128/8 = 16 步）
            Assert.IsTrue(TerrainView.ChunkVertexRect(in grid, new Vector2Int(0, 0), chunk,
                out var i0, out var i1, out var j0, out var j1));
            Assert.AreEqual(0, i0); Assert.AreEqual(16, i1);
            Assert.AreEqual(17, i1 - i0 + 1, "闭区间顶点数 = 跨步数 + 1");

            // 末 chunk 被数据域截断：索引钳到 count-1，不再越界
            Assert.IsTrue(TerrainView.ChunkVertexRect(in grid, new Vector2Int(4, 4), chunk,
                out i0, out i1, out j0, out j1));
            Assert.AreEqual(64, i0);
            Assert.AreEqual(count - 1, i1, "末尾必须钳到最后一个数据域内索引（越界会采到被 clamp 的假坡度）");

            // 图外 chunk 不产出几何
            Assert.IsFalse(TerrainView.ChunkVertexRect(in grid, new Vector2Int(9, 9), chunk,
                out _, out _, out _, out _), "完全在图外的 chunk 必须返回 false（不得留旧地形）");
        }

        [Test]
        public void Degenerate_Chunk_Request_Is_Clamped_Not_Crashing()
        {
            var grid = new CityGrid(600f, 2f, 8f, 20f, 100f, 60f);

            // 默认实例的结构体（DisplayColumns=0）不得让显示层崩或产出 NaN
            CityGrid empty = default;
            Assert.IsFalse(TerrainView.ChunkVertexRect(in empty, Vector2Int.zero, 128f,
                out _, out _, out _, out _), "空格网必须判为不可建（CityGrid 是结构体，不能与 null 比）");
            Assert.AreEqual(0, TerrainView.ChunksTouching(in empty, 128f, null).Count);

            // 一个比图幅还大的 chunk：应退化成"整图 1 个 chunk"而不是 0 个
            var big = TerrainView.EffectiveChunkSize(5000f, grid.DisplayStep);
            Assert.AreEqual(1, TerrainView.ChunksPerSide(grid.WorldSize, big));
        }

        // ─────────────── 几何正确性：锚在解析平面上（非自反断言） ───────────────

        struct Plane { public float A, B; }                 // h = A·x + B·z

        /// <summary>把场填成解析平面。BaseHeights 与 Heights 都填：显示层读 Heights。</summary>
        static void FillPlane(TerrainField f, Plane p)
        {
            var g = f.Grid;
            for (var z = 0; z < g.Rows; z++)
            for (var x = 0; x < g.Columns; x++)
            {
                var c = g.CellCenterWorld(x, z);
                var h = p.A * c.x + p.B * c.y;
                f.Heights[z * g.Columns + x] = h;
                f.BaseHeights[z * g.Columns + x] = h;
            }
        }

        [Test]
        public void Plane_Field_Reproduces_Exact_Heights_And_Normals_On_Mesh()
        {
            var grid = new CityGrid(300f, 2f, 8f, 20f, 100f, 20f);
            var field = new TerrainField(grid);
            var p = new Plane { A = 0.25f, B = -0.15f };
            FillPlane(field, p);

            var vp = new TerrainView.ViewParams(
                TerrainView.EffectiveChunkSize(100f, grid.DisplayStep), 16f, true, TerrainView.SlopeRefDeg);
            var geom = TerrainView.BuildChunk(field, grid, Vector2Int.zero, vp);
            Assert.IsFalse(geom.IsEmpty);

            // 解析平面的精确法线
            var expectN = new Vector3(-p.A, 1f, -p.B).normalized;
            var do0 = TerrainView.DisplayOrigin(in grid);

            var worstY = 0f;
            var worstN = 0f;
            for (var i = 0; i < geom.VertexCount; i++)
            {
                var v = geom.Positions[i];

                // ① 顶点必须落在"首格中心 + 索引 × 显示步长"的网格线上（H-40 修正后的基准）
                var ii = geom.OriginIndex.x + i % geom.VertCount.x;
                var jj = geom.OriginIndex.y + i / geom.VertCount.x;
                Assert.AreEqual(do0.x + ii * grid.DisplayStep, v.x, 1e-4f,
                    "X 必须以首格中心为基准按显示步长取点，不能落在图幅角网格线上");
                Assert.AreEqual(do0.y + jj * grid.DisplayStep, v.z, 1e-4f);

                // ② 顶点必须在<b>差分探针安全域</b>内（H-40：四边各内缩一个逻辑格）。
                //    顶点自己落在数据域内还不够 —— GetNormal 是中心差分（探针 = ±cellSize/2），
                //    探针越界同样被 clamp，实测残余 7.96°（= atan(0.2915) − atan(0.1458)，有效梯度减半）。
                var lo = grid.Origin + new Vector2(grid.CellSize, grid.CellSize);
                var hi = grid.Origin + new Vector2((grid.Columns - 1) * grid.CellSize,
                                                   (grid.Rows - 1) * grid.CellSize);
                Assert.GreaterOrEqual(v.x, lo.x - 1e-4f, "顶点及其差分探针不得越出左边界（越出即被 clamp）");
                Assert.LessOrEqual(v.x, hi.x + 1e-4f, "顶点及其差分探针不得越出右边界");
                Assert.GreaterOrEqual(v.z, lo.y - 1e-4f, "同理，Z 方向探针不得越界");
                Assert.LessOrEqual(v.z, hi.y + 1e-4f);

                // ③ 高度与法线：数据域内的线性场，双线性与中心差分都<b>无误差</b>
                //    ⇒ 对<b>每一个</b>顶点做绝对比较（不需要 edge/inner 分档，那是在迁就 clamp）
                var expectH = p.A * v.x + p.B * v.z;
                worstY = Mathf.Max(worstY, Mathf.Abs(v.y - expectH));
                worstN = Mathf.Max(worstN, Vector3.Angle(expectN, geom.Normals[i]));

                // ④ UV = 世界 XZ / tileScale（世界映射，接缝不拉伸）
                Assert.AreEqual(v.x / 16f, geom.Uvs[i].x, 1e-4f);
                Assert.AreEqual(v.z / 16f, geom.Uvs[i].y, 1e-4f);
            }

            Assert.Greater(geom.VertexCount, 60, "样本量必须足够，否则本用例只测了几个点");
            Assert.Less(worstY, 1e-4f,
                $"每个顶点高度都必须等于解析平面值（实得最大偏差 {worstY:0.######}m）");
            Assert.Less(worstN, 0.05f,
                $"每个顶点法线都必须等于解析法线（实得最大 {worstN:0.####}°；若显示层改用 RecalculateNormals 这里必红）");
        }

        [Test]
        public void Triangle_Winding_Makes_Surface_Face_Up()
        {
            var grid = new CityGrid(200f, 2f, 8f, 20f, 100f, 10f);
            var field = new TerrainField(grid);
            FillPlane(field, new Plane { A = 0.1f, B = 0.1f });

            var vp = new TerrainView.ViewParams(
                TerrainView.EffectiveChunkSize(120f, grid.DisplayStep), 16f, false, TerrainView.SlopeRefDeg);
            var geom = TerrainView.BuildChunk(field, grid, Vector2Int.zero, vp);

            Assert.AreEqual(geom.VertCount.x * geom.VertCount.y, geom.VertexCount);
            Assert.AreEqual((geom.VertCount.x - 1) * (geom.VertCount.y - 1) * 6, geom.Triangles.Length,
                "三角形数必须等于 quad 数 × 3 顶点 × 2 三角");

            // 绕序反了 = 背面剔除后地形"从上面看不见"，是显示层最刺眼的故障，必须静态断言
            var up = Vector3.up;
            var minDot = 1f;
            var outOfRange = 0;
            for (var t = 0; t < geom.Triangles.Length; t += 3)
            {
                var ia = geom.Triangles[t];
                var ib = geom.Triangles[t + 1];
                var ic = geom.Triangles[t + 2];
                if (ia < 0 || ib < 0 || ic < 0 || ia >= geom.VertexCount
                    || ib >= geom.VertexCount || ic >= geom.VertexCount) { outOfRange++; continue; }

                var a = geom.Positions[ia]; var b = geom.Positions[ib]; var c = geom.Positions[ic];
                var gn = Vector3.Cross(b - a, c - a).normalized;
                minDot = Mathf.Min(minDot, Vector3.Dot(gn, up));
            }
            Assert.AreEqual(0, outOfRange, "不得有越界索引（越界在 Unity 里表现为随机破面而非报错）");
            Assert.Greater(minDot, 0.9f, $"所有三角形几何法线必须朝上（最小点积 {minDot:0.###}）");
        }

        [Test]
        public void Slope_Colors_Are_Banded_And_Monotonic()
        {
            var flat = TerrainView.SlopeColor(0f, TerrainView.SlopeRefDeg);
            var mid = TerrainView.SlopeColor(TerrainView.SlopeRefDeg * 0.5f, TerrainView.SlopeRefDeg);
            var steep = TerrainView.SlopeColor(90f, TerrainView.SlopeRefDeg);      // 超参考值必须钳住

            // 平 = 绿占优；陡 = 红占优（与坡度着色的直觉一致，用户在 SceneView 里能直接读懂）
            Assert.Greater(flat.g, flat.r, "平地顶点色应偏绿");
            Assert.Greater(steep.r, steep.g, "陡地顶点色应偏红");

            // H-43："越陡越红"必须用<b>红绿差</b>表达，不能用红分量或绿分量单独递增表达。
            // 我先前两种写法都错了：色带中段锚是<b>土黄</b>（黄 = 红 + 绿同时偏高），
            // 实测 r = 82 → 184 → 158（中点达峰后回落）、g = 115 → 158 → 56（先升后降），
            // 两个分量都<b>非单调</b>。可单调的只有对比度 r−g：−33 → 26 → 102。
            // 判据写错时人会本能地去调锚点颜色来"凑绿" —— 那是被自己的断言牵着改设计。
            var dFlat = (int)flat.r - flat.g;
            var dMid = (int)mid.r - mid.g;
            var dSteep = (int)steep.r - steep.g;
            Assert.Less(dFlat, dMid, "红绿对比必须随坡度单调增强（否则调试着色没有信息量）");
            Assert.Less(dMid, dSteep, "对比增强必须持续到色带末段");
            Assert.Less(dFlat, 0, "色带必须跨越【偏绿→偏红】的分界，否则全程同色系、看不出差异");
            Assert.Greater(dSteep, 0, "同上：陡端必须已翻到偏红");

            // 三段两两可区分（撞色 = 着色失效）
            Assert.AreNotEqual(flat, mid);
            Assert.AreNotEqual(mid, steep);

            // 钳制：超过参考值不得继续变化（否则不同坡度范围会撞色）
            Assert.AreEqual(steep.r, TerrainView.SlopeColor(400f, TerrainView.SlopeRefDeg).r);
            Assert.AreEqual(steep.g, TerrainView.SlopeColor(400f, TerrainView.SlopeRefDeg).g);

            // 钳制：超过参考值不得继续变化（否则不同坡度范围会撞色）
            Assert.AreEqual(steep.r, TerrainView.SlopeColor(400f, TerrainView.SlopeRefDeg).r);
        }

        [Test]
        public void Slope_Colors_Off_Writes_White_VertexColors()
        {
            var grid = new CityGrid(120f, 2f, 8f, 20f, 100f, 10f);
            var field = new TerrainField(grid);
            FillPlane(field, new Plane { A = 0.5f, B = 0f });

            var on = TerrainView.BuildChunk(field, grid, Vector2Int.zero,
                new TerrainView.ViewParams(80f, 16f, true, TerrainView.SlopeRefDeg));
            var off = TerrainView.BuildChunk(field, grid, Vector2Int.zero,
                new TerrainView.ViewParams(80f, 16f, false, TerrainView.SlopeRefDeg));

            for (var i = 0; i < off.VertexCount; i++)
            {
                Assert.AreEqual(255, off.Colors[i].r);
                Assert.AreEqual(255, off.Colors[i].g);
                Assert.AreEqual(255, off.Colors[i].b);
            }
            Assert.AreNotEqual(on.Colors[0], off.Colors[0], "斜 26° 的场开着色带必须不是白色，否则开关失效");
        }

        // ───────────────────── 脏区域 → chunk 交集（增量重建的合法性） ─────────────────────

        [Test]
        public void Dirty_Region_Maps_To_Minimal_Chunk_Set()
        {
            var grid = new CityGrid(600f, 2f, 8f, 20f, 100f, 60f);
            var chunk = TerrainView.EffectiveChunkSize(128f, grid.DisplayStep);
            var per = TerrainView.ChunksPerSide(grid.WorldSize, chunk);

            // null = 全图：必须等于 per²，这是"全量重建"的判据
            Assert.AreEqual(per * per, TerrainView.ChunksTouching(in grid, chunk, null).Count);

            // 一个局限在某处的小盒：命中的 chunk 必须远少于全图（否则增量重建名不副实）
            var small = new Bounds(new Vector3(150f, 0f, 150f), new Vector3(30f, 1f, 30f));
            var hit = TerrainView.ChunksTouching(in grid, chunk, small);
            Assert.Greater(hit.Count, 0, "小盒至少要命中一个 chunk，否则改了参数画面永远不动");
            Assert.Less(hit.Count, per * per, $"局部改动不得触发全图重建（实得 {hit.Count}/{per * per}）");
            foreach (var c in hit)
            {
                Assert.GreaterOrEqual(c.x, 0); Assert.Less(c.x, per);
                Assert.GreaterOrEqual(c.y, 0); Assert.Less(c.y, per);
                // 命中的 chunk 必须与"脏盒 + 真实余量"相交。余量口径 = <b>一个显示步</b>（H-42），
                // 不是"一整个 chunk"：阶段上报的包围盒已含约束过渡带，显示层只需再外扩影响半径。
                // 判据若写得比实现宽松，"每个小改动重建 9 块"这类过度命中就检不出来。
                var m = grid.DisplayStep;
                var x0 = c.x * chunk; var x1 = x0 + chunk;
                var z0 = c.y * chunk; var z1 = z0 + chunk;
                Assert.IsTrue(x1 >= small.min.x - m && x0 <= small.max.x + m
                           && z1 >= small.min.z - m && z0 <= small.max.z + m,
                           $"chunk {c} 与脏盒（含 {m:0}m 余量）不相交却被重建");
            }

            // 退化盒（点）：只命中它所在的那一个 chunk
            var point = new Bounds(new Vector3(300f, 0f, 300f), Vector3.zero);
            var hitPoint = TerrainView.ChunksTouching(in grid, chunk, point);
            Assert.AreEqual(1, hitPoint.Count, $"零尺寸盒应只命中自身所在 chunk（实得 {hitPoint.Count}）");

            // 无重复
            var set = new HashSet<Vector2Int>(hit);
            Assert.AreEqual(hit.Count, set.Count, "不得重复返回同一 chunk（重复重建 = 浪费且不幂等）");
        }

        // ───────────────────── 写进 Mesh 的两条路径（01 §5.4） ─────────────────────

        [Test]
        public void Apply_To_Mesh_Keeps_Topology_On_Resize_And_Grows_When_Needed()
        {
            var grid = new CityGrid(200f, 2f, 8f, 20f, 100f, 10f);
            var field = new TerrainField(grid);
            FillPlane(field, new Plane { A = 0.2f, B = 0.1f });

            var mesh = Track(new Mesh { name = "t_apply" });
            var vp = new TerrainView.ViewParams(
                TerrainView.EffectiveChunkSize(80f, grid.DisplayStep), 16f, true, TerrainView.SlopeRefDeg);
            var g1 = TerrainView.BuildChunk(field, grid, Vector2Int.zero, vp);

            TerrainView.ApplyToMesh(g1, mesh);
            Assert.AreEqual(g1.VertexCount, mesh.vertexCount);
            Assert.AreEqual(g1.Triangles.Length, mesh.triangles.Length);
            Assert.Greater(mesh.bounds.extents.x, 0f, "必须重算包围盒，否则整块会被视锥剔除掉");

            // 同拓扑第二次写入：走缓冲更新（不清不重建），顶点数保持不变
            FillPlane(field, new Plane { A = -0.2f, B = 0.1f });
            var g2 = TerrainView.BuildChunk(field, grid, Vector2Int.zero, vp);
            TerrainView.ApplyToMesh(g2, mesh);
            Assert.AreEqual(g1.VertexCount, mesh.vertexCount, "同拓扑不得重建数组");
            Assert.Less(mesh.vertices[5].y, g1.Positions[5].y,
                "坡向翻转后顶点必须真的更新（缓冲更新路径若写错槽位，这里就会红）");

            // 换更大 chunk → 拓扑变化 → 必须走 Clear + 全量重建
            var vpBig = new TerrainView.ViewParams(
                TerrainView.EffectiveChunkSize(160f, grid.DisplayStep), 16f, true, TerrainView.SlopeRefDeg);
            var g3 = TerrainView.BuildChunk(field, grid, Vector2Int.zero, vpBig);
            TerrainView.ApplyToMesh(g3, mesh);
            Assert.AreEqual(g3.VertexCount, mesh.vertexCount, "拓扑变化必须重建顶点数组");
        }

        [Test]
        public void Mesh_Grows_To_Uint32_Only_When_Vertices_Exceed_16bit_Index()
        {
            var grid = new CityGrid(200f, 2f, 8f, 20f, 100f, 10f);
            var field = new TerrainField(grid);
            FillPlane(field, new Plane { A = 0.05f, B = 0f });

            // 正常 chunk：默认 16bit 索引即可（省一半带宽）
            var mesh = Track(new Mesh { name = "t_fmt" });
            TerrainView.ApplyToMesh(TerrainView.BuildChunk(field, grid, Vector2Int.zero,
                new TerrainView.ViewParams(80f, 16f, false, TerrainView.SlopeRefDeg)), mesh);
            Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt16, mesh.indexFormat);
            mesh.Clear(false);

            // 人为构造 > 65k 顶点的几何：必须升 UInt32，否则索引被截断 = 大面积破面
            var big = new TerrainView.ChunkGeometry
            {
                Positions = new Vector3[65_001],
                Normals = new Vector3[65_001],
                Uvs = new Vector2[65_001],
                Colors = new Color32[65_001],
                Triangles = new int[3],
            };
            big.Positions[0] = Vector3.zero;
            big.Positions[1] = Vector3.right;
            big.Positions[2] = Vector3.forward;
            big.Normals[0] = big.Normals[1] = big.Normals[2] = Vector3.up;
            big.Triangles[0] = 0; big.Triangles[1] = 1; big.Triangles[2] = 2;

            TerrainView.ApplyToMesh(big, mesh);
            Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt32, mesh.indexFormat,
                "超 65k 顶点若不升 32bit 索引，破面会表现为随机现象、极难定位");
        }

        // ─────────────── 宿主组件端到端（编辑期"画面真的动起来"的唯一证据） ───────────────

        struct ViewRig
        {
            public CityProject Project;
            public TerrainProfile Profile;
            public GameObject Go;
            public CityTerrain Terrain;
            public CityTerrainView View;
        }

        ViewRig MakeRig(ulong seed, float worldSize = 400f)
        {
            var proj = Track(ScriptableObject.CreateInstance<CityProject>());
            proj.EnsureRegistered();
            proj.SetWorldParams(worldSize, WorldScale.Small2km, 60f, 20f, 100f, 100f);
            proj.SetSeed(seed);

            var profile = Track(ScriptableObject.CreateInstance<TerrainProfile>());
            profile.SetBaseShape(18f, 520f, 4, 0.5f, 2f, 0.35f);
            profile.SetUrbanFlatness(0.5f);

            var go = Track(new GameObject("view_rig_" + seed));
            var terr = go.AddComponent<CityTerrain>();
            terr.SetProfile(profile);
            var view = go.AddComponent<CityTerrainView>();
            view.SetStandaloneProject(proj);

            return new ViewRig { Project = proj, Profile = profile, Go = go, Terrain = terr, View = view };
        }

        [Test]
        public void Standalone_Rig_Builds_Chunk_Children_And_Meshes()
        {
            var rig = MakeRig(4711);
            rig.View.RebuildNow();

            Assert.Greater(rig.View.ChunkObjectCount, 0, "自建路径必须产出 chunk");
            Assert.Greater(rig.View.EffectiveChunkSize, 0f);

            // chunk 必须挂在宿主子节点下（层级可读），且每个都有非空 mesh
            var root = rig.Go.transform.Find("terrain_chunks (derived)");
            Assert.IsNotNull(root, "派生 chunk 必须集中在一个子节点下，不污染场景根层级");
            Assert.Greater(root.childCount, 0);

            var totalVerts = 0;
            foreach (Transform ch in root)
            {
                var mf = ch.GetComponent<MeshFilter>();
                Assert.IsNotNull(mf, "chunk 必须有 MeshFilter");
                var mr = ch.GetComponent<MeshRenderer>();
                Assert.IsNotNull(mr, "chunk 必须有 MeshRenderer");
                Assert.IsNotNull(mf.sharedMesh, "chunk mesh 不得为空");
                Assert.Greater(mf.sharedMesh.vertexCount, 3, "有顶点才算画出东西");
                Assert.Greater(mf.sharedMesh.triangles.Length, 0);
                totalVerts += mf.sharedMesh.vertexCount;
            }
            Assert.Greater(totalVerts, 100, "整图顶点总量应与图幅/显示步长同量级");

            // 派生态不得入库：一旦被序列化，用户"什么都没改却要保存场景"，且重启后 mesh 引用会悬空
            Assert.IsTrue((rig.Go.hideFlags & HideFlags.HideAndDontSave) != 0
                          || root.gameObject.hideFlags == HideFlags.HideAndDontSave,
                          "派生根节点必须 HideAndDontSave（01 §6：显示 mesh 不落盘）");
            foreach (Transform ch in root)
                Assert.AreEqual(HideFlags.HideAndDontSave, ch.gameObject.hideFlags,
                    "chunk 物体不得进场景序列化");

            Assert.IsNotEmpty(rig.View.LastStatus);
            Debug.Log($"[M3] 自建路径：{rig.View.LastStatus}");
        }

        [Test]
        public void Panel_Context_Takes_Precedence_Over_Standalone_And_Only_Rebuilds_On_Commit()
        {
            var rig = MakeRig(9090);

            // 造一个"面板算出来的"上下文：故意填成与自建路径完全不同的高度场
            var authProject = rig.Project;
            var ctx = new PipelineContext(authProject);
            var field = TerrainBaseStage.EnsureField(ctx);
            FillPlane(field, new Plane { A = 0.4f, B = 0f });
            CityRuntime.Activate(ctx);
            rig.View.RefreshFromRuntime();

            var root = rig.Go.transform.Find("terrain_chunks (derived)");
            Assert.IsNotNull(root);
            var first = root.GetChild(0).GetComponent<MeshFilter>();
            var y0 = first.sharedMesh.vertices[1].y;
            var x0 = first.sharedMesh.vertices[1].x;
            Assert.AreEqual(0.4f * x0, y0, 1e-3f,
                "有权威上下文时必须显示面板算出的高度场，而不是自建路径重算的那份（否则出现两份世界）");

            // 序号未前进 ⇒ 不得重复重建（同帧合并/防抖的判据）。
            // 必须用 TickOnce（模拟常规一帧），不能用 RebuildNow —— 后者语义就是"强制重建"，
            // 拿它断言"不该重建"是自相矛盾，只会恒红。
            var before = RigStateDigest(first.sharedMesh);
            rig.View.TickOnce();
            var after = RigStateDigest(first.sharedMesh);
            Assert.AreEqual(before, after, "提交序号未前进时不得重建（防止 LateUpdate 空转刷性能）");

            // 提交一次 ⇒ 画面必须跟着变
            FillPlane(field, new Plane { A = -0.4f, B = 0f });
            CityRuntime.MarkCommitted(null);
            rig.View.RefreshFromRuntime();
            var flipped = first.sharedMesh.vertices[1];
            Assert.AreEqual(-0.4f * flipped.x, flipped.y, 1e-3f, "Commit 后显示层必须反映新高度场");
        }

        static string RigStateDigest(Mesh m)
        {
            var v = m.vertices;
            return v.Length + ":" + (v.Length > 1 ? v[1].ToString("F5") : "-");
        }

        [Test]
        public void Hiding_Terrain_Destroys_Chunks_And_Showing_Rebuilds_Them()
        {
            var rig = MakeRig(3300);
            rig.View.RebuildNow();
            var n1 = rig.View.ChunkObjectCount;
            Assert.Greater(n1, 0);

            rig.View.SetShowTerrain(false);
            rig.View.RebuildNow();
            Assert.AreEqual(0, rig.View.ChunkObjectCount,
                "关闭显示必须销毁 chunk 物体（只隐藏不销毁会留下一堆看不见的渲染器占内存）");
            Assert.IsNull(rig.Go.transform.Find("terrain_chunks (derived)"), "根节点也必须回收");

            rig.View.SetShowTerrain(true);
            rig.View.RebuildNow();
            Assert.Greater(rig.View.ChunkObjectCount, 0, "重新打开必须恢复显示");
        }

        [Test]
        public void Changing_Chunk_Size_Rebuilds_Without_Leftover_Old_Chunks()
        {
            var rig = MakeRig(6400, 400f);
            rig.View.SetChunkSizeMeters(200f);
            rig.View.RebuildNow();
            var coarse = rig.View.ChunkObjectCount;
            Assert.Greater(coarse, 0);

            // 变细：chunk 数必须增加，且旧 chunk 不得残留成"两层地形叠在一起"
            rig.View.SetChunkSizeMeters(100f);
            rig.View.RebuildNow();
            var fine = rig.View.ChunkObjectCount;
            Assert.Greater(fine, coarse, $"更小的 chunk 必须产生更多块（{coarse} → {fine}）");

            rig.View.SetChunkSizeMeters(400f);
            rig.View.RebuildNow();
            var coarser = rig.View.ChunkObjectCount;
            Assert.Less(coarser, fine, "图幅/尺寸缩小后多余 chunk 必须回收，否则残留旧地形");
            Debug.Log($"[M3] chunk 数随尺寸变化：200m={coarse} 100m={fine} 400m={coarser}");
        }

        [Test]
        public void Missing_Data_Source_Fails_Verbose_Instead_Of_Blank()
        {
            // 只给 Profile、不给任何上下文/项目：必须给出可读原因，而不是静静显示一片空白
            var go = Track(new GameObject("view_nodata"));
            var terr = go.AddComponent<CityTerrain>();
            var profile = Track(ScriptableObject.CreateInstance<TerrainProfile>());
            terr.SetProfile(profile);
            var view = go.AddComponent<CityTerrainView>();
            CityRuntime.Deactivate();

            view.RebuildNow();
            Assert.IsNotEmpty(view.LastStatus);
            StringAssert.Contains("项目", view.LastStatus,
                $"数据源缺失必须说明缺什么（实得：{view.LastStatus}）");

            // 有项目但 Profile 为空（另一条缺失分支）
            var proj2 = Track(ScriptableObject.CreateInstance<CityProject>());
            proj2.EnsureRegistered();
            proj2.SetWorldParams(300f, WorldScale.Small2km, 60f, 20f, 100f, 100f);
            var go2 = Track(new GameObject("view_noprofile"));
            go2.AddComponent<CityTerrain>();
            var view2 = go2.AddComponent<CityTerrainView>();
            view2.SetStandaloneProject(proj2);
            view2.RebuildNow();
            StringAssert.Contains("TerrainProfile", view2.LastStatus,
                $"缺 Profile 的分支必须单独报因（实得：{view2.LastStatus}）");
        }
    }
}
