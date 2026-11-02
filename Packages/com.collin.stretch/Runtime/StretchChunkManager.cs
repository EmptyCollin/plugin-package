using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stretch
{
    /// <summary>
    /// 分块实例化提交器（DESIGN §8.2 P3）：把"复制份"从几何退化为矩阵——
    /// 块几何（段×材质槽）经缓存共享，每帧对每个 (块, 材质) 组用 Graphics.DrawMeshInstanced
    /// 一次提交全部份（本对象份数 × 绑定实例数），Draw 数与复制份/实例数解耦。
    /// 语义边界（如实）：不做阴影/接收阴影（DrawMeshInstanced 不进阴影 pass，文档标注）；
    /// 绑定同一提交器的实例须用相同材质数组（材质纪律，§8.2 P4）；份数变化下一帧自动生效。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class StretchChunkManager : MonoBehaviour
    {
        /// <summary>一个 (块, 材质槽) 的提交组。</summary>
        private class Group
        {
            public Mesh mesh;
            public Material material;
            public int segment;   // 该块所属段（份数与位移按 profile 算）
            public int materialSlot;
            public readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
        }

        private readonly List<StretchInstance> _bound = new List<StretchInstance>();
        private readonly List<Group> _groups = new List<Group>();
        private readonly Dictionary<int, Group> _groupMap = new Dictionary<int, Group>();
        private static readonly Matrix4x4[] _scratch = new Matrix4x4[1023];
        private static readonly List<StretchChunkManager> _all = new List<StretchChunkManager>();

        /// <summary>本帧 DrawMeshInstanced 提交组数与矩阵总数（统计/验收用）。</summary>
        public static int LastDrawGroups;
        public static int LastDrawMatrices;

        public StretchInstance attachedInstance => _bound.Count > 0 ? _bound[0] : null;
        public int BoundCount => _bound.Count;

        private void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
        }

        private void OnDisable()
        {
            _all.Remove(this);
        }

        public void Bind(StretchInstance inst)
        {
            if (inst == null) return;
            if (!_bound.Contains(inst)) _bound.Add(inst);
            // Chunked 由 DrawMeshInstanced 独立提交——关掉本物体 renderer 防源网格重影（材质仍从它读取）
            var r = inst.GetComponent<MeshRenderer>();
            if (r != null) r.enabled = false;
            RebuildGroups();
        }

        public void Unbind(StretchInstance inst = null)
        {
            if (inst != null)
            {
                _bound.Remove(inst);
                if (inst != null && inst.GetComponent<MeshRenderer>() is var rr && rr != null) rr.enabled = true;
            }
            else
            {
                foreach (var b in _bound)
                {
                    if (b != null && b.GetComponent<MeshRenderer>() is var rr2 && rr2 != null) rr2.enabled = true;
                }
                _bound.Clear();
            }
            ReleaseGroups();
        }

        private void ReleaseGroups()
        {
            // 块数组归属缓存，本提交器持有一份引用（RebuildGroups 时 Acquire）
            if (_chunkRef != null) { StretchMeshCache.ReleaseChunks(_chunkRef); _chunkRef = null; }
            _groups.Clear();
            _groupMap.Clear();
        }

        private ChunkedStretch.Chunk[] _chunkRef;
        private static readonly List<Material> _touchedMats = new List<Material>();

        /// <summary>退出 Play 时还原被本组件开启过的材质 instancing（编辑期共享材质不留痕）。</summary>
        public static void RestoreTouchedMaterials()
        {
            foreach (var m in _touchedMats)
            {
                if (m != null) m.enableInstancing = false;
            }
            _touchedMats.Clear();
        }

        private void Update()
        {
            if (_bound.Count == 0) return;
            // 份数/边界外部热改：下帧自动重算组（廉价：只在几何键变化时重建）
            if (_bound[0].EffectiveRepeats().Length == 0 || _groups.Count == 0) RebuildGroups();
            Submit();
        }

        /// <summary>块组重建：块=缓存分块（与份数无关）；材质=绑定实例的 sharedMaterials。</summary>
        private void RebuildGroups()
        {
            _groups.Clear();
            _groupMap.Clear();
            if (_bound.Count == 0) return;

            var lead = _bound[0];
            if (lead.profile == null || lead.OriginalMesh == null) return;
            var chunks = StretchMeshCache.AcquireChunks(lead.OriginalMesh, lead.profile);
            if (chunks == null) return;
            if (_chunkRef != chunks)
            {
                if (_chunkRef != null) StretchMeshCache.ReleaseChunks(_chunkRef);
                _chunkRef = chunks;
            }

            var renderer = lead.GetComponent<MeshRenderer>();
            var mats = renderer != null ? renderer.sharedMaterials : null;

            foreach (var c in chunks)
            {
                Material m = mats != null && c.materialSlot < mats.Length ? mats[c.materialSlot] : null;
                if (m == null) continue;
                if (!m.enableInstancing)
                {
                    // DrawMeshInstanced 要求材质开 GPU Instancing；直接开原材质（共享资产，
                    // 开了对普通渲染无副作用），而非实例化副本（材质纪律 §8.2 P4）
                    m.enableInstancing = true; // 序列化字段改变；编辑器由保存守卫兜底标脏
                }
                int key = (c.mesh.GetEntityId().GetHashCode() * 397) ^ m.GetEntityId().GetHashCode();
                if (!_groupMap.TryGetValue(key, out var g))
                {
                    g = new Group { mesh = c.mesh, material = m, segment = c.segment, materialSlot = c.materialSlot };
                    _groupMap[key] = g;
                    _groups.Add(g);
                }
                g.matrices.Add(Matrix4x4.identity); // 占位标记组活跃；真实矩阵在 Submit 组装
            }
        }

        /// <summary>每帧组装矩阵并提交：对每个绑定实例，按 profile.repeats 把块摆到各份位移。</summary>
        private void Submit()
        {
            LastDrawGroups = 0; LastDrawMatrices = 0;
            foreach (var g in _groups) g.matrices.Clear();
            if (_groups.Count == 0) return;

            foreach (var inst in _bound)
            {
                if (inst == null || !inst.isActiveAndEnabled || inst.profile == null) continue;
                var reps = inst.EffectiveRepeats();
                var renderer = inst.GetComponent<MeshRenderer>();
                var mats = renderer != null ? renderer.sharedMaterials : null;
                var root = inst.transform.localToWorldMatrix;

                foreach (var g in _groups)
                {
                    Material m = mats != null && g.materialSlot < mats.Length ? mats[g.materialSlot] : null;
                    if (m == null || m != g.material) continue; // 材质不一致（违反绑定纪律）→ 跳过该实例
                    var c = new ChunkedStretch.Chunk { mesh = g.mesh, segment = g.segment, materialSlot = g.materialSlot };
                    int copies = ChunkedStretch.ChunkCopies(inst.profile, reps, c);
                    for (int k = 0; k < copies; k++)
                    {
                        var off = ChunkedStretch.ChunkOffset(inst.profile, reps, c, k);
                        g.matrices.Add(root * Matrix4x4.TRS(off, Quaternion.identity, Vector3.one));
                    }
                }
            }

            foreach (var g in _groups)
            {
                // DrawMeshInstanced 要求材质启用 GPU Instancing（运行期临时开，OnDisable 还原）
                if (Application.isPlaying && !g.material.enableInstancing)
                {
                    g.material.enableInstancing = true;
                    if (!_touchedMats.Contains(g.material)) _touchedMats.Add(g.material);
                }
                var list = g.matrices;
                int i = 0;
                while (list.Count - i > 0)
                {
                    int n = Mathf.Min(1023, list.Count - i);
                    // DrawMeshInstanced 每批上限 1023
                    for (int j = 0; j < n; j++) _scratch[j] = list[i + j];
                    Graphics.DrawMeshInstanced(g.mesh, 0, g.material, _scratch, n,
                        null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                    LastDrawGroups++; LastDrawMatrices += n;
                    i += n;
                }
            }
        }
    }
}
