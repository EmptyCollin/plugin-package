using System.Collections.Generic;
using UnityEngine;

namespace Collin.StealthPlanner
{
    /// <summary>
    /// 把「某桶的逐格状态」烘成<b>合并后的网格</b>，供时间轴热区一次性绘制。
    /// <para>存在理由：11k 格若逐格 <c>DrawMesh</c> 会把 SceneView 拖垮，必须合并相邻同色格。
    /// 合并策略是「<b>按行把连续同状态格拼成一条横带</b>」，再按状态各自成组：
    /// 相邻同状态在<b>同行</b>时天然合并；跨行的同色块留作相邻条带。相邻同状态格<b>共享边</b>
    /// （非凸处不补缝），避免叠色发黑——这是「热区看着脏」的常见根因。</para>
    /// <para>纯 CPU，无场景依赖：输入是三张 <c>byte[]</c>（危险位掩码 + 本桶可达 + 累计可达），
    /// 因此可脱离 Play 态在 EditMode 断言。M2 只用到「危险热区」与「可达/累计可达」三类状态。</para>
    /// </summary>
    public static class HeatmapGrid
    {
        /// <summary>
        /// 把 <paramref name="dangerMask"/> 里 1 的格按行拼横带。横带彼此不重叠 ⇒ 单遍绘制不叠色。
        /// </summary>
        public static Mesh BuildDanger(VisibilityTable table, byte[] dangerMask)
        {
            var m = new Mesh { name = "HeatmapDanger" };
            if (table == null || table.DimX <= 0 || table.DimZ <= 0 || dangerMask == null
                || dangerMask.Length < table.CellCount)
            {
                return m;
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var cell = table.CellSize;
            var ox = table.Origin.x;
            var oz = table.Origin.z;

            for (var z = 0; z < table.DimZ; z++)
            {
                var runStart = -1;
                for (var x = 0; x <= table.DimX; x++)
                {
                    var on = x < table.DimX && dangerMask[z * table.DimX + x] != 0;
                    if (on && runStart < 0) runStart = x;
                    if (!on && runStart >= 0)
                    {
                        AppendBand(verts, tris, ox + runStart * cell, ox + x * cell,
                            oz + z * cell, oz + (z + 1) * cell);
                        runStart = -1;
                    }
                }
            }
            Apply(m, verts, tris);
            return m;
        }

        /// <summary>
        /// 把 <paramref name="reachableMask"/>（本桶可达）拼横带，抬高 <paramref name="y"/> 以浮于地面。
        /// </summary>
        public static Mesh BuildReachable(VisibilityTable table, byte[] reachableMask, float y)
        {
            var m = new Mesh { name = "HeatmapReachable" };
            if (table == null || table.DimX <= 0 || table.DimZ <= 0 || reachableMask == null
                || reachableMask.Length < table.CellCount)
            {
                return m;
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var cell = table.CellSize;
            var ox = table.Origin.x;
            var oz = table.Origin.z;

            for (var z = 0; z < table.DimZ; z++)
            {
                var runStart = -1;
                for (var x = 0; x <= table.DimX; x++)
                {
                    var on = x < table.DimX && reachableMask[z * table.DimX + x] != 0;
                    if (on && runStart < 0) runStart = x;
                    if (!on && runStart >= 0)
                    {
                        AppendBand(verts, tris, ox + runStart * cell, ox + x * cell,
                            oz + z * cell, oz + (z + 1) * cell, y);
                        runStart = -1;
                    }
                }
            }
            Apply(m, verts, tris);
            return m;
        }

        /// <summary>
        /// 把「某桶可达」与「累计可达并集」两层的<b>并集</b>拼成一个网格（M2 累计可达热区）。
        /// 两两共享同格时只画一条横带，避免叠色。
        /// </summary>
        public static Mesh BuildCumulative(VisibilityTable table, byte[] curBucket, byte[] everBucket, float y)
        {
            var m = new Mesh { name = "HeatmapCumulative" };
            if (table == null || table.DimX <= 0 || table.DimZ <= 0 || curBucket == null
                || everBucket == null || curBucket.Length < table.CellCount
                || everBucket.Length < table.CellCount)
            {
                return m;
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var cell = table.CellSize;
            var ox = table.Origin.x;
            var oz = table.Origin.z;

            for (var z = 0; z < table.DimZ; z++)
            {
                var runStart = -1;
                for (var x = 0; x <= table.DimX; x++)
                {
                    var on = x < table.DimX
                        && (curBucket[z * table.DimX + x] != 0 || everBucket[z * table.DimX + x] != 0);
                    if (on && runStart < 0) runStart = x;
                    if (!on && runStart >= 0)
                    {
                        AppendBand(verts, tris, ox + runStart * cell, ox + x * cell,
                            oz + z * cell, oz + (z + 1) * cell, y);
                        runStart = -1;
                    }
                }
            }
            Apply(m, verts, tris);
            return m;
        }

        static void Apply(Mesh m, List<Vector3> verts, List<int> tris)
        {
            if (verts.Count == 0)
            {
                m.Clear();
                return;
            }
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
        }

        static void AppendBand(List<Vector3> verts, List<int> tris,
                               float xMin, float xMax, float zMin, float zMax, float y = 0.02f)
        {
            // 单条横带 = 4 顶点、2 三角。 winding：法线朝上（+Y）。
            var v0 = verts.Count;
            verts.Add(new Vector3(xMin, y, zMin));
            verts.Add(new Vector3(xMax, y, zMin));
            verts.Add(new Vector3(xMax, y, zMax));
            verts.Add(new Vector3(xMin, y, zMax));
            tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 2);
            tris.Add(v0); tris.Add(v0 + 2); tris.Add(v0 + 3);
        }
    }
}
