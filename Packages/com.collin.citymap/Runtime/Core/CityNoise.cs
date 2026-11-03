using System;

namespace Collin.CityMap
{
    /// <summary>
    /// 自带确定性噪声内核（Modules/00 §3.2，01 §3.1）。
    /// <para>不用 Unity <c>Mathf.PerlinNoise</c>：其精度与跨平台一致性不可控，而 A-1 要求"同输入同输出"
    /// 是跨机器、跨会话成立的事实，不是尽力而为。</para>
    /// <para>也不用 <c>Unity.Mathematics</c>：本包依赖纪律是"零第三方/未声明 UPM 包"（总纲 §2），
    /// 引用未在 package.json 声明的包会让依赖真源失真。</para>
    /// <para><b>纯函数纪律</b>：无静态可变状态、无随会话变化的缓存查找表。
    /// 位运算与整数乘法为主，浮点仅在最外层参与，降低跨平台舍入差异面。</para>
    /// </summary>
    public static class CityNoise
    {
        /// <summary>格点值：splitmix 混列 → [0,1)。取高 24 位，避免低位相关性。</summary>
        public static float Value2D(ulong seed, int ix, int iz)
        {
            var h = CityRng.Mix(seed, CityRng.Mix((uint)ix, (uint)iz));
            return (h >> 40) * (1f / 16777216f);      // 24 位尾数 → 1/2^24
        }

        /// <summary>五次平滑插值 6t⁵−15t⁴+10t³（C2 连续，消除格点对齐造成的视觉阶梯）。</summary>
        static float SmootherStep(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>二维 value noise，输出 [0,1]。</summary>
        public static float Noise2D(ulong seed, float x, float z)
        {
            var ix = (int)Math.Floor((double)x);
            var iz = (int)Math.Floor((double)z);
            var fx = SmootherStep(x - ix);
            var fz = SmootherStep(z - iz);

            var a = Value2D(seed, ix, iz);
            var b = Value2D(seed, ix + 1, iz);
            var c = Value2D(seed, ix, iz + 1);
            var d = Value2D(seed, ix + 1, iz + 1);

            return Lerp(Lerp(a, b, fx), Lerp(c, d, fx), fz);
        }

        /// <summary>
        /// 分形叠加（fbm）：Σ gain^k · noise(p/λ_k)，归一化到约 [0,1]、均值约 0.5。
        /// </summary>
        public static float Fbm(ulong seed, float x, float z, float wavelength, int octaves,
                                float gain, float lacunarity)
        {
            if (octaves <= 0) return 0f;

            float sum = 0f, amp = 1f, norm = 0f, lambda = wavelength;
            // lacunarity ≤ 1 会让八度不分离（噪声退化为单层），钳到略大于 1
            var lac = Math.Max(1.0001f, lacunarity);

            for (var o = 0; o < octaves; o++)
            {
                var s = CityRng.Mix(seed, (ulong)o);
                sum += amp * Noise2D(s, x / lambda, z / lambda);
                norm += amp;
                amp *= Math.Max(0f, Math.Min(1f, gain));
                lambda *= lac;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>中心化到约 [−0.5, 0.5]，供"以 0 为均值"的地形基底使用。</summary>
        public static float SignedFbm(ulong seed, float x, float z, float wavelength, int octaves,
                                      float gain, float lacunarity)
            => Fbm(seed, x, z, wavelength, octaves, gain, lacunarity) - 0.5f;

        /// <summary>
        /// 域扭曲：采样点自身按噪声位移，打破 fbm 的规则格感（01 §3.1）。
        /// 偏移常数为固定值，保证纯函数（不可用时间/帧序）。
        /// </summary>
        public static void DomainWarp(ulong seed, ref float x, ref float z, float amp, float wavelength)
        {
            if (amp <= 0f) return;
            const float offA = 1191.3f, offB = -3727.9f;
            var dx = SignedFbm(seed, x + offA, z + offA, wavelength, 2, 0.5f, 2f);
            var dz = SignedFbm(seed, x + offB, z + offB, wavelength, 2, 0.5f, 2f);
            x += dx * amp;
            z += dz * amp;
        }

        /// <summary>smoothstep(edge0→edge1)，与 01 §3.1 的城内外衰减口径一致（钳到 [0,1]）。</summary>
        public static float SmoothStep(float edge0, float edge1, float v)
        {
            if (Math.Abs(edge1 - edge0) < 1e-6f) return v >= edge1 ? 1f : 0f;
            var t = (v - edge0) / (edge1 - edge0);
            t = Math.Max(0f, Math.Min(1f, t));
            return t * t * (3f - 2f * t);
        }
    }
}
