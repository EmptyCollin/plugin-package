using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Collin.Stretch
{
    /// <summary>
    /// 结果网格生成器（DESIGN D-3）：逐三角形按重心归段，整三角形刚性放置，
    /// UV/法线/切线/顶点色逐字拷贝——复制单元与原件像素级一致，杜绝巨型窗户。
    /// 结构来自 <see cref="StretchProfile"/>（共享 Model），倍数来自实例数组（§0.1 原则2）。
    /// </summary>
    public static class StretchMeshBuilder
    {
        /// <summary>源网格必须可读且含 UV0。失败原因经 error 返回。</summary>
        public static bool CanProcess(Mesh source, out string error)
        {
            if (source == null) { error = "源网格为空"; return false; }
            if (!source.isReadable) { error = "源网格不可读：导入设置勾选 Read/Write（模型）或确保非烘焙网格"; return false; }
            if (source.uv == null || source.uv.Length != source.vertexCount)
            {
                error = "源网格缺少 UV0"; return false;
            }
            if (source.triangles == null || source.triangles.Length == 0)
            {
                error = "源网格没有三角形"; return false;
            }
            error = null;
            return true;
        }

        /// <summary>构建结果网格。失败静默返回 null——日志归调用方（§1.6b 门禁友好）。</summary>
        public static Mesh Build(Mesh source, StretchProfile profile, IList<int> repeats)
        {
            if (source == null || profile == null) return null;
            if (!CanProcess(source, out _)) return null;
            float min = BoundsMin(source, profile.axis);
            float max = BoundsMax(source, profile.axis);
            if (profile.Validate(min, max) != null) return null;
            if (profile.ValidateRepeats(repeats) != null) return null;

            var verts = source.vertices;
            var uvs = source.uv;
            var normals = source.normals;
            var tangents = source.tangents;
            var colors = source.colors32;
            bool hasNormals = normals != null && normals.Length == verts.Length;
            bool hasTangents = tangents != null && tangents.Length == verts.Length;
            bool hasColors = colors != null && colors.Length == verts.Length && colors.Length > 0;

            var tris = source.triangles;
            int triCount = tris.Length / 3;
            var ax = (int)profile.axis;

            var vertPool = new List<Vector3>(verts.Length * 2);
            var uvPool = new List<Vector2>(verts.Length * 2);
            var normalPool = new List<Vector3>(verts.Length * 2);
            var tangentPool = new List<Vector4>(verts.Length * 2);
            var colorPool = new List<Color32>(verts.Length * 2);
            var indexOut = new List<int>(tris.Length * 2);

            for (int t = 0; t < triCount; t++)
            {
                int i0 = tris[t * 3], i1 = tris[t * 3 + 1], i2 = tris[t * 3 + 2];
                float cx = (verts[i0][ax] + verts[i1][ax] + verts[i2][ax]) / 3f;
                int seg = profile.SegmentIndexAt(cx);
                if (seg < 0) seg = 0;

                int repeats_ = profile.TypeAt(seg) == SegmentType.Repeatable
                    ? StretchProfile.RepeatAt(repeats, seg) : 1;

                for (int k = 0; k < repeats_; k++)
                {
                    float offset = profile.OffsetFor(seg, k, repeats);
                    int localBase = vertPool.Count;
                    AppendVert(vertPool, uvPool, normalPool, tangentPool, colorPool,
                        verts, uvs, normals, tangents, colors, hasNormals, hasTangents, hasColors, i0, ax, offset);
                    AppendVert(vertPool, uvPool, normalPool, tangentPool, colorPool,
                        verts, uvs, normals, tangents, colors, hasNormals, hasTangents, hasColors, i1, ax, offset);
                    AppendVert(vertPool, uvPool, normalPool, tangentPool, colorPool,
                        verts, uvs, normals, tangents, colors, hasNormals, hasTangents, hasColors, i2, ax, offset);
                    indexOut.Add(localBase);
                    indexOut.Add(localBase + 1);
                    indexOut.Add(localBase + 2);
                }
            }

            var mesh = new Mesh
            {
                name = source.name + "_stretch",
                indexFormat = vertPool.Count > 65535
                    ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(vertPool);
            mesh.SetUVs(0, uvPool);
            if (hasNormals) mesh.SetNormals(normalPool);
            if (hasTangents) mesh.SetTangents(tangentPool);
            if (hasColors) mesh.SetColors(colorPool);
            mesh.SetTriangles(indexOut, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>失败原因版本（供 UI/命令弹窗展示）。</summary>
        public static string FailReason(Mesh source, StretchProfile profile, IList<int> repeats)
        {
            if (source == null || profile == null) return "源网格或 Profile 为空";
            if (!CanProcess(source, out var err)) return err;
            float min = BoundsMin(source, profile.axis);
            float max = BoundsMax(source, profile.axis);
            var verr = profile.Validate(min, max);
            if (verr != null) return "结构校验未通过: " + verr;
            var rerr = profile.ValidateRepeats(repeats);
            if (rerr != null) return "倍数数据异常: " + rerr;
            return null;
        }

        private static void AppendVert(List<Vector3> vPool, List<Vector2> uvPool,
            List<Vector3> nPool, List<Vector4> tPool, List<Color32> cPool,
            Vector3[] verts, Vector2[] uvs, Vector3[] normals, Vector4[] tangents, Color32[] colors,
            bool hasN, bool hasT, bool hasC, int srcIndex, int ax, float offset)
        {
            var v = verts[srcIndex];
            v[ax] += offset;
            vPool.Add(v);
            uvPool.Add(uvs[srcIndex]);
            if (hasN) nPool.Add(normals[srcIndex]);
            if (hasT) tPool.Add(tangents[srcIndex]);
            if (hasC) cPool.Add(colors[srcIndex]);
        }

        public static float BoundsMin(Mesh m, StretchAxis axis) => m.bounds.min[(int)axis];
        public static float BoundsMax(Mesh m, StretchAxis axis) => m.bounds.max[(int)axis];
    }
}
