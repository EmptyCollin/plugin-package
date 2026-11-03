using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>察觉累积/衰减的阈值组（由 <see cref="VisionProfile"/> 打包，供跨帧累加）。</summary>
    public struct AwarenessTuning
    {
        /// <summary>不可见时的回落速率（每秒）。</summary>
        public float decayPerSecond;

        /// <summary>越过则进入「疑惑」。</summary>
        public float suspicionThreshold;

        /// <summary>越过则进入「警觉」。</summary>
        public float alertThreshold;
    }

    /// <summary>
    /// 察觉度累加模型（纯函数，无状态、不碰物理与帧循环）。
    /// <para>做成纯函数的理由与 M1 同源：B 包要能在<b>编辑态</b>预演
    /// 「玩家站在这里 n 秒会被发现到什么程度」，靠帧积分的版本在编辑器里算不出来。</para>
    /// </summary>
    public static class AwarenessModel
    {
        /// <summary>察觉度下限：低于此值归零并回到未察觉档。</summary>
        public const float Floor = 0f;

        /// <summary>察觉度上限。</summary>
        public const float Ceiling = 1f;

        /// <summary>
        /// 推进一个时间步。
        /// </summary>
        /// <param name="suspicion">当前察觉度 [0,1]。</param>
        /// <param name="dt">时间步长（秒）。</param>
        /// <param name="gainPerSecond">本帧被看见时的上升速率（已含层级判定）。</param>
        /// <param name="exposureScale">L4 遮蔽修正后的有效速率倍率 [0,1]（蹲伏/光照/安全岛）。</param>
        /// <param name="seen">此刻是否被任一视野看到。</param>
        /// <param name="tuning">阈值与回落速率。</param>
        public static float Integrate(float suspicion, float dt, float gainPerSecond,
                                      float exposureScale, bool seen, in AwarenessTuning tuning)
        {
            if (dt <= 0f) return Clamp(suspicion);

            if (seen)
            {
                var scale = Clamp01(exposureScale);
                suspicion += Mathf.Max(0f, gainPerSecond) * scale * dt;
            }
            else
            {
                suspicion -= Mathf.Max(0f, tuning.decayPerSecond) * dt;
            }

            if (suspicion <= Floor) return Floor;
            if (suspicion > Ceiling) return Ceiling;
            return suspicion;
        }

        /// <summary>
        /// 由察觉度导出档位。
        /// <para>阈值 ≤ 0 视为「该档未启用」——否则 <c>default(AwarenessTuning)</c>（阈值全 0）
        /// 会让察觉度 0 被读成「已疑惑」，辅助台的分层着色会整片错色。</para>
        /// </summary>
        public static StealthDetectionStage StageOf(float suspicion, in AwarenessTuning tuning)
        {
            if (tuning.alertThreshold > Floor && suspicion >= tuning.alertThreshold) return StealthDetectionStage.Alerted;
            if (tuning.suspicionThreshold > Floor && suspicion >= tuning.suspicionThreshold) return StealthDetectionStage.Suspicious;
            return StealthDetectionStage.Patrol;
        }

        /// <summary>
        /// L4 有效速率倍率：光照敏感度与蹲伏/掩体的合成，安全岛直接归零。
        /// <para>只影响<b>累积速率</b>，不影响可见性判定（DESIGN §3.3：L4 不改几何结论）。</para>
        /// </summary>
        public static float ExposureScale(float lightSensitivity, float targetExposure, bool crouching, StealthMask masks)
        {
            if ((masks & StealthMask.SafeIsland) != 0) return 0f;

            var s = Clamp01(lightSensitivity);
            var scale = (1f - s) + s * Clamp01(targetExposure);

            if ((masks & StealthMask.CoverOrCrouch) != 0) scale *= CrouchFactor;
            return Clamp01(scale);
        }

        /// <summary>蹲伏/掩体对察觉速率的折扣。</summary>
        public const float CrouchFactor = 0.35f;

        static float Clamp(float v)
        {
            if (v < Floor) return Floor;
            if (v > Ceiling) return Ceiling;
            return v;
        }

        static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
