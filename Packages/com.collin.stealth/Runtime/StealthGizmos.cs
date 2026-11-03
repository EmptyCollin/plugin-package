using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 场景可视化（M4）：巡逻折线 + 视野扇形 Gizmos。编辑态与 Play 态用同一数据源
    /// （路线资产与 VisionProfile），不存在第二套绘制口径。
    /// </summary>
    public static class StealthGizmos
    {
        static readonly Color RouteColor = new Color(0.3f, 0.8f, 1f, 0.9f);
        static readonly Color ConeColor = new Color(1f, 0.85f, 0.3f, 0.85f);
        static readonly Color ConeFill = new Color(1f, 0.85f, 0.3f, 0.10f);

        /// <summary>路线根：画路点折线（局部坐标经根换算到世界）。</summary>
        [UnityEngine.ContextMenu("OnDrawGizmos")]
        public static void DrawRoute(PatrolRouteRoot root)
        {
            var route = root != null ? root.Route : null;
            if (route == null || route.WaypointCount < 2) return;

            Gizmos.color = RouteColor;
            var first = root.LocalToWorld(route.GetWaypointPosition(0));
            var prev = first;
            for (var i = 1; i < route.WaypointCount; i++)
            {
                var p = root.LocalToWorld(route.GetWaypointPosition(i));
                Gizmos.DrawLine(prev, p);
                Gizmos.DrawWireSphere(p, 0.15f);
                prev = p;
            }
            if (route.LoopMode == PatrolLoopMode.Loop && route.WaypointCount > 2)
            {
                Gizmos.DrawLine(prev, first);
            }
            Gizmos.DrawWireSphere(first, 0.3f);   // 起点标记
        }

        /// <summary>视野体：水平扇形线框 + 半透明填充（按当前位姿）。</summary>
        public static void DrawVision(VisionVolume v)
        {
            if (v == null || !v.Enabled || v.Profile == null) return;

            var origin = v.OriginWorld;
            var fwd = v.ForwardWorld;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            var p = v.Params;
            var half = p.horizontalAngleDegrees * 0.5f;

            if (p.horizontalAngleDegrees >= 359.9f)
            {
                // 全向：画圆。
                DrawFan(origin, fwd, p.maxDistance, 0f, 360f, 24);
            }
            else
            {
                var left = Quaternion.Euler(0f, -half, 0f) * fwd;
                var right = Quaternion.Euler(0f, half, 0f) * fwd;
                Gizmos.color = ConeColor;
                Gizmos.DrawLine(origin, origin + left.normalized * p.maxDistance);
                Gizmos.DrawLine(origin, origin + right.normalized * p.maxDistance);
                DrawFan(origin, fwd, p.maxDistance, -half, half, 12);
            }
        }

        static void DrawFan(Vector3 origin, Vector3 forward, float radius, float fromDeg, float toDeg, int steps)
        {
            if (radius <= 0f || steps < 2) return;
            var prev = default(Vector3);
            Gizmos.color = ConeColor;
            for (var i = 0; i <= steps; i++)
            {
                var deg = Mathf.Lerp(fromDeg, toDeg, i / (float)steps);
                var dir = Quaternion.Euler(0f, deg, 0f) * forward;
                var p = origin + dir * radius;
                if (i > 0)
                {
                    Gizmos.DrawLine(prev, p);
                    // 半透明三角填充：视觉上标出可达区域，不影响任何判定。
                    Gizmos.color = ConeFill;
                    Gizmos.DrawLine(origin, p);
                    Gizmos.color = ConeColor;
                }
                prev = p;
            }
        }
    }
}
