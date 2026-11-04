using System;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>视野几何参数（值类型，供 <see cref="VisionGeometry"/> 纯函数使用）。</summary>
    public struct VisionParams
    {
        public float maxDistance;
        public float horizontalAngleDegrees;
        public float verticalAngleDegrees;

        /// <summary>
        /// 近身半径（米）：目标进入此距离即视为贴脸察觉，<b>跳过 L2 视角锥</b>。
        /// <para>0 = 关闭。缺这一档会出现「NPC 与玩家重叠反而看不见」：目标几乎在视线正下方，
        /// 俯仰差 &gt; 半垂直角就被 L2 判瞎（实机反馈到的 bug 根因之一）。</para>
        /// </summary>
        public float proximityRadius;
    }

    /// <summary>
    /// 四层察觉管线的几何部分（L1 距离 → L2 视角锥 → L3 视线），<b>纯函数</b>。
    /// <para>为什么单独拆成静态函数：B 包要在<b>编辑态</b>按任意时刻 t 的 NPC 位姿问
    /// 「这一点看不看得到」（分层查看器 + 回溯预览）。若判定逻辑绑在 MonoBehaviour 的
    /// 当前 transform 上，编辑态就只剩「当前位置」一种问法，t 时刻的可见性问不出来。</para>
    /// <para>L3 以委托注入，使 L1/L2 的断言完全不依赖物理场景；
    /// 遮挡层过滤与射线预算留在 <see cref="VisionVolume"/> 一侧。</para>
    /// </summary>
    public static class VisionGeometry
    {
        const float Eps = 1e-6f;

        /// <summary>
        /// 判 target 相对 origin+forward 是否可见。
        /// </summary>
        /// <param name="losCheck">L3 视线检查（from, to）→ 是否无遮挡；
        /// 传入 null 时，若 <paramref name="options"/> 要求 L3 则视为被遮挡（保守失败）。</param>
        public static bool Evaluate(Vector3 origin, Vector3 forward, Vector3 target,
                                    in VisionParams p, QueryOptions options,
                                    Func<Vector3, Vector3, bool> losCheck)
        {
            var delta = target - origin;
            var distSq = delta.sqrMagnitude;

            // L1 距离：球检，最廉价的一层先做早退。
            if (distSq > p.maxDistance * p.maxDistance + Eps) return false;
            if (distSq <= Eps) return true;   // 目标就在视线上（含重叠），无方向可言，判可见。

            // 近身档：贴脸即察觉，跳过视角锥（但仍受 L3 遮挡约束——躲进掩体后不该被发现）。
            var proximal = p.proximityRadius > Eps &&
                           distSq <= p.proximityRadius * p.proximityRadius;

            // L2 视角锥：水平夹角 + 俯仰夹角。
            if (!proximal && (options & QueryOptions.ConeOnly) != 0)
            {
                if (!InHorizontalCone(delta, forward, p.horizontalAngleDegrees)) return false;
                if (!InVerticalCone(delta, forward, p.verticalAngleDegrees)) return false;
            }

            // L3 视线：几何遮挡。
            if ((options & QueryOptions.LineOfSight) != 0)
            {
                if (losCheck == null) return false;
                if (!losCheck(origin, target)) return false;
            }

            return true;
        }

        /// <summary>水平夹角判定。视角 ≥ 359.9° 视为全向（不看朝向）。</summary>
        public static bool InHorizontalCone(Vector3 delta, Vector3 forward, float horizontalAngleDegrees)
        {
            if (horizontalAngleDegrees >= 359.9f) return true;

            var fHor = Horizontal(forward);
            var dHor = Horizontal(delta);
            if (fHor.sqrMagnitude <= Eps || dHor.sqrMagnitude <= Eps) return true; // 正上/正下：水平面不约束

            var dot = Vector3.Dot(fHor, dHor) /
                      (Mathf.Sqrt(fHor.sqrMagnitude) * Mathf.Sqrt(dHor.sqrMagnitude));
            return dot >= Mathf.Cos(horizontalAngleDegrees * 0.5f * Mathf.Deg2Rad);
        }

        /// <summary>俯仰夹角判定：目标方向的仰角与视线自身仰角之差须在半垂直角内。</summary>
        public static bool InVerticalCone(Vector3 delta, Vector3 forward, float verticalAngleDegrees)
        {
            if (verticalAngleDegrees >= 179.9f) return true;

            var tPitch = PitchOf(delta);
            var fPitch = PitchOf(forward);
            return Mathf.Abs(tPitch - fPitch) <= verticalAngleDegrees * 0.5f;
        }

        /// <summary>把向量压到水平面（去掉 Y 分量）。</summary>
        static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>向量的仰角（度）。零水平分量时返回 ±90。</summary>
        static float PitchOf(Vector3 v)
        {
            var hor = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            if (hor <= Eps) return v.y >= 0f ? 90f : -90f;
            return Mathf.Atan2(v.y, hor) * Mathf.Rad2Deg;
        }
    }
}
