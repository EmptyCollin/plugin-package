using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 把 <see cref="GroundVisionProjection"/> 的多边形<b>真正画在地面上</b>（Play 态半透明填充）。
    /// <para>用户实机反馈：「加上 NPC 警戒范围在地面的投影，否则根本看不出边界」——
    /// 只有扇形线框时人眼判断不了地面覆盖到哪。</para>
    /// <para>实现取舍：用 <c>Graphics.DrawMesh</c> 而不是给每个哨兵挂 MeshRenderer 子物体，
    /// 因为预制件层级要保持干净（拖进场景不多出物体），且运行期<b>不得访问 <c>renderer.material</c></b>
    /// （编辑态访问会报 error 并泄漏材质，本包已实测吃过）。</para>
    /// <para>只在 Play 态画；编辑态的边界由 <see cref="StealthGizmos"/> 用同一投影函数画线，
    /// 两条路径共用一套几何，不会出现「 Gizmos 说能看见、实际看不见」的矛盾。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VisionVolume))]
    public sealed class StealthGroundProjection : MonoBehaviour
    {
        [Tooltip("扇形细分射线数（越大越圆滑，成本线性）")]
        [SerializeField, Range(4, 64)] int _rays = 20;

        [Tooltip("地面高度（世界 Y）。白盒示例的地面在 0")]
        [SerializeField] float _groundY;

        [Tooltip("判定点相对地面的高度（米）。默认与 DetectionState 身体判定点同口径，"
                 + "投影边界才会和实际察觉范围一致")]
        [SerializeField, Range(0f, 2f)] float _probeHeight = GroundVisionProjection.DefaultProbeHeight;

        [Tooltip("填充不透明度")]
        [SerializeField, Range(0f, 0.6f)] float _fillOpacity = 0.22f;

        VisionVolume _volume;
        PatrolAgent _agent;
        Mesh _mesh;
        Material _material;
        bool _materialFailed;

        readonly List<Vector3> _vertices = new List<Vector3>(64);
        readonly List<int> _indices = new List<int>(192);

        /// <summary>当前投影面积（平方米），供 HUD 与测试读取。</summary>
        public float CoverageArea { get; private set; }

        void Awake()
        {
            _volume = GetComponent<VisionVolume>();
            _agent = GetComponent<PatrolAgent>();
            _mesh = new Mesh { name = "GroundVisionProjection" };
            _mesh.MarkDynamic();
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            if (_volume == null) _volume = GetComponent<VisionVolume>();
            if (_volume == null || !_volume.Enabled || _volume.Profile == null) return;

            if (!GroundVisionProjection.Build(
                    _volume.OriginWorld,
                    Mathf.Atan2(_volume.ForwardWorld.x, _volume.ForwardWorld.z) * Mathf.Rad2Deg,
                    _volume.Profile,
                    _volume.DistanceMultiplier,
                    transform.position,
                    _probeHeight,
                    _rays,
                    _groundY,
                    _volume.OccluderMaskValue,
                    _vertices, _indices))
            {
                return;
            }

            CoverageArea = GroundVisionProjection.PolygonArea(_vertices, _indices);

            _mesh.Clear(false);
            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(_indices, 0);
            _mesh.RecalculateBounds();

            var mat = EnsureMaterial();
            if (mat != null)
            {
                var color = StealthStageVisual.ColorOf(StageNow());
                color.a = _fillOpacity;
                mat.color = color;
                Graphics.DrawMesh(_mesh, Matrix4x4.identity, mat, gameObject.layer,
                                  null, 0, null, false, false);
            }
        }

        StealthDetectionStage StageNow()
        {
            var brain = _agent != null ? _agent.GetComponent<GuardBrain>() : null;
            return brain != null ? brain.Stage : StealthDetectionStage.Patrol;
        }

        /// <summary>
        /// 运行期建一份半透明材质。着色器按 URP → Sprites → 内置 顺序找，
        /// 都找不到就<b>只报一次 error 并放弃绘制</b>，不反复刷屏。
        /// </summary>
        Material EnsureMaterial()
        {
            if (_material != null) return _material;
            if (_materialFailed) return null;

            Shader shader = null;
            string[] candidates =
            {
                "Universal Render Pipeline/Unlit",
                "Unlit/Color",
                "Sprites/Default",
                "Particles/Standard Unlit",
                "Standard"
            };
            for (var i = 0; i < candidates.Length && shader == null; i++)
            {
                shader = Shader.Find(candidates[i]);
                if (shader != null && !shader.isSupported) shader = null;
            }

            if (shader == null)
            {
                _materialFailed = true;
                Debug.LogWarning("[StealthGroundProjection] 找不到可用的无光着色器，地面投影不绘制"
                                 + "（编辑态 Gizmos 仍会画出边界线）。");
                return null;
            }

            _material = new Material(shader)
            {
                name = "StealthGroundProjectionRuntime",
                hideFlags = HideFlags.HideAndDontSave
            };
            return _material;
        }

        void OnValidate()
        {
            _rays = Mathf.Clamp(_rays, 4, 64);
            _fillOpacity = Mathf.Clamp01(_fillOpacity);
        }
    }
}
