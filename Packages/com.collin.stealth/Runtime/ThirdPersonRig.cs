using System;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 第三人称操控的纯数学层（可测、无状态）。
    /// <para>存在理由：本包的地基纪律是「几何判定与运动学做成纯函数」，
    /// 操控手感的核心数学同样必须能脱离 Play 态断言——
    /// 否则「A/D 是侧移还是转镜头」这类问题只能靠人肉进 Play 试。</para>
    /// </summary>
    public static class ThirdPersonRig
    {
        /// <summary>
        /// 把「摇杆/键位的二维输入」换算成<b>相机相对</b>的世界平面移动方向。
        /// <para>这是 A/D 语义的唯一定义处：<c>x&gt;0</c> = 沿<b>相机右手边</b>侧移，
        /// <c>y&gt;0</c> = 沿<b>相机朝向</b>前进。绝不与角色自身朝向耦合，
        /// 否则「按 A 变成原地转身、镜头跟着转」。</para>
        /// </summary>
        /// <param name="cameraYawDegrees">相机水平角（度，Unity 左手系：0=+Z，90=+X）。</param>
        /// <param name="input">x=左右、y=前后，取值 [-1,1]。</param>
        /// <returns>已归一化的世界平面方向；输入为零向量时返回 <see cref="Vector3.zero"/>。</returns>
        public static Vector3 MoveDirection(float cameraYawDegrees, Vector2 input)
        {
            var flat = new Vector3(input.x, 0f, input.y);
            if (flat.sqrMagnitude <= 1e-8f) return Vector3.zero;
            if (flat.sqrMagnitude > 1f) flat.Normalize();

            // 绕 Y 旋转 camYaw：把「相机坐标」的输入向量映到世界平面。
            return Quaternion.Euler(0f, cameraYawDegrees, 0f) * flat;
        }

        /// <summary>
        /// 角度指数平滑（帧率无关）。
        /// <para>用 <c>1 - e^(-k·dt)</c> 而非 <c>Lerp(a,b,dt·k)</c>：后者在 15 FPS
        /// 录屏或 144 FPS 高刷屏上收敛速度差好几倍，是「拖镜头时画面抖」的常见来源之一
        /// （旋转瞬时到位、位置慢慢追，两者脱节就会抖）。</para>
        /// </summary>
        public static float SmoothAngle(float current, float target, float stiffnessPerSecond, float dt)
        {
            if (dt <= 0f || stiffnessPerSecond <= 0f) return target;
            var t = 1f - Mathf.Exp(-stiffnessPerSecond * dt);
            return Mathf.Repeat(current + Mathf.DeltaAngle(current, target) * t, 360f);
        }

        /// <summary>标量指数平滑（帧率无关），用于相机距离收放与位置跟随。</summary>
        public static float SmoothValue(float current, float target, float stiffnessPerSecond, float dt)
        {
            if (dt <= 0f || stiffnessPerSecond <= 0f) return target;
            var t = 1f - Mathf.Exp(-stiffnessPerSecond * dt);
            return current + (target - current) * t;
        }

        /// <summary>把俯仰角钳进窗口（上限必须 ≥ 下限，反了会每次拖拽来回弹）。</summary>
        public static float ClampPitch(float pitch, float minDegrees, float maxDegrees)
        {
            if (maxDegrees < minDegrees)
            {
                var tmp = minDegrees;
                minDegrees = maxDegrees;
                maxDegrees = tmp;
            }
            return Mathf.Clamp(pitch, minDegrees, maxDegrees);
        }

        /// <summary>
        /// 视觉胶囊贴合：返回 <c>(localScale.y, localPosition.y)</c>，使胶囊<b>底边恰好落在脚底</b>。
        /// <para>存在理由（用户实机反馈「玩家一出场，胶囊体半个在地里」）：旧实现写
        /// <c>localPosition.y = (h - standHeight) * 0.5f</c>，站立时算出 <b>0</b> ⇒ 整根胶囊
        /// 中心落到脚底、下半截埋进地面。几何真源是 Unity Capsule 基元的
        /// <c>scale.y = 1 ⇔ 总高 2m</c>，故中心高度必须等于总高之半。</para>
        /// <para>x/z 分量不在此处决定（保留作者摆好的粗细），本函数只管纵向。</para>
        /// </summary>
        /// <param name="currentHeight">当前角色身高（站立传站高、蹲伏传蹲高）。</param>
        /// <returns>x=缩放后的 localScale.y，y=贴合后的 localPosition.y。</returns>
        public static Vector2 CapsuleVisualPose(float currentHeight)
        {
            var scale = Mathf.Max(0.01f, currentHeight) * 0.5f;   // 基元总高 = 2 × scale.y
            return new Vector2(scale, scale);                     // 中心 = scale ⇒ 底边贴 y=0
        }
    }
}
