using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Collin.Stretch
{
    /// <summary>
    /// 结果网格生成器：对**任意**源网格（含多材质子网格）、边界切在**任意**位置均成立（AGENTS.md §0.1 通用性）。
    /// 三角形用边界线半空间裁剪分片（Sutherland–Hodgman），每片刚性归入所在段：
    /// fixed 段整体平移，repeatable 段复制 repeat 份；位置/UV/法线/切线/色在切口线性插值。
    /// 逐子网格处理并**保持 subMeshCount 与材质槽对应**（FBX 多材质模型必需）。
    /// </summary>
    public static class StretchMeshBuilder
    {
        private const float Eps = 1e-5f;

        /// <summary>源网格必须可读且含 UV0。失败原因经 error 返回。</summary>
        public static bool CanProcess(Mesh source, out string error)
        {
            if (source == null) { error = "源网格为空"; return false; }
            if (!source.isReadable) { error = "源网格不可读：导入设置勾选 Read/Write（模型）或确保非烘焙网格"; return false; }
            if (source.uv == null || source.uv.Length != source.vertexCount)
            {
                error = "源网格缺少 UV0"; return false;
            }
            int totalTris = 0;
            for (int s = 0; s < source.subMeshCount; s++) totalTris += source.GetTriangles(s).Length;
            if (totalTris == 0)
            {
                error = "源网格没有三角形"; return false;
            }
            error = null;
            return true;
        }

        /// <summary>单个带属性的顶点（沿轴坐标由 pos 分量派生）。</summary>
        private struct V
        {
            public Vector3 p;
            public Vector2 uv;
            public Vector3 n;
            public Vector4 t;
            public Color32 c;
        }

        /// <summary>构建结果网格。失败静默返回 null——日志归调用方（§1.6b 门禁友好）。</summary>
        public static Mesh Build(Mesh source, StretchProfile profile, IList<int> repeats)
        {
            if (source == null || profile == null) return null;
            if (!CanProcess(source, out _)) return null;
            float bmin = BoundsMin(source, profile.axis);
            float bmax = BoundsMax(source, profile.axis);
            if (profile.Validate(bmin, bmax) != null) return null;
            if (profile.ValidateRepeats(repeats) != null) return null;

            var verts = source.vertices;
            var uvs = source.uv;
            var normals = source.normals;
            var tangents = source.tangents;
            var colors = source.colors32;
            bool hasN = normals != null && normals.Length == verts.Length;
            bool hasT = tangents != null && tangents.Length == verts.Length;
            bool hasC = colors != null && colors.Length == verts.Length && colors.Length > 0;
            int ax = (int)profile.axis;
            int segCount = profile.segmentTypes.Count;
            int subCount = Mathf.Max(1, source.subMeshCount);

            var vertPool = new List<Vector3>(verts.Length * 2);
            var uvPool = new List<Vector2>(verts.Length * 2);
            var nPool = new List<Vector3>(verts.Length * 2);
            var tPool = new List<Vector4>(verts.Length * 2);
            var cPool = new List<Color32>(verts.Length * 2);
            var idxPerSub = new List<int>[subCount];
            for (int s = 0; s < subCount; s++) idxPerSub[s] = new List<int>();

            // 段基准位移（k=0）预计算
            var segOffset = new float[segCount];
            var segCopies = new int[segCount];
            for (int i = 0; i < segCount; i++)
            {
                segOffset[i] = profile.ExtensionBelow(profile.sliceBounds[i], repeats);
                segCopies[i] = profile.TypeAt(i) == SegmentType.Repeatable
                    ? StretchProfile.RepeatAt(repeats, i) : 1;
            }

            V MakeV(int srcIndex)
            {
                var v = new V();
                v.p = verts[srcIndex];
                v.uv = uvs[srcIndex];
                v.n = hasN ? normals[srcIndex] : Vector3.up;
                v.t = hasT ? tangents[srcIndex] : Vector4.zero;
                v.c = hasC ? colors[srcIndex] : new Color32(255, 255, 255, 255);
                return v;
            }
            static float Coord(V v, int ax) => v.p[ax];
            V Lerp(V a, V b, float s)
            {
                var r = new V();
                r.p = Vector3.Lerp(a.p, b.p, s);
                r.uv = Vector2.Lerp(a.uv, b.uv, s);
                r.n = Vector3.Lerp(a.n, b.n, s).normalized;
                r.t = Vector4.Lerp(a.t, b.t, s);
                r.c = Color.Lerp(a.c, b.c, s);
                return r;
            }

            // Sutherland–Hodgman 半空间裁剪：keepBelow=true 保留 coord<=cut
            List<V> Clip(List<V> poly, float cut, bool keepBelow)
            {
                var outp = new List<V>(poly.Count + 1);
                int n0 = poly.Count;
                if (n0 < 3) return outp;
                for (int i = 0; i < n0; i++)
                {
                    var cur = poly[i];
                    var prev = poly[(i + n0 - 1) % n0];
                    float cc = Coord(cur, ax), pc = Coord(prev, ax);
                    bool curIn = keepBelow ? cc <= cut + Eps : cc >= cut - Eps;
                    bool prevIn = keepBelow ? pc <= cut + Eps : pc >= cut - Eps;
                    if (curIn)
                    {
                        if (!prevIn)
                        {
                            float span = cc - pc;
                            float s = Mathf.Abs(span) < 1e-9f ? 0f : (cut - pc) / span;
                            outp.Add(Lerp(prev, cur, s));
                        }
                        outp.Add(cur);
                    }
                    else if (prevIn)
                    {
                        float span = cc - pc;
                        float s = Mathf.Abs(span) < 1e-9f ? 0f : (cut - pc) / span;
                        outp.Add(Lerp(prev, cur, s));
                    }
                }
                return outp;
            }

            void Emit(V v, float offset, List<int> idx, int sm)
            {
                var p = v.p;
                p[ax] += offset;
                vertPool.Add(p);
                uvPool.Add(v.uv);
                if (hasN) nPool.Add(v.n);
                if (hasT) tPool.Add(v.t);
                if (hasC) cPool.Add(v.c);
                idx.Add(vertPool.Count - 1);
            }

            // 逐子网格：材质槽结构原样保留（sub s 的三角形写入结果 sub s）
            for (int sm = 0; sm < subCount; sm++)
            {
                var tris = source.GetTriangles(sm);
                var idx = idxPerSub[sm];
                for (int tri = 0; tri + 2 < tris.Length; tri += 3)
                {
                    var v0 = MakeV(tris[tri]);
                    var v1 = MakeV(tris[tri + 1]);
                    var v2 = MakeV(tris[tri + 2]);
                    float c0 = Coord(v0, ax), c1 = Coord(v1, ax), c2 = Coord(v2, ax);
                    float tmin = Mathf.Min(c0, Mathf.Min(c1, c2));
                    float tmax = Mathf.Max(c0, Mathf.Max(c1, c2));
                    tmin = Mathf.Clamp(tmin, bmin, bmax);
                    tmax = Mathf.Clamp(tmax, bmin, bmax);

                    int sFirst = profile.SegmentIndexAt(Mathf.Clamp(tmin + Eps, bmin, bmax - Eps * 2));
                    int sLast = profile.SegmentIndexAt(Mathf.Clamp(tmax - Eps, bmin, bmax - Eps * 2));
                    if (sFirst < 0) sFirst = 0;
                    if (sLast < 0) sLast = segCount - 1;

                    if (sFirst == sLast)
                    {
                        // 快速路径：整三角形在一段闭区间内 → 原样输出
                        for (int k = 0; k < segCopies[sFirst]; k++)
                        {
                            float off = segOffset[sFirst] + k * profile.SegmentLength(sFirst);
                            Emit(v0, off, idx, sm); Emit(v1, off, idx, sm); Emit(v2, off, idx, sm);
                        }
                        continue;
                    }

                    // 通用路径：用 (tmin, tmax) 之间的内部边界裁剪成逐段分片
                    var boundaries = new List<float>();
                    foreach (var b in profile.sliceBounds)
                    {
                        if (b > tmin + Eps && b < tmax - Eps) boundaries.Add(b);
                    }

                    var remain = new List<V> { v0, v1, v2 };
                    var pieces = new List<List<V>>(boundaries.Count + 1);
                    foreach (var b in boundaries)
                    {
                        var below = Clip(remain, b, true);
                        var above = Clip(remain, b, false);
                        if (below.Count >= 3) pieces.Add(below);
                        remain = above;
                    }
                    if (remain.Count >= 3) pieces.Add(remain);

                    foreach (var piece in pieces)
                    {
                        float avg = 0f;
                        foreach (var v in piece) avg += Coord(v, ax);
                        avg /= piece.Count;
                        int seg = profile.SegmentIndexAt(Mathf.Clamp(avg, bmin, bmax - Eps * 2));
                        if (seg < 0) seg = segCount - 1;

                        for (int k = 0; k < segCopies[seg]; k++)
                        {
                            float off = segOffset[seg] + k * profile.SegmentLength(seg);
                            for (int i = 1; i + 1 < piece.Count; i++)
                            {
                                Emit(piece[0], off, idx, sm); Emit(piece[i], off, idx, sm); Emit(piece[i + 1], off, idx, sm);
                            }
                        }
                    }
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
            if (hasN) mesh.SetNormals(nPool);
            if (hasT) mesh.SetTangents(tPool);
            if (hasC) mesh.SetColors(cPool);
            mesh.subMeshCount = subCount;
            for (int sm = 0; sm < subCount; sm++)
            {
                mesh.SetTriangles(idxPerSub[sm].Count > 0 ? idxPerSub[sm].ToArray() : new int[0], sm);
            }
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

        public static float BoundsMin(Mesh m, StretchAxis axis) => m.bounds.min[(int)axis];
        public static float BoundsMax(Mesh m, StretchAxis axis) => m.bounds.max[(int)axis];
    }
}
