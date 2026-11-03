using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 全工程唯一的衰减求值口径（Modules/00 §3.3，D-00-7）。
    /// 场域（05 §1）与地形平整约束（01 §3.4）必须共用它，否则同名参数在两处手感不同、经验不可迁移。
    /// <para>约定：输入 <c>t∈[0,1]</c> 是**归一化半径进度**（0=内边界，1=外边界）；
    /// 输出 <c>s∈[0,1]</c> 是**从 innerValue 走向 outerValue 的进度**；曲线越界一律 Clamp01。</para>
    /// </summary>
    public static class Falloff
    {
        public enum Preset
        {
            Linear = 0,   ///<summary>匀速过渡</summary>
            EaseIn = 1,   ///<summary>近处缓、外缘骤降</summary>
            EaseOut = 2,  ///<summary>近处陡降、远尾缓延</summary>
            Step = 3,     ///<summary>硬边界（行政区式场域）</summary>
        }

        /// <summary>预设曲线求值（纯函数，不依赖 AnimationCurve，可进 Job）。</summary>
        public static float Evaluate(Preset preset, float t)
        {
            t = Mathf.Clamp01(t);
            switch (preset)
            {
                case Preset.Linear:  return t;
                case Preset.EaseIn:  return t * t * t;                     // 3 次入
                case Preset.EaseOut: return 1f - (1f - t) * (1f - t) * (1f - t);
                case Preset.Step:    return t >= 1f ? 1f : 0f;
                default: throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }

        /// <summary>
        /// 曲线求值：优先用共享曲线资产（被 ≥2 处复用时才成立，§0.1 原则2），
        /// 否则用内联曲线；两者都无则退到预设。null 安全——空曲线不报错，走预设。
        /// </summary>
        public static float Evaluate(AnimationCurve shared, AnimationCurve inlineCurve,
                                     Preset fallback, float t)
        {
            t = Mathf.Clamp01(t);
            if (shared != null && shared.length > 0) return Mathf.Clamp01(shared.Evaluate(t));
            if (inlineCurve != null && inlineCurve.length > 0) return Mathf.Clamp01(inlineCurve.Evaluate(t));
            return Evaluate(fallback, t);
        }

        /// <summary>场域求值（05 §1 定稿公式）：内平台 → 曲线过渡 → 外截止。</summary>
        public static int SampleField(float distance, float innerRadius, float outerRadius,
                                      int innerValue, int outerValue,
                                      AnimationCurve shared, AnimationCurve inlineCurve, Preset fallback)
        {
            if (outerRadius < innerRadius)
                throw new ArgumentException("outerRadius 必须 ≥ innerRadius（05 §4.2 手柄已强制，此处兜底）");

            if (distance <= innerRadius) return ClampValue(innerValue);
            if (distance > outerRadius) return 0;

            // 平台与影响区重合（inner==outer）时不做除零冒险：直接按外径处取值
            var span = outerRadius - innerRadius;
            if (span <= 1e-4f) return ClampValue(outerValue);

            var t = (distance - innerRadius) / span;
            var s = Evaluate(shared, inlineCurve, fallback, t);
            return ClampValue(Mathf.RoundToInt(Mathf.Lerp(innerValue, outerValue, s)));
        }

        /// <summary>四维强度整数标度钳制（D-02：0~100 整数）。</summary>
        public static int ClampValue(int v) => Mathf.Clamp(v, 0, 100);
    }
}
