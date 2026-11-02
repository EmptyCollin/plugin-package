using UnityEngine;

namespace Collin.Stretch
{
    /// <summary>
    /// 场景执行组件（DESIGN §4）：把"源网格 + Profile"变成实例化的拉伸结果。
    /// 编辑期通过 StretchPreview 看效果；本组件负责运行时（Play/构建）自动替换网格。
    /// 约定：挂在含 MeshFilter 的对象上；不修改磁盘资产，运行时换用内存网格。
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [AddComponentMenu("Collin/Stretch/Repeater")]
    public class StretchRepeater : MonoBehaviour
    {
        [Tooltip("拉伸参数资产。null 或非法时保持原网格并告警一次。")]
        public StretchProfile profile;

        private Mesh _generated;
        private Mesh _original;
        private bool _warned;

        private void Awake()
        {
            Apply();
        }

        private void Apply()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter == null || profile == null) return;
            var src = filter.sharedMesh;
            if (src == null) return;

            if (!StretchMeshBuilder.CanProcess(src, out var err))
            {
                WarnOnce(err);
                return;
            }
            var built = StretchMeshBuilder.Build(src, profile);
            if (built == null)
            {
                WarnOnce("参数校验未通过，保持原网格");
                return;
            }

            if (_original == null) _original = filter.sharedMesh;
            if (_generated != null) Destroy(_generated);
            _generated = built;
            filter.sharedMesh = _generated; // 统一走 sharedMesh，避免 mesh 属性泄漏语义歧义

            // 同步盒子碰撞体尺寸（若有）
            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                var nb = _generated.bounds;
                box.size = new Vector3(
                    Mathf.Max(0.0001f, nb.size.x),
                    Mathf.Max(0.0001f, nb.size.y),
                    Mathf.Max(0.0001f, nb.size.z));
                box.center = nb.center;
            }
        }

        private void WarnOnce(string msg)
        {
            if (_warned) return;
            _warned = true;
            Debug.LogWarning($"[StretchRepeater:{name}] {msg}", this);
        }

        private void OnValidate()
        {
            // Inspector 改 profile 后编辑期不自动重建（避免播放中卡顿），下次 Awake/手动预览生效
            if (isActiveAndEnabled && Application.isPlaying) Apply();
        }

        private void OnDestroy()
        {
            if (_generated != null) Destroy(_generated);
            var filter = GetComponent<MeshFilter>();
            if (filter != null && _original != null) filter.sharedMesh = _original;
        }
    }
}
