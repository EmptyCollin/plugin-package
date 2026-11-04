using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 视野的<b>地面投影</b>几何（纯函数，无状态）。
    /// <para>存在理由：只画线框/扇形立面时，玩家<b>看不出警戒范围在地面上到底到哪里</b>
    /// （实机反馈）。潜行关卡的设计与游玩都靠「地面上那块区域」判断。</para>
    /// <para><b>与判定同源的环形扇区</b>（这是本函数存在的意义，任何一点不一致都会变成
    /// 「画的边界 ≠ 实际边界」）：把判定点取在玩家身体中部（<see cref="DefaultProbeHeight"/>），
    /// 与 <see cref="VisionGeometry"/> 的三层约束逐条对应——</para>
    /// <list type="bullet">
    /// <item><b>外缘 rFar</b>：L1 是斜距球检 ⇒ 地面水平半径上限 sqrt(maxDist² − Δh²)。</item>
    /// <item><b>内缘 rNear</b>：L2 俯仰闸门 ⇒ 视线锥够不到脚下近处，d ≥ Δh/tan(半垂直角)。
    /// 注意这是<b>最近</b>可见距离，不是射程上限（曾把它算成上限，投影只剩 3 米，与 14 米的
    /// 实际察觉范围严重不符）。若 L1 的近身豁免（proximity，<b>全向</b>）能盖住这圈盲区，
    /// 内缘就取 0（连续扇形），否则保留环口。</item>
    /// <item><b>遮挡裁边</b>：每根外缘射线命中遮挡物即收在命中处，照出掩体背后的阴影缺口。</item>
    /// </list>
    /// </summary>
    public static class GroundVisionProjection
    {
        /// <summary>投影默认判定点高度（米）：与 DetectionState 的身体判定点口径一致。</summary>
        public const float DefaultProbeHeight = 0.95f;

        const float Eps = 1e-4f;

        /// <summary>
        /// 生成地面投影（外弧 + 内弧的三角带；内弧退化为中心点时即为连续扇形）。
        /// <para>顶点契约：<c>0..rays</c> 为外弧（含首尾重合点），
        /// <c>rays+1..2*rays+1</c> 为对应内弧。索引是逐格四边形拆的三角带。
        /// 调用方画轮廓只需遍历外弧段。</para>
        /// </summary>
        /// <param name="eyePos">观察原点（世界，含眼高）。</param>
        /// <param name="yawDegrees">观察水平朝向（度）。</param>
        /// <param name="profile">视野参数（视距/水平角/垂直角/近身半径）。</param>
        /// <param name="distanceScale">实例视距倍率。</param>
        /// <param name="footPos">哨兵脚底位置（内缘与近身圆的水平圆心）。</param>
        /// <param name="probeHeight">判定点相对地面的高度（米），见 <see cref="DefaultProbeHeight"/>。</param>
        /// <param name="rays">外弧细分段数（最少 2）。</param>
        /// <param name="groundY">地面高度（世界 Y）。</param>
        /// <param name="occluderMask">遮挡层掩码（0 = 无遮挡）。</param>
        /// <param name="outVertices">输出顶点。</param>
        /// <param name="outIndices">输出三角形索引。</param>
        /// <returns>是否生成了有效多边形。</returns>
        public static bool Build(Vector3 eyePos, float yawDegrees, VisionProfile profile,
                                 float distanceScale, Vector3 footPos, float probeHeight,
                                 int rays, float groundY, int occluderMask,
                                 List<Vector3> outVertices, List<int> outIndices)
        {
            if (profile == null || outVertices == null || outIndices == null) return false;
            outVertices.Clear();
            outIndices.Clear();

            var maxDist = profile.MaxDistance * Mathf.Max(0.01f, distanceScale);
            if (maxDist <= Eps) return false;

            var deltaH = eyePos.y - (groundY + Mathf.Max(0f, probeHeight));   // 眼与判定点的高差
            var halfV = profile.VerticalAngle * 0.5f;
            var halfH = profile.HorizontalAngle * 0.5f;
            var omnidirectional = halfH >= 179.9f;
            if (!omnidirectional && halfH <= Eps) return false;

            // ── 外缘：L1 斜距球检换算到水平半径 ────────────────────
            var rFar = Mathf.Sqrt(Mathf.Max(0f, maxDist * maxDist - deltaH * deltaH));
            if (rFar <= Eps) return false;

            // ── 内缘：L2 俯仰闸门（最近的够得到的地面距离）─────────
            var rNearCone = 0f;
            if (!omnidirectional && deltaH > Eps && halfV < 89.9f)
            {
                var t = Mathf.Tan(halfV * Mathf.Deg2Rad);
                if (t > 1e-3f) rNearCone = deltaH / t;
            }
            else if (omnidirectional)
            {
                rNearCone = 0f;
            }

            // ── 近身豁免（全向、跳锥，斜距 ≤ proximity）────────────
            var rProx = 0f;
            if (profile.ProximityRadius > deltaH + Eps)
            {
                rProx = Mathf.Sqrt(profile.ProximityRadius * profile.ProximityRadius - deltaH * deltaH);
            }

            // 近身圈足以盖住锥盲区 ⇒ 连续扇形（内缘 0）；否则保留环口。
            var rNear = (rProx + Eps >= rNearCone) ? 0f : rNearCone;
            if (rNear >= rFar - Eps) rNear = 0f;    // 病态参数兜底：宁可画成扇形也不画空

            var stepCount = Mathf.Max(2, rays);
            var from = omnidirectional ? 0f : -halfH;
            var to = omnidirectional ? 360f : halfH;
            var foot = new Vector3(footPos.x, groundY, footPos.z);

            var outerCount = stepCount + 1;
            for (var i = 0; i < outerCount; i++)
            {
                var deg = Mathf.Lerp(from, to, i / (float)stepCount);
                var dir = DirFromYaw(yawDegrees + deg);
                outVertices.Add(CastToOuter(eyePos, dir, rFar, foot, groundY, occluderMask));
            }
            for (var i = 0; i < outerCount; i++)
            {
                var deg = Mathf.Lerp(from, to, i / (float)stepCount);
                var dir = DirFromYaw(yawDegrees + deg);
                // 内缘不做遮挡裁切（遮挡物贴不住脚下这个环），保持解析位置。
                outVertices.Add(rNear <= Eps ? foot : foot + dir * rNear);
            }

            var innerBase = outerCount;
            for (var i = 0; i < stepCount; i++)
            {
                var o0 = i;
                var o1 = i + 1;
                var i0 = innerBase + i;
                var i1 = innerBase + i + 1;
                // 四边形外0→外1→内1 / 外0→内1→内0（绕序：+Y 法向）
                outIndices.Add(o0); outIndices.Add(o1); outIndices.Add(i1);
                outIndices.Add(o0); outIndices.Add(i1); outIndices.Add(i0);
            }

            return outIndices.Count > 0;
        }

        /// <summary>
        /// 外缘落点：从眼向地面外缘点发射线，命中遮挡物则收在命中处的水平距离上。
        /// 落在内缘以内时保留（会被三角带自然压扁）。
        /// </summary>
        static Vector3 CastToOuter(Vector3 eyePos, Vector3 flatDir, float rFar,
                                   Vector3 foot, float groundY, int occluderMask)
        {
            var target = foot + flatDir * rFar;
            if (occluderMask == 0) return target;

            var toTarget = target - eyePos;
            var len = toTarget.magnitude;
            if (len <= Eps) return target;

            if (Physics.Raycast(eyePos, toTarget / len, out var hit, len - 0.02f, occluderMask,
                                QueryTriggerInteraction.Ignore))
            {
                var flat = new Vector3(hit.point.x - foot.x, 0f, hit.point.z - foot.z);
                var d = flat.magnitude;
                if (d < Eps) return foot;
                return foot + flat / d * Mathf.Max(0f, d - 0.05f);   // 略收，避免共面闪烁
            }
            return target;
        }

        /// <summary>由水平角求单位方向（Unity 左手系：0°=+Z，90°=+X）。</summary>
        public static Vector3 DirFromYaw(float yawDegrees)
        {
            var r = yawDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        /// <summary>投影面积（平方米）。用于「这块区域多大」的诊断与测试断言。</summary>
        public static float PolygonArea(List<Vector3> vertices, List<int> indices)
        {
            if (vertices == null || indices == null || indices.Count < 3) return 0f;

            var area = 0f;
            for (var i = 0; i + 2 < indices.Count; i += 3)
            {
                var a = vertices[indices[i]];
                var b = vertices[indices[i + 1]];
                var c = vertices[indices[i + 2]];
                area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
            return area;
        }
    }
}
