using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stretch
{
    /// <summary>
    /// 结果网格缓存（DESIGN §8.2 P1）：拉伸结果由 (源网格, Profile, repeats) 唯一决定，
    /// 同键实例共享同一份 Mesh——万级实例、参数组合有限时内存骤降，且恢复 GPU Instancing。
    /// 引用计数 + LRU 上限逐出；源/profile 销毁时条目作废。域重载自动清空（重建廉价，缓存命中后不重建）。
    /// 分块渲染的块组（DESIGN P3）也经本缓存共享：键含 chunk 前缀，值为块数组。
    /// </summary>
    public static class StretchMeshCache
    {
        private struct Key
        {
            public int srcId;      // GetEntityId().GetHashCode()（域内稳定；引用有效性由 Alive 兜底）
            public int profileId;
            public int repeatsHash;
            public bool chunked;
        }

        private class Entry
        {
            public Key key;
            public Mesh mesh;                 // 全网格模式
            public ChunkedStretch.Chunk[] chunks; // 分块模式（可空）
            public Object srcRef;
            public Object profileRef;
            public int refs;
            public LinkedListNode<Entry> lru;
        }

        private const int MaxIdle = 64;
        private static readonly Dictionary<Key, Entry> _map = new Dictionary<Key, Entry>();
        private static readonly LinkedList<Entry> _lru = new LinkedList<Entry>();
        private static int _hits, _misses;

        public static int Count => _map.Count;
        public static int Hits => _hits;
        public static int Misses => _misses;

        public static void ResetStats() { _hits = 0; _misses = 0; }

        private static int HashRepeats(IList<int> reps)
        {
            unchecked
            {
                int h = (int)2166136261;
                if (reps != null)
                {
                    for (int i = 0; i < reps.Count; i++)
                    {
                        h = (h * 16777619) ^ reps[i];
                    }
                }
                return h;
            }
        }

        private static Key MakeKey(Mesh src, StretchProfile profile, IList<int> reps, bool chunked)
        {
            var k = new Key
            {
                srcId = src.GetEntityId().GetHashCode(),
                profileId = profile.GetEntityId().GetHashCode(),
                repeatsHash = HashRepeats(reps),
                chunked = chunked,
            };
            return k;
        }

        /// <summary>取（或建）全网格。返回 null 表示构建失败（不入缓存）。</summary>
        public static Mesh AcquireFull(Mesh src, StretchProfile profile, IList<int> reps)
        {
            if (src == null || profile == null) return null;
            var key = MakeKey(src, profile, reps, false);
            if (_map.TryGetValue(key, out var en) && Alive(en))
            {
                en.refs++;
                _lru.Remove(en.lru); en.lru = _lru.AddFirst(en);
                _hits++;
                return en.mesh;
            }
            _misses++;
            var mesh = StretchMeshBuilder.Build(src, profile, reps);
            if (mesh == null) return null;
            mesh.hideFlags = HideFlags.DontSave;
            Store(key, src, profile, mesh, null);
            return mesh;
        }

        /// <summary>取（或建）块组（分块渲染用；与 repeats 无关，块几何只依赖源+结构）。返回的数组为共享，禁止改动。</summary>
        public static ChunkedStretch.Chunk[] AcquireChunks(Mesh src, StretchProfile profile)
        {
            if (src == null || profile == null) return null;
            var key = MakeKey(src, profile, null, true);
            if (_map.TryGetValue(key, out var en) && Alive(en) && en.chunks != null)
            {
                en.refs++;
                _lru.Remove(en.lru); en.lru = _lru.AddFirst(en);
                _hits++;
                return en.chunks;
            }
            _misses++;
            var chunks = ChunkedStretch.BuildChunks(src, profile);
            if (chunks == null) return null;
            Store(key, src, profile, null, chunks);
            return chunks;
        }

        /// <summary>释放 Acquire 的引用。refs 归零后转入 LRU 待逐出（不立即销毁，利于参数来回切）。</summary>
        public static void ReleaseFull(Mesh mesh)
        {
            if (mesh == null) return;
            foreach (var kv in _map)
            {
                if (kv.Value.mesh == mesh)
                {
                    if (kv.Value.refs > 0) kv.Value.refs--;
                    return;
                }
            }
        }

        public static void ReleaseChunks(ChunkedStretch.Chunk[] chunks)
        {
            if (chunks == null) return;
            foreach (var kv in _map)
            {
                if (kv.Value.chunks == chunks)
                {
                    if (kv.Value.refs > 0) kv.Value.refs--;
                    return;
                }
            }
        }

        private static bool Alive(Entry en)
        {
            if (en.srcRef == null || en.profileRef == null)
            {
                Drop(en); // 源或 profile 已销毁
                return false;
            }
            return true;
        }

        private static void Store(Key key, Mesh src, StretchProfile profile, Mesh mesh, ChunkedStretch.Chunk[] chunks)
        {
            var en = new Entry
            {
                key = key,
                mesh = mesh,
                chunks = chunks,
                srcRef = src,
                profileRef = profile,
                refs = 1,
            };
            en.lru = _lru.AddFirst(en);
            _map[key] = en;
            Evict();
        }

        private static void Evict()
        {
            var node = _lru.Last;
            while (_map.Count > MaxIdle && node != null)
            {
                var next = node.Previous;
                if (node.Value.refs == 0) Trim(node.Value);
                node = next;
            }
        }

        private static void Drop(Entry en)
        {
            _map.Remove(en.key);
            if (en.lru != null) _lru.Remove(en.lru);
            Trim(en);
        }

        private static void Trim(Entry en)
        {
            if (en.mesh != null) Object.DestroyImmediate(en.mesh);
            if (en.chunks != null)
            {
                foreach (var c in en.chunks)
                {
                    if (c.mesh != null) Object.DestroyImmediate(c.mesh);
                }
            }
        }
    }

    /// <summary>分块几何（P3 数据结构）：一块 = 一段×一个材质槽的静置几何；份数由渲染器按 repeats 生成矩阵。</summary>
    public static class ChunkedStretch
    {
        public struct Chunk
        {
            public Mesh mesh;       // 共享缓存持有
            public int materialSlot; // 对应源/实例材质的 submesh 索引
            public int segment;      // 用于计算本块各份的位移
        }

        /// <summary>按段×submesh 切块：三角形按重心归段（与全网格同判据），块内顶点 (源索引) 去重共享。</summary>
        public static Chunk[] BuildChunks(Mesh src, StretchProfile profile)
        {
            if (!StretchMeshBuilder.CanProcess(src, out _)) return null;
            int segN = profile.segmentTypes.Count;
            int subN = Mathf.Max(1, src.subMeshCount);
            var verts = src.vertices;
            var uvs = src.uv;
            var nrm = src.normals;
            bool hasN = nrm != null && nrm.Length == verts.Length;
            int ax = (int)profile.axis;
            float bmin = StretchMeshBuilder.BoundsMin(src, profile.axis);
            float bmax = StretchMeshBuilder.BoundsMax(src, profile.axis);
            const float Eps = 1e-5f;

            var list = new List<Chunk>(segN * subN);
            for (int sm = 0; sm < subN; sm++)
            {
                var tris = src.GetTriangles(sm);
                // 每段独立顶点池（块内去重；跨块不共享——块本来就要分开提交）
                var poolV = new List<Vector3>[segN];
                var poolU = new List<Vector2>[segN];
                var poolN = new List<Vector3>[segN];
                var poolI = new List<int>[segN];
                var remap = new Dictionary<int, int>[segN];
                for (int s = 0; s < segN; s++)
                {
                    poolV[s] = new List<Vector3>(); poolU[s] = new List<Vector2>();
                    poolN[s] = new List<Vector3>(); poolI[s] = new List<int>();
                    remap[s] = new Dictionary<int, int>();
                }

                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    float cx = (verts[i0][ax] + verts[i1][ax] + verts[i2][ax]) / 3f;
                    int seg = profile.SegmentIndexAt(Mathf.Clamp(cx, bmin, bmax - Eps * 2));
                    if (seg < 0) seg = segN - 1;

                    // 注意：块按"整三角形不切割"聚合——切割仅在全网格构建时发生；
                    // 分块渲染的复制份是整段搬运，跨段三角形整体归重心段（0.1 语义，零畸变）。
                    for (int j = 0; j < 3; j++)
                    {
                        int si = j == 0 ? i0 : (j == 1 ? i1 : i2);
                        if (!remap[seg].TryGetValue(si, out int local))
                        {
                            local = poolV[seg].Count;
                            remap[seg][si] = local;
                            poolV[seg].Add(verts[si]);
                            poolU[seg].Add(uvs[si]);
                            poolN[seg].Add(hasN ? nrm[si] : Vector3.up);
                        }
                        poolI[seg].Add(local);
                    }
                }

                for (int s = 0; s < segN; s++)
                {
                    if (poolI[s].Count == 0) continue;
                    var m = new Mesh { name = $"chunk_s{s}_sub{sm}" };
                    m.indexFormat = poolV[s].Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                    m.SetVertices(poolV[s]);
                    m.SetUVs(0, poolU[s]);
                    m.SetNormals(poolN[s]);
                    m.SetTriangles(poolI[s].ToArray(), 0);
                    m.RecalculateBounds();
                    m.hideFlags = HideFlags.DontSave;
                    list.Add(new Chunk { mesh = m, materialSlot = sm, segment = s });
                }
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        /// <summary>某块第 k 份的局部位移向量（与全网格同语义：segOffset + k×段长）。</summary>
        public static Vector3 ChunkOffset(StretchProfile profile, IList<int> repeats, Chunk c, int k)
        {
            float off = profile.ExtensionBelow(profile.sliceBounds[c.segment], repeats) + k * profile.SegmentLength(c.segment);
            var v = Vector3.zero;
            v[(int)profile.axis] = off;
            return v;
        }

        /// <summary>某块的复制份数。</summary>
        public static int ChunkCopies(StretchProfile profile, IList<int> repeats, Chunk c)
        {
            return profile.TypeAt(c.segment) == SegmentType.Repeatable
                ? StretchProfile.RepeatAt(repeats, c.segment) : 1;
        }
    }
}
