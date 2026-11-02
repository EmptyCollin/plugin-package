using UnityEngine;

namespace Collin.Stretch
{
    /// <summary>
    /// 编辑期预览开关（DESIGN D-2）：把 Profile 的结果应用到自身 MeshFilter，
    /// 但**只写内存网格**（HideFlags.DontSave），不生成磁盘资产；关闭时还原原始网格。
    /// 由 Editor 面板开关与刷新，也可被其他工具调用。域重载后 OnEnable 自动重建，幂等。
    /// </summary>
    [DisallowMultipleComponent]
    public class StretchPreview : MonoBehaviour
    {
        public StretchProfile profile;

        [SerializeField, HideInInspector] private Mesh _originalMesh;
        private Mesh _previewMesh;

        public bool Active => isActiveAndEnabled;
        public Mesh OriginalMesh => _originalMesh;
        public MeshFilter Filter => GetComponent<MeshFilter>();

        /// <summary>装配入口：赋 profile 并立即重建预览。
        /// 注意 OnEnable 早于外部赋值的时序陷阱——外部设置 profile 必须走本方法（或直接调 Rebuild）。</summary>
        public void SetProfile(StretchProfile value)
        {
            profile = value;
            if (isActiveAndEnabled) Rebuild();
        }

        private void OnEnable() => Rebuild();

        private void OnDisable() => Restore();

        /// <summary>依据当前 Profile 重建预览网格。非法参数保持原样并告警。</summary>
        public void Rebuild()
        {
            var filter = Filter;
            if (filter == null || profile == null) return;

            var src = _originalMesh != null ? _originalMesh : filter.sharedMesh;
            if (src == null) return;
            if (_originalMesh == null) _originalMesh = src;

            if (!StretchMeshBuilder.CanProcess(src, out var err))
            {
                Debug.LogWarning($"[StretchPreview:{name}] {err}", this);
                return;
            }
            var built = StretchMeshBuilder.Build(src, profile);
            if (built == null) return;

            built.hideFlags = HideFlags.DontSave;
            if (_previewMesh != null && _previewMesh != built) DestroyImmediate(_previewMesh);
            _previewMesh = built;
            filter.sharedMesh = _previewMesh; // 编辑期禁读/写 mesh 属性（会实例化泄漏）
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
    }
}
