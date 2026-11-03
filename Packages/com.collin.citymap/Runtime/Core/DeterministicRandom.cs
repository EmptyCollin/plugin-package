using System;

namespace Collin.CityMap
{
    /// <summary>
    /// 全城唯一随机源（Modules/00 §3，D-10）。
    /// <para>铁律：一切随机必须经本类以 <c>空间位置 / 稳定 id</c> 派生，
    /// 禁止遍历序、集合枚举序、<c>UnityEngine.Random</c>、无种子 <c>System.Random</c>、时间/帧序参与。</para>
    /// 全整数、无静态状态、纯函数 —— 同一输入在进程重启、域重载、顺序变化后取值完全一致（A-0-1）。
    /// </summary>
    public static class CityRng
    {
        public const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

        /// <summary>splitmix64 混合步（Steele 等《Fast Splittable Pseudorandom Number Generators》的 finalizer 形式）。</summary>
        public static ulong Mix(ulong seed, ulong salt)
        {
            ulong z = seed + salt * GoldenGamma;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5F9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>阶段种子：stage ∈ {S0..S6}（用 <see cref="StageId"/> 的整数值）。</summary>
        public static ulong StageSeed(ulong rootSeed, StageId stage) => Mix(rootSeed, (ulong)stage);

        /// <summary>按空间位置的格种子——局部稳定性的根基（00 D-10）。</summary>
        public static ulong CellSeed(ulong rootSeed, StageId stage, int gridX, int gridZ)
        {
            var posHash = Mix((ulong)(uint)gridX, (ulong)(uint)gridZ);
            return Mix(StageSeed(rootSeed, stage), posHash);
        }

        /// <summary>按稳定 id 的对象种子（场域 stableId 哈希、slotKey、parcel id 等）。</summary>
        public static ulong ObjectSeed(ulong rootSeed, StageId stage, long stableIdHash)
            => Mix(StageSeed(rootSeed, stage), (ulong)stableIdHash);

        /// <summary>一次性取格上的 64 位值（无流状态，可并行、可重放）。</summary>
        public static ulong NextCellU64(ulong rootSeed, StageId stage, int gridX, int gridZ, ulong salt = 0)
            => Mix(CellSeed(rootSeed, stage, gridX, gridZ), salt);

        /// <summary>32 位派生：slotKey / parcel id 等键空间用（高位再折叠，避免低位相关性）。</summary>
        public static int NextCellI32(ulong rootSeed, StageId stage, int gridX, int gridZ, ulong salt = 0)
        {
            var v = NextCellU64(rootSeed, stage, gridX, gridZ, salt);
            return unchecked((int)(v ^ (v >> 32)));
        }
    }

    /// <summary>
    /// 可推进的 splitmix64 流。只允许由 <see cref="CityRng"/> 派生的种子创建，
    /// 且流本身不得跨对象共享（每对象/每格独立流，否则又会引入顺序依赖）。
    /// </summary>
    public struct RngStream
    {
        private ulong _state;

        public RngStream(ulong seed) { _state = seed; }

        public static RngStream ForCell(ulong rootSeed, StageId stage, int x, int z, ulong salt = 0)
            => new RngStream(CityRng.NextCellU64(rootSeed, stage, x, z, salt));

        public static RngStream ForObject(ulong rootSeed, StageId stage, long stableIdHash, ulong salt = 0)
            => new RngStream(CityRng.Mix(CityRng.ObjectSeed(rootSeed, stage, stableIdHash), salt));

        public ulong NextU64()
        {
            _state += CityRng.GoldenGamma;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5F9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return unchecked((int)(NextU64() % (ulong)maxExclusive));
        }

        public int NextRange(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive) throw new ArgumentException("max < min");
            var span = (ulong)((long)maxInclusive - minInclusive + 1);
            return unchecked((int)(NextU64() % span) + minInclusive);
        }

        /// <summary>[0,1)：(x >> 11) · 2⁻⁵³，双精度可精确表示的 53 位尾数。</summary>
        public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

        public bool NextBool(double trueProbability = 0.5) => NextDouble() < trueProbability;
    }

    /// <summary>
    /// 集合的稳定哈希（08 §5.1、11 §6）：输入必须先按 stableId 有序再折叠。
    /// 禁止直接喂字典/集合的枚举序（00 §3.1 铁律）。
    /// </summary>
    public static class IntentHash
    {
        /// <summary>折叠 (stableId, manualVersion) 对。要求调用方保证按 stableId 字典序输入。</summary>
        public static long Fold(System.Collections.Generic.IEnumerable<(string stableId, long version)> orderedPairs)
        {
            unchecked
            {
                ulong acc = 0xCBF29CE484222325UL; // FNV 偏移基，仅作非零起点
                string last = null;
                foreach (var (id, ver) in orderedPairs)
                {
                    if (string.IsNullOrEmpty(id))
                        throw new ArgumentException("意图对象必须有非空 stableId");
                    // 有序性自检：乱序输入会静默破坏确定性，这里主动抛。
                    if (last != null && string.CompareOrdinal(last, id) > 0)
                        throw new InvalidOperationException(
                            $"IntentHash.Fold 输入未按 stableId 有序（{last} > {id}），违反 00 §3.1 铁律");
                    last = id;
                    var idHash = (ulong)StableStringHash(id);
                    acc = CityRng.Mix(acc, idHash ^ (ulong)ver);
                }
                return (long)acc;
            }
        }

        /// <summary>stableId 字符串的确定性数值（跨版本不依赖 String.GetHashCode，后者不保证稳定）。</summary>
        public static int StableStringHash(string s)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (int i = 0; i < s.Length; i++)
                {
                    h = (h ^ s[i]) * 1099511628211UL;
                }
                var v = (long)(h ^ (h >> 32));
                return (int)(v ^ (v >> 32));
            }
        }
    }
}
