using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stretch
{
    /// <summary>
    /// 拉伸实例组件（AGENTS.md §0.1 原则1/2/3）：
    /// Model 的实例半边——引用共享 <see cref="StretchProfile"/>（轴/边界/段类型），
    /// 并**序列化每段倍数**（实例状态：同一 Profile 的不同楼可以各自不同倍数）。
    /// [ExecuteAlways]：编辑期参数改动即时重建预览并即时同步碰撞体；Play 态同一条构建路径（原则3）。
    /// 结果网格经 <see cref="StretchMeshCache"/> 按 (源,profile,repeats) 共享（DESIGN §8.2 P1）；
    /// 启用 syncMeshCollider 或 <see cref="uniqueMesh"/> 时退回独占网格（MeshCollider 不宜与渲染共享/
    /// 分块渲染的实例不应持有全网格）。
    /// 预览网格 DontSave，落盘由 Editor 侧 StretchSaveHook 守卫——场景只存源网格+Profile+倍数。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    public class StretchInstance : MonoBehaviour
    {
        [Tooltip("共享结构资产：轴、slice 边界、每段类型。")]
        public StretchProfile profile;

        [Tooltip("每段复制倍数（索引对齐 profile.segmentTypes）。Fixed 段忽略此值。")]
        public int[] repeats = new int[0];

        [Tooltip("拉伸后是否同步 BoxCollider 尺寸（与视觉同等大小）。")]
        public bool syncBoxCollider = true;

        [Tooltip("拉伸后是否同步 MeshCollider 为结果网格。大规模实例请关闭（用 Box/程序化碰撞，DESIGN §8.2 P4）。")]
        public bool syncMeshCollider = false;

        [Tooltip("强制独占网格（禁用缓存共享）。分块渲染组件接管显示时勾选，避免本实例持有整份大网格。")]
        public bool uniqueMesh = false;

        /// <summary>渲染路径（DESIGN §8.2）：MeshFilter=单网格兼容；Chunked=分块实例化（万级规模）。</summary>
        public enum RenderModeKind { MeshFilter = 0, Chunked = 1 }

        [Tooltip("渲染路径：MeshFilter=单网格（兼容/小规模）；Chunked=分块实例化（Draw 数与实例数解耦）。")]
        public RenderModeKind renderMode = RenderModeKind.MeshFilter;

        [Tooltip("Chunked 模式的提交器；null 时自动挂到自身。")]
        public StretchChunkManager chunkHost;

        [SerializeField, HideInInspector] private Mesh _originalMesh;
        [SerializeField, HideInInspector] private List<MeshColliderBackup> _meshColliders = new List<MeshColliderBackup>();
        private Mesh _previewMesh;
        private bool _previewShared; // 当前预览是否来自缓存（Release 用）

        /// <summary>MeshCollider 的源网格备份（Restore 时还原；序列化以便跨保存/重开保持）。</summary>
        [System.Serializable]
        public struct MeshColliderBackup
        {
            public MeshCollider collider;
            public Mesh originalMesh;
        }

        public MeshFilter Filter => GetComponent<MeshFilter>();
        public Mesh OriginalMesh => _originalMesh;
        public Mesh PreviewMesh => _previewMesh;

        /// <summary>取该实例的有效倍数数组；长度不符时按 Profile 重建。</summary>
        public int[] EffectiveRepeats()
        {
            if (profile == null) return repeats ?? new int[0];
            if (repeats == null || repeats.Length != profile.segmentTypes.Count) SyncRepeats();
            return repeats;
        }

        /// <summary>按当前段类型补齐/裁剪倍数数组（保留既有值）。</summary>
        public void SyncRepeats()
        {
            if (profile == null) return;
            var next = new int[profile.segmentTypes.Count];
            for (int i = 0; i < next.Length; i++)
            {
                int old = repeats != null && i < repeats.Length ? repeats[i] : 1;
                next[i] = profile.TypeAt(i) == SegmentType.Repeatable ? Mathf.Max(1, old) : 1;
            }
            repeats = next;
        }

        /// <summary>边界越界/乱序时归一（Profile 重排 + repeats 同步映射）。返回是否发生改动。</summary>
        public bool NormalizeBounds()
        {
            if (profile == null) return false;
            var map = profile.NormalizeOrder();
            if (map == null) return false;
            repeats = StretchProfile.RemapByOrder(EffectiveRepeats(), map);
            return true;
        }

        /// <summary>装配入口：赋 profile 并同步倍数后重建（规避 OnEnable 早于外部赋值的时序陷阱）。</summary>
        public void SetProfile(StretchProfile value)
        {
            profile = value;
            SyncRepeats();
            if (isActiveAndEnabled) Rebuild();
        }

        private void OnEnable()
        {
            if (_originalMesh == null && Filter != null && Filter.sharedMesh != null)
            {
                _originalMesh = Filter.sharedMesh;
            }
            // 首帧走队列错峰；面板交互用 RebuildNow 保即时（§8.2 P4）
            StretchScheduler.Enqueue(this);
        }

        private void OnDisable() => StretchScheduler.Dequeue(this);

        /// <summary>重建预览（内部决策共享/独占）。保留原签名兼容面板与既有调用。</summary>
        public bool Rebuild()
        {
            return RebuildNow();
        }

        /// <summary>同步重建（编辑态面板即时预览用；调度泵亦调用）。失败静默返回 false。</summary>
        public bool RebuildNow()
        {
            var filter = Filter;
            if (filter == null || profile == null) return false;

            var src = _originalMesh != null ? _originalMesh : filter.sharedMesh;
            if (src == null) return false;
            if (_originalMesh == null) _originalMesh = src;

            if (renderMode == RenderModeKind.Chunked)
            {
                ReleasePreview(); // 交还全网格引用，MeshFilter 归位源网格
                if (chunkHost == null)
                {
                    chunkHost = GetComponent<StretchChunkManager>();
                    if (chunkHost == null) chunkHost = gameObject.AddComponent<StretchChunkManager>();
                }
                chunkHost.Bind(this);
                return true;
            }
            if (chunkHost != null) chunkHost.Unbind();

            var reps = EffectiveRepeats();
            bool wantShared = !uniqueMesh && !syncMeshCollider;

            Mesh built;
            bool shared;
            if (wantShared)
            {
                built = StretchMeshCache.AcquireFull(src, profile, reps);
                shared = built != null;
            }
            else
            {
                built = StretchMeshBuilder.Build(src, profile, reps);
                shared = false;
            }
            if (built == null) return false;

            ReleasePreview(); // 旧预览：共享则 Release，独占则销毁

            if (!shared) built.hideFlags = HideFlags.DontSave;
            _previewMesh = built;
            _previewShared = shared;
            filter.sharedMesh = _previewMesh;

            CaptureMeshColliders(src);
            SyncColliders(built.bounds);
            SyncMeshColliderAfterRebuild();
            return true;
        }

        private void ReleasePreview()
        {
            if (_previewMesh == null) return;
            if (_previewShared) StretchMeshCache.ReleaseFull(_previewMesh);
            else if (_previewMesh != null) DestroyImmediate(_previewMesh);
            _previewMesh = null;
            _previewShared = false;
            var filter = Filter;
            if (filter != null && _originalMesh != null) filter.sharedMesh = _originalMesh;
        }

        // ── 碰撞体同步（视觉与碰撞同尺寸） ──────────────────

        private void CaptureMeshColliders(Mesh src)
        {
            var buf = new List<MeshCollider>();
            GetComponents<MeshCollider>(buf);
            foreach (var mc in buf)
            {
                if (mc == null) continue;
                bool tracked = false;
                for (int i = 0; i < _meshColliders.Count; i++)
                {
                    if (_meshColliders[i].collider == mc)
                    {
                        if (_meshColliders[i].originalMesh != null
                            && mc.sharedMesh != _meshColliders[i].originalMesh
                            && mc.sharedMesh != _previewMesh)
                        {
                            mc.sharedMesh = _meshColliders[i].originalMesh;
                        }
                        tracked = true;
                        break;
                    }
                }
                if (!tracked && mc.sharedMesh == src)
                {
                    _meshColliders.Add(new MeshColliderBackup
                    {
                        collider = mc,
                        originalMesh = src
                    });
                }
            }
        }

        /// <summary>把碰撞体几何同步到结果网格的局部包围盒（collider 与 mesh bounds 同为局部空间）。</summary>
        public void SyncColliders(Bounds resultBounds)
        {
            var size = resultBounds.size;
            var center = resultBounds.center;

            if (syncBoxCollider)
            {
                var buf = new List<BoxCollider>();
                GetComponents<BoxCollider>(buf);
                foreach (var b in buf)
                {
                    b.size = new Vector3(
                        Mathf.Max(1e-4f, size.x),
                        Mathf.Max(1e-4f, size.y),
                        Mathf.Max(1e-4f, size.z));
                    b.center = center;
                }
            }

            var rb = GetComponent<Rigidbody>();
            if (rb != null && Application.isPlaying)
            {
                rb.ResetCenterOfMass();
                rb.ResetInertiaTensor();
                // 编辑期刚体物理量无意义（无模拟），不做处理（原则3：Runtime 不依赖 UnityEditor）
            }
        }

        private static readonly List<MeshCollider> _mcBuf = new List<MeshCollider>();

        private void SyncMeshColliderAfterRebuild()
        {
            if (!syncMeshCollider) return;
            GetComponents<MeshCollider>(_mcBuf);
            foreach (var mc in _mcBuf)
            {
                if (mc == null) continue;
                mc.sharedMesh = _previewMesh;
            }
        }

        /// <summary>还原源网格与碰撞体引用，释放预览。</summary>
        public void Restore()
        {
            StretchScheduler.Dequeue(this);
            var filter = Filter;
            if (filter != null && _originalMesh != null)
            {
                filter.sharedMesh = _originalMesh;
                foreach (var e in _meshColliders)
                {
                    if (e.collider != null && e.originalMesh != null)
                    {
                        e.collider.sharedMesh = e.originalMesh;
                    }
                }

                if (syncBoxCollider)
                {
                    var buf = new List<BoxCollider>();
                    GetComponents<BoxCollider>(buf);
                    foreach (var b in buf)
                    {
                        b.size = _originalMesh.bounds.size;
                        b.center = _originalMesh.bounds.center;
                    }
                }
            }
            ReleasePreview();
            if (chunkHost != null && chunkHost.attachedInstance == this) chunkHost.Unbind();
        }

        private void OnDestroy() => Restore();

        /// <summary>构建失败原因（供面板提示）。</summary>
        public string FailReason()
        {
            var filter = Filter;
            if (filter == null) return "物体没有 MeshFilter";
            if (profile == null) return "未指定 Profile";
            var src = _originalMesh != null ? _originalMesh : filter.sharedMesh;
            return StretchMeshBuilder.FailReason(src, profile, EffectiveRepeats());
        }
    }
}
