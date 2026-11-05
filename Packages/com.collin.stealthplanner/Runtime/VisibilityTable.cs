using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.StealthPlanner
{
    /// <summary>
    /// <b>时空可见性查找表</b>（DESIGN D-01 / D-02 的落地结构）——本包唯一的数据真源。
    /// <para>存在理由：玩法包的可见性判定底层是 <c>Physics.RaycastNonAlloc</c>，只能主线程、每次极贵。
    /// 于是「主线程烘一次成表 → 之后求解/可视化全走纯 CPU 查表」，一举拿到
    /// 可后台并行、编辑器与打包后引擎共用、且与玩法包判定同源三个好处。</para>
    /// <para><b>分片口径（关键，别改错）</b>：不物化全局 LCM 周期。第 i 个哨兵的可见性是
    /// 它<b>自身周期 c_i</b> 的纯函数，故每片只覆盖 <c>[0, c_i)</c>；查任意 t 用 <c>t mod c_i</c>
    /// 落到该片的时间桶。体积从 <c>格×(LCM/Δt)</c> 降到 <c>Σᵢ 格×⌈c_i/Δt⌉</c>，且改一个 NPC 只重烘一片。</para>
    /// <para><b>本类不含任何可见性判定逻辑</b>（DESIGN §4 红线：判定唯一来源是玩法包的
    /// <c>IVisionWorld</c>）。表只存烘焙好的位；填位由 Editor 侧烘焙器调契约完成。</para>
    /// <para>纯托管结构，不引用 UnityEngine 场景对象（除 <see cref="Vector3"/>），故可脱离 Play 态断言。</para>
    /// </summary>
    [Serializable]
    public sealed class VisibilityTable
    {
        /// <summary>单个哨兵的可见性分片（覆盖它自己的一个周期）。</summary>
        [Serializable]
        public struct GuardSlice
        {
            /// <summary>单位 id（<c>StealthIds</c> 口径，域重载后会变，故仅作当次烘焙键、不持久依赖）。</summary>
            public int actorId;
            /// <summary>显示名（供面板标签；来自契约 <c>StealthActorInfo.displayName</c>）。</summary>
            public string displayName;
            /// <summary>该单位路线的 T 周期（秒）。取模基准。</summary>
            public float cycleSeconds;
            /// <summary>本片时间桶数 = ⌈cycleSeconds / Δt⌉（≥1）。</summary>
            public int buckets;
            /// <summary>打包位图：第 <c>bucket*CellCount + cell</c> 位 = 该桶该格是否被本哨兵看见。</summary>
            public byte[] bits;
        }

        [Tooltip("世界→格换算的左下角（含）；cell 线性索引 = z*dimX + x")]
        [SerializeField] Vector3 _origin;
        [Tooltip("网格边长（米），烘焙时来自 PlannerBakeSettings，非写死")]
        [SerializeField] float _cellSize = 0.5f;
        [SerializeField] int _dimX;
        [SerializeField] int _dimZ;
        [Tooltip("时间桶 Δt（秒）")]
        [SerializeField] float _timeStep = 0.5f;

        [SerializeField] List<GuardSlice> _slices = new List<GuardSlice>();

        /// <summary>跨片 AND 的派生快表：每格 1 字节，1=永久安全（所有哨兵全周期都看不见），0=动态格。
        /// 永久安全格可让求解器不展开时间维（剪枝①）；查询走它，其余落回逐片 mod。</summary>
        [SerializeField] byte[] _permanentSafe = Array.Empty<byte>();

        public Vector3 Origin => _origin;
        public float CellSize => _cellSize;
        public float TimeStep => _timeStep;
        public int DimX => _dimX;
        public int DimZ => _dimZ;
        public int CellCount => _dimX * _dimZ;
        public IReadOnlyList<GuardSlice> Slices => _slices;

        // ── 装配（烘焙器调用）──────────────────────────────────────

        /// <summary>设定网格几何。<paramref name="origin"/> 为 x/z 左下角（y 取场地基准）。</summary>
        public void ConfigureGrid(Vector3 origin, float cellSize, int dimX, int dimZ, float timeStep)
        {
            _origin = origin;
            _cellSize = Mathf.Max(0.01f, cellSize);
            _timeStep = Mathf.Max(0.01f, timeStep);
            _dimX = Mathf.Max(1, dimX);
            _dimZ = Mathf.Max(1, dimZ);
            _permanentSafe = new byte[CellCount];
        }

        /// <summary>清空所有分片（重烘前调用）。</summary>
        public void ClearSlices()
        {
            _slices.Clear();
            for (var i = 0; i < _permanentSafe.Length; i++) _permanentSafe[i] = 0;
        }

        /// <summary>
        /// 追加一个哨兵分片。<paramref name="buckets"/> 应等于 ⌈cycleSeconds/Δt⌉；位图长度自动按
        /// <c>buckets×CellCount</c> 打包。<b>调用方负责填位</b>（用 <see cref="SetSliceVisible"/>），
        /// 本类不知道任何判定规则。
        /// </summary>
        public int AddSlice(int actorId, string displayName, float cycleSeconds, int buckets)
        {
            buckets = Mathf.Max(1, buckets);
            var bits = new byte[((long)buckets * CellCount + 7) / 8];
            _slices.Add(new GuardSlice
            {
                actorId = actorId,
                displayName = displayName,
                cycleSeconds = Mathf.Max(0.0001f, cycleSeconds),
                buckets = buckets,
                bits = bits
            });
            return _slices.Count - 1;
        }

        /// <summary>标记某片某桶某格「被看见」。<paramref name="slice"/> 越界即忽略（不抛，防烘焙循环炸在半路）。</summary>
        public void SetSliceVisible(int slice, int bucket, int cell, bool visible)
        {
            if (slice < 0 || slice >= _slices.Count) return;
            var s = _slices[slice];
            if (bucket < 0 || bucket >= s.buckets || cell < 0 || cell >= CellCount) return;
            SetBit(s.bits, (long)bucket * CellCount + cell, visible);
            // 结构体存回（GuardSlice 是值类型）
            _slices[slice] = s;
        }

        /// <summary>
        /// 重算永久安全快表（跨所有片做「该格在任何桶都不可见」的 AND）。分片全部填完后调用一次。
        /// <para>严格正确：<c>permanentSafe=1</c> 意味着对<b>所有</b>哨兵、其<b>全周期</b>都看不见该格——
        /// 与相位窗 P 是否对齐无关（每片本就覆盖各自完整周期），故求解器可无条件走快表。</para>
        /// </summary>
        public void RebuildPermanentSafeLayer()
        {
            var n = CellCount;
            if (_permanentSafe.Length != n) _permanentSafe = new byte[n];

            for (var c = 0; c < n; c++) _permanentSafe[c] = 1;                 // 先假设都安全
            for (var s = 0; s < _slices.Count; s++)
            {
                var sl = _slices[s];
                for (var c = 0; c < n; c++)
                {
                    if (_permanentSafe[c] == 0) continue;                      // 已判危险，跳过
                    for (var b = 0; b < sl.buckets; b++)
                    {
                        if (GetBit(sl.bits, (long)b * n + c)) { _permanentSafe[c] = 0; break; }
                    }
                }
            }
        }

        // ── 查询（求解器 / 可视化 / 引擎侧复用，O(哨兵数) 非 O(格)）──

        /// <summary>世界坐标 → 格线性索引；越出网格返回 false（输出不可信）。</summary>
        public bool TryCellIndex(Vector3 world, out int cell)
        {
            cell = -1;
            var fx = Mathf.FloorToInt((world.x - _origin.x) / _cellSize);
            var fz = Mathf.FloorToInt((world.z - _origin.z) / _cellSize);
            if (fx < 0 || fz < 0 || fx >= _dimX || fz >= _dimZ) return false;
            cell = fz * _dimX + fx;
            return true;
        }

        /// <summary>格索引 → 格中心世界坐标（y 用表基准 origin.y，判定另由调用方给观察高度）。</summary>
        public Vector3 CellCenterWorld(int cell)
        {
            var x = cell % _dimX;
            var z = cell / _dimX;
            return new Vector3(
                _origin.x + (x + 0.5f) * _cellSize,
                _origin.y,
                _origin.z + (z + 0.5f) * _cellSize);
        }

        /// <summary>该格是否永久安全（所有哨兵全周期都看不见）。越界返回 false（保守：当作需查表）。</summary>
        public bool IsPermanentlySafe(int cell)
        {
            return cell >= 0 && cell < _permanentSafe.Length && _permanentSafe[cell] != 0;
        }

        /// <summary>
        /// 某格在时刻 t 是否<b>危险</b>（被任一哨兵看见）。核心查询，纯 CPU。
        /// <para>先查永久安全快表（命中即安全，短路）；否则对每个哨兵取 <c>t mod c_i</c> 落到其片桶，
        /// 任一桶看见即危险。时间复杂度 = 哨兵数，与格数、相位窗长度无关。</para>
        /// </summary>
        public bool IsDangerousAt(int cell, float t)
        {
            if (cell < 0 || cell >= CellCount) return true;                    // 越界保守判危险
            if (_permanentSafe.Length == CellCount && _permanentSafe[cell] != 0) return false;

            for (var s = 0; s < _slices.Count; s++)
            {
                var sl = _slices[s];
                var b = BucketOf(t, sl.cycleSeconds, sl.buckets);
                if (GetBit(sl.bits, (long)b * CellCount + cell)) return true;
            }
            return false;
        }

        /// <summary>世界点 + 时刻的危险查询（先做世界→格换算）。越出网格保守返回 true。</summary>
        public bool IsDangerousWorld(Vector3 world, float t)
        {
            return !TryCellIndex(world, out var cell) || IsDangerousAt(cell, t);
        }

        // ── 统计（体积与剪枝有效性诊断，供验收「体积在预算内」直接读数）──

        /// <summary>永久安全格数（剪枝①砍掉的时间扩展节点规模）。</summary>
        public int PermanentlySafeCellCount
        {
            get
            {
                var c = 0;
                for (var i = 0; i < _permanentSafe.Length; i++) if (_permanentSafe[i] != 0) c++;
                return c;
            }
        }

        /// <summary>位图本体字节数（不含元数据），体积预算核验用。</summary>
        public long PackedByteCount
        {
            get
            {
                long sum = _permanentSafe.Length;
                for (var s = 0; s < _slices.Count; s++)
                    sum += _slices[s].bits == null ? 0 : _slices[s].bits.Length;
                return sum;
            }
        }

        // ── 桶映射与位打包（内部）─────────────────────────────────

        /// <summary>
        /// t 落到某哨兵片的桶号：<c>b = ⌊(t mod c_i) / (c_i / buckets)⌋</c>，钳到 <c>[0, buckets-1]</c>。
        /// <c>c_i ≤ 0</c> 视为常数（永远桶 0）。
        /// <para><b>桶宽用 <c>c_i/buckets</c> 而非直接除以 Δt</b>：当 c_i 不是 Δt 的整数倍时，
        /// <c>buckets=⌈c_i/Δt⌉</c> 会让「÷Δt」在尾部算出 <c>b==buckets</c> 越界；按周期均分则恒落在
        /// <c>[0,buckets-1]</c>，代价仅是尾桶实际跨度略小于 Δt（保守可接受，不会漏判危险）。</para>
        /// <para>公开静态纯函数，是「分片用 mod 而非全局 LCM」口径的<b>唯一真源</b>：
        /// 烘焙器<b>填桶</b>与查询<b>取桶</b>必须走同一个它，否则填进去的位查不出来（整张表静默失效）。
        /// 独立测试程序集亦经此钉住口径（受限可见会跨不到）。</para>
        /// </summary>
        public static int BucketOf(float t, float cycleSeconds, int buckets)
        {
            if (buckets <= 1 || cycleSeconds <= 0f) return 0;
            var m = t % cycleSeconds;
            if (m < 0f) m += cycleSeconds;                                    // 负 t（回溯/未来）也要落正桶
            var b = Mathf.FloorToInt(m / (cycleSeconds / buckets));            // 桶宽 = c_i/buckets
            return Mathf.Clamp(b, 0, buckets - 1);
        }

        static bool GetBit(byte[] arr, long bit)
        {
            if (arr == null || bit < 0 || bit >= (long)arr.Length * 8) return false;
            return (arr[bit >> 3] & (1 << (int)(bit & 7))) != 0;
        }

        static void SetBit(byte[] arr, long bit, bool v)
        {
            if (arr == null || bit < 0 || bit >= (long)arr.Length * 8) return;
            var i = bit >> 3;
            var mask = 1 << (int)(bit & 7);
            if (v) arr[i] |= (byte)mask;
            else arr[i] &= (byte)~mask;
        }
    }
}
