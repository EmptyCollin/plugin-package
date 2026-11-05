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

        /// <summary>
        /// 填充「时刻 t 每格是否危险」掩码（1=危险，0=安全）。
        /// <para><b>实现刻意逐格走 <see cref="IsDangerousAt"/></b>，不开第二套判定循环：
        /// 时间轴热区、可达传播、（未来的）求解器剪枝全都吃这份掩码，
        /// 一旦这里自己按桶扫一遍位图，就会与查询口径悄悄分叉（本包 DESIGN 的单一真源纪律）。</para>
        /// </summary>
        /// <returns>掩码长度不足时 false（不部分写入，避免调用方拿到半张脏掩码）。</returns>
        public bool BuildDangerMask(float t, byte[] mask)
        {
            var n = CellCount;
            if (mask == null || mask.Length < n) return false;
            for (var c = 0; c < n; c++) mask[c] = IsDangerousAt(c, t) ? (byte)1 : (byte)0;
            return true;
        }

        // ── 足迹留痕：哪些格<b>真的被探测过</b> ─────────────────────
        /// <summary>每格 1=烘焙时真被射线探测过；0=<b>没有任何数据</b>（墙内、掩体内、地图外）。</summary>
        [SerializeField] byte[] _evaluated = Array.Empty<byte>();

        /// <summary>
        /// 登记本次烘焙实际探测过的格集合（footprint）。
        /// <para><b>为什么必须留这一痕</b>：墙体/掩体内部的格<b>从未被射线打过</b>，
        /// 它们的位图自然是全 0 ⇒ <see cref="IsDangerousAt"/> 会答「安全」。
        /// 可达传播与热区若照这个答案画，玩家就能<b>穿墙走过去</b>、UI 还会把墙涂成安全格。
        /// 这是「没有数据」被当成「数据说安全」的典型静默错误（与 D-17 同类）。</para>
        /// </summary>
        public void MarkEvaluated(IReadOnlyList<int> cells)
        {
            if (_evaluated.Length != CellCount) _evaluated = new byte[CellCount];
            for (var i = 0; i < _evaluated.Length; i++) _evaluated[i] = 0;
            if (cells == null) return;
            for (var i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c >= 0 && c < _evaluated.Length) _evaluated[c] = 1;
            }
        }

        /// <summary>
        /// 该格是否被探测过。<b>数组缺失时按 true 处理</b>（向后兼容手工拼表与旧资产：
        /// 它们没有足迹信息，此时不能反过来把所有格判成「无数据」，那会让一切传播全灭）。
        /// 新鲜烘焙的表一定会写足迹，故真实关卡不依赖这个默认值。
        /// </summary>
        public bool IsEvaluated(int cell)
        {
            if (_evaluated == null || _evaluated.Length != CellCount) return true;
            return cell >= 0 && cell < _evaluated.Length && _evaluated[cell] != 0;
        }

        /// <summary>是否记录了足迹（决定 UI 能否区分「无数据」与「安全」）。</summary>
        public bool HasFootprint => _evaluated != null && _evaluated.Length == CellCount;

        /// <summary>
        /// 填充「无数据格」掩码（1=未被探测，不可通行、也不得画成安全）。未记录足迹时全 0。
        /// </summary>
        public bool BuildNoDataMask(byte[] mask)
        {
            var n = CellCount;
            if (mask == null || mask.Length < n) return false;
            for (var c = 0; c < n; c++) mask[c] = IsEvaluated(c) ? (byte)0 : (byte)1;
            return true;
        }

        // ── 数据缺失留痕（表级）：视野在表里等于不存在的单位，在这里显式记账 ────────
        [SerializeField] List<string> _unknownNames = new List<string>();

        /// <summary>视野<b>没有真正进入这张表</b>的单位名。两类来源（<c>VisibilityBakeRunner</c> 收尾合并登记）：
        /// ①位姿不可解析而被<b>整片跳过</b>；②跑完了但<b>整条周期一个格都没看见</b>（D-17 第二道防线）。</summary>
        public IReadOnlyList<string> UnknownGuardNames => _unknownNames;

        /// <summary>
        /// 登记被跳过的单位名（烘焙器在<b>跳过</b>位姿不可解析的单位后调用）。
        /// <para>为什么必须留痕而不是直接不写：被跳过的哨兵在表里等同于「不存在」，
        /// 于是它的视野区会被画成安全——这是<b>谎报安全</b>，比谎报危险严重得多。
        /// 只打日志会被忘掉，UI 必须据此给可见警告。</para>
        /// <para>刻意<b>不做成逐格掩码</b>：我们并不知道被跳过的哨兵能看见哪些格，
        /// 把全场都涂成「未知」等于把有用信息全盖掉。所以是表级警告横幅 + 足迹才是逐格概念。</para>
        /// </summary>
        public void SetUnknownGuardNames(IReadOnlyList<string> names)
        {
            _unknownNames.Clear();
            if (names == null) return;
            for (var i = 0; i < names.Count; i++)
            {
                if (!string.IsNullOrEmpty(names[i])) _unknownNames.Add(names[i]);
            }
        }

        /// <summary>某分片在时刻 t 所在桶内<b>可见的格数</b>（诊断：这名哨兵此刻盯住了多大面积）。</summary>
        public int VisibleCellCountInBucket(int sliceIndex, float t)
        {
            if (sliceIndex < 0 || sliceIndex >= _slices.Count) return 0;
            var sl = _slices[sliceIndex];
            // 取模口径仍走 BucketOf（唯一真源），位扫描交给按桶号的那一条
            return VisibleCellCountInSliceBucket(sliceIndex, BucketOf(t, sl.cycleSeconds, sl.buckets));
        }

        /// <summary>
        /// 某分片<b>自身第 <paramref name="intraBucket"/> 桶</b>可见的格数（不做时间取模）。
        /// <para>泳道要「一名哨兵一整条周期的逐桶强度」，若外部用 <c>t = b × Δt</c> 反推桶号，
        /// 在周期不整除 Δt 时会因取模而错位 ⇒ 只能按片内桶号直接取。位扫描实现与上面共用这一条。</para>
        /// </summary>
        public int VisibleCellCountInSliceBucket(int sliceIndex, int intraBucket)
        {
            if (sliceIndex < 0 || sliceIndex >= _slices.Count) return 0;
            var sl = _slices[sliceIndex];
            if (intraBucket < 0 || intraBucket >= sl.buckets) return 0;
            var n = CellCount;
            var count = 0;
            for (var c = 0; c < n; c++)
            {
                if (GetBit(sl.bits, (long)intraBucket * n + c)) count++;
            }
            return count;
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
