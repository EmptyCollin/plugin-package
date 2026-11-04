using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 潜行关卡的<b>通关点</b>（撤离区）。
    /// <para>用户实机反馈：「始终无法结束游戏」——本包此前只有察觉与状态机，
    /// 没有任何胜负条件，所以玩法没有终点。本组件补上最小闭环：进入该区即通关。</para>
    /// <para>判定刻意用<b>纯几何</b>（水平包围 + 高度窗口）而不是 <c>OnTriggerEnter</c>：
    /// 预制件不需要配碰撞体层级、不需要给玩家加 Rigidbody，也不依赖物理事件时序
    /// （物理事件在域重载与编辑态都不可靠，本包已多次吃过这亏）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StealthExitZone : MonoBehaviour
    {
        [Tooltip("区域半径（米，水平圆判定）")]
        [SerializeField, Min(0.3f)] float _radius = 2.2f;

        [Tooltip("允许的脚底高度差（米）。过高说明玩家已跳离本层")]
        [SerializeField, Min(0.2f)] float _heightTolerance = 2.5f;

        [Tooltip("需要停留多久才通关（秒）。0 = 踏入即通关")]
        [SerializeField, Min(0f)] float _dwellSeconds = 0.6f;

        [Tooltip("区域标记材质（可选）。留空时用 Unity 内置标准着色器")]
        [SerializeField] Material _markerMaterial;

        float _dwellTimer;

        /// <summary>是否已被通关（避免重复触发）。</summary>
        public bool Reached { get; private set; }

        /// <summary>本区中心的水平位置。</summary>
        public Vector3 Center => transform.position;

        /// <summary>区域半径。</summary>
        public float Radius => _radius;

        /// <summary>当前驻留进度 [0,1]，供 HUD 显示。</summary>
        public float DwellProgress => _dwellSeconds <= 0f
            ? (Reached ? 1f : 0f)
            : Mathf.Clamp01(_dwellTimer / _dwellSeconds);

        /// <summary>玩家脚底是否落在本区内（纯几何，不依赖物理触发器）。</summary>
        public bool Contains(Vector3 footPosition)
        {
            var dx = footPosition.x - transform.position.x;
            var dz = footPosition.z - transform.position.z;
            if (dx * dx + dz * dz > _radius * _radius) return false;
            return Mathf.Abs(footPosition.y - transform.position.y) <= _heightTolerance;
        }

        /// <summary>推进驻留计时。<c>dt &lt;= 0</c>（编辑态预览）时不改状态。</summary>
        public bool Tick(bool inside, float dt)
        {
            if (Reached) return true;
            if (dt <= 0f) return false;

            if (!inside)
            {
                _dwellTimer = 0f;
                return false;
            }
            if (_dwellSeconds <= 0f)
            {
                Reached = true;
                return true;
            }

            _dwellTimer += dt;
            if (_dwellTimer >= _dwellSeconds) Reached = true;
            return Reached;
        }

        /// <summary>复原（辅助台/重开一局用）。</summary>
        public void ResetZone()
        {
            Reached = false;
            _dwellTimer = 0f;
        }

        void OnValidate()
        {
            _radius = Mathf.Max(0.3f, _radius);
            _heightTolerance = Mathf.Max(0.2f, _heightTolerance);
            _dwellSeconds = Mathf.Max(0f, _dwellSeconds);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Reached ? new Color(0.3f, 0.95f, 0.4f, 0.9f)
                                  : new Color(0.3f, 0.7f, 1f, 0.6f);
            var p = transform.position;
            // 地面圆环 + 立柱，一眼看出撤离在哪
            var prev = p + Vector3.right * _radius;
            for (var i = 1; i <= 24; i++)
            {
                var a = i / 24f * Mathf.PI * 2f;
                var cur = p + new Vector3(Mathf.Cos(a) * _radius, 0f, Mathf.Sin(a) * _radius);
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
            Gizmos.DrawLine(p, p + Vector3.up * 2.2f);
        }
    }
}
