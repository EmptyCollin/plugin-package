using UnityEngine;

namespace Collin.Stretch
{
    /// <summary>
    /// 拉伸实例组件（AGENTS.md §0.1 原则1/2）：
    /// Model 的实例半边——引用共享 <see cref="StretchProfile"/>（轴/边界/段类型），
    /// 并**序列化每段倍数**（实例状态：同一 Profile 的不同楼可以各自不同倍数）。
    /// [ExecuteAlways]：编辑期参数改动即时重建预览；Play 态同一构建路径（原则3）。
    /// 预览网格为 DontSave 内存态，落盘由 Editor 侧 StretchSaveHook 守卫——场景只存源网格+Profile+倍数。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class StretchInstance : MonoBehaviour
    {
        [Tooltip("共享结构资产：轴、slice 边界、每段类型。")]
        public StretchProfile profile;

        [Tooltip("每段复制倍数（索引对齐 profile.segmentTypes）。Fixed 段忽略此值。")]
        public int[] repeats = new int[0];

        [SerializeField, HideInInspector] private Mesh _originalMesh;
        private Mesh _previewMesh;

        public MeshFilter Filter => GetComponent<MeshFilter>();
        public Mesh OriginalMesh => _originalMesh;
        public Mesh PreviewMesh => _previewMesh;

        /// <summary>取该实例的有效倍数数组；长度不符时按 Profile 重建（Fixed 段=1）。</summary>
        public int[] EffectiveRepeats()
        {
            if (profile == null) return repeats ?? new int[0];
            if (repeats == null || repeats.Length != profile.segmentTypes.Count)
            {
                SyncRepeats();
            }
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

        /// <summary>装配入口：赋 profile 并同步倍数后重建（规避 OnEnable 早于外部赋值的时序陷阱）。</summary>
        public void SetProfile(StretchProfile value)
        {
            profile = value;
            SyncRepeats();
            if (isActiveAndEnabled) Rebuild();
        }

        private void OnEnable() => Rebuild();

        /// <summary>依据 Profile + 实例倍数重建预览网格。失败静默返回 null，日志归调用方。</summary>
        public bool Rebuild()
        {
            var filter = Filter;
            if (filter == null || profile == null) return false;

            var src = _originalMesh != null ? _originalMesh : filter.sharedMesh;
            if (src == null) return false;
            if (_originalMesh == null) _originalMesh = src;

            var built = StretchMeshBuilder.Build(src, profile, EffectiveRepeats());
            if (built == null) return false;

            built.hideFlags = HideFlags.DontSave;
            if (_previewMesh != null && _previewMesh != built) DestroyImmediate(_previewMesh);
            _previewMesh = built;
            filter.sharedMesh = _previewMesh;
            return true;
        }

        /// <summary>还原原始网格并释放预览内存。</summary>
        public void Restore()
        {
            var filter = Filter;
            if (filter != null && _originalMesh != null)
            {
                filter.sharedMesh = _originalMesh;
            }
            if (_previewMesh != null)
            {
                DestroyImmediate(_previewMesh);
                _previewMesh = null;
            }
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
