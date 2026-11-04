using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 场景可视化（M4）：巡逻折线 + 视野<b>地面投影边界</b> + 扇形立面。
    /// <para>关键约定：编辑态 Gizmos 与 Play 态填充<b>共用 <see cref="GroundVisionProjection"/>
    /// 同一套几何</b>。若两处各算一次，会出现「Gizmos 画到那里、实机照不到」的矛盾，
    /// 与 AGENTS.md §0.1「所见即所得」直接冲突。</para>
    /// </summary>
    public static class StealthGizmos
    {
        static readonly Color RouteColor = new Color(0.3f, 0.8f, 1f, 0.9f);
        static readonly Color ConeColor = new Color(1f, 0.85f, 0.3f, 0.85f);
        static readonly Color RadialColor = new Color(1f, 0.85f, 0.3f, 0.22f);
        static readonly Color ProximityColor = new Color(1f, 0.45f, 0.2f, 0.8f);
        static readonly Color BlindColor = new Color(1f, 0.4f, 0.4f, 0.55f);

        // Gizmos 在主线程即时模式绘制，复用缓冲是安全的。
        static readonly List<Vector3> s_vertices = new List<Vector3>(96);
        static readonly List<int> s_indices = new List<int>(256);

        /// <summary>路线根：画路点折线（局部坐标经根换算到世界）。</summary>
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

        /// <summary>
        /// 视野体：地面投影边界（与运行期填充同源）+ 扇形立面 + 近身圈。
        /// </summary>
        public static void DrawVision(VisionVolume v)
        {
            if (v == null || !v.Enabled || v.Profile == null) return;

            var origin = v.OriginWorld;
            var fwd = v.ForwardWorld;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            var yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            var p = v.Params;

            DrawGroundOutline(v, origin, yaw, p);
            DrawFanElevation(origin, fwd, p);
        }

        /// <summary>
        /// 地面投影轮廓：外弧折线 + 脚底辐射线 + 内弧（盲区环口）。
        /// 顶点契约见 <see cref="GroundVisionProjection.Build"/>：前半为外弧、后半为内弧。
        /// </summary>
        static void DrawGroundOutline(VisionVolume v, Vector3 eye, float yaw, VisionParams p)
        {
            var groundY = eye.y - Mathf.Max(0.05f, v.EffectiveEyeHeight);
            var foot = v.transform.position;
            if (!GroundVisionProjection.Build(eye, yaw, v.Profile, v.DistanceMultiplier,
                                              foot, GroundVisionProjection.DefaultProbeHeight,
                                              24, groundY, v.OccluderMaskValue,
                                              s_vertices, s_indices))
            {
                return;
            }

            var count = s_vertices.Count;
            var outer = count / 2;                       // 外弧段长（含首尾重合点）

            Gizmos.color = ConeColor;
            for (var i = 0; i + 1 < outer; i++)
            {
                Gizmos.DrawLine(s_vertices[i], s_vertices[i + 1]);
            }

            // 内弧：有盲区环口时画出来，提醒「脚下这一圈反而看不见」
            if (outer < count && (s_vertices[outer] - s_vertices[0]).sqrMagnitude > 1e-4f)
            {
                Gizmos.color = BlindColor;
                for (var i = outer; i + 1 < count; i++)
                {
                    Gizmos.DrawLine(s_vertices[i], s_vertices[i + 1]);
                }
            }

            var body = foot + Vector3.up * 0.06f;
            Gizmos.color = RadialColor;
            for (var i = 0; i < outer; i += 3)
            {
                Gizmos.DrawLine(body, s_vertices[i]);
            }

            // 近身豁免圈：与判定的 proximityRadius 同源，贴脸必然察觉
            if (p.proximityRadius > 0.01f) DrawCircle(body, p.proximityRadius, 20);
        }

        static void DrawCircle(Vector3 center, float radius, int segments)
        {
            Gizmos.color = ProximityColor;
            var prev = center + Vector3.right * radius;
            for (var i = 1; i <= segments; i++)
            {
                var a = i / (float)segments * Mathf.PI * 2f;
                var cur = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
        }

        /// <summary>扇形立面：两条侧边 + 弧，用于判断朝向与视距。</summary>
        static void DrawFanElevation(Vector3 origin, Vector3 fwd, VisionParams p)
        {
            Gizmos.color = ConeColor;

            if (p.horizontalAngleDegrees >= 359.9f)
            {
                DrawRing(origin, fwd, p.maxDistance, 24, 0f, 360f);
                return;
            }

            var half = p.horizontalAngleDegrees * 0.5f;
            var left = Quaternion.Euler(0f, -half, 0f) * fwd;
            var right = Quaternion.Euler(0f, half, 0f) * fwd;
            Gizmos.DrawLine(origin, origin + left.normalized * p.maxDistance);
            Gizmos.DrawLine(origin, origin + right.normalized * p.maxDistance);
            DrawRing(origin, fwd, p.maxDistance, 12, -half, half);
        }

        static void DrawRing(Vector3 origin, Vector3 fwd, float radius, int steps,
                             float fromDeg, float toDeg)
        {
            if (radius <= 0f || steps < 2) return;
            var prev = default(Vector3);
            for (var i = 0; i <= steps; i++)
            {
                var deg = Mathf.Lerp(fromDeg, toDeg, i / (float)steps);
                var dir = Quaternion.Euler(0f, deg, 0f) * fwd;
                var p = origin + dir.normalized * radius;
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
