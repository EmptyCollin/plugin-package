using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>人工雕刻的叠加方式（01 §2.3）。</summary>
    public enum DeltaMode { Additive = 0, Absolute = 1 }

    /// <summary>
    /// 人工雕刻层（01 §2.3，重放次序第 3 层）。<b>存增量不存绝对高度</b>（D-01-1）：
    /// 改噪声参数后人工意图以"相对改动量"存活；"压平到指定值"用 <see cref="DeltaMode.Absolute"/>
    /// 重放即赋值目标高程。<b>本类只做数据 + 重放</b>，不含笔刷几何、不引用 UnityEditor。
    /// 管线接线已在 M4-c 完成（DESIGN **D-31** 批准"雕刻须触发 S1b 重算"）：
    /// S1b 起点复位后由 <c>FlattenSolveStage</c> 调 <see cref="ApplyTo"/> 叠加，故本类自身不碰任何阶段。
    /// <para><b>本类同时是意图登记对象</b>（2026-11-06，用户反馈「笔刷撤销不生效」的正解）：
    /// 落笔脏位原先只记在 <see cref="CityTerrain"/>（场景组件）的账本上，而
    /// <c>Undo.RecordObject</c> 登记的是<b>本资产</b> ⇒ 撤销把 chunks 还原了、实例账本的序列号却原地不动 ⇒
    /// Tracker 的两条检（通道序列号差 + 集合哈希）全都看不见 ⇒ 数据回到过去、画面停在未来。
    /// 这正是 09 H-9 那条纪律的反面教材：<b>账本必须长在"会被撤销还原的那个对象"上</b>。
    /// 现在 chunks 与 ledger 同属本资产，一次撤销同时还原二者 ⇒ 序列号与基线不等 ⇒ 自动复用整条脏传播链，
    /// 撤销专用的恢复代码一行都不需要。</para>
    /// <para>记账<b>双写</b>（本资产 + 实例）：实例那份必须留着，因为显示层按
    /// <c>_terrain.Ledger.BoundsOf(DeltaStroke)</c> 取脏盒做增量重建（CityTerrainView 自建路径）；
    /// 两者都是同一个 DeltaStroke 位，Tracker 按位并入挂起集合 ⇒ 不会跑两遍。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/CityMap/Sculpt Delta", fileName = "SculptDelta")]
    public sealed class SculptDeltaAsset : ScriptableObject, IIntentTracked
    {
        /// <summary>绝对高程"无值"哨兵。0 是合法高程，必须用 NaN 区分——
        /// 与 11 §2 的 heightValue 双义哨兵是同一类坑（拿 0 当哨兵会静默吞掉"用户就要 0m"）。</summary>
        public const float NoTarget = float.NaN;

        /// <summary>与 00 §4.5 资产快照同构的存盘格式版本（09 H-24 教训：无版本则旧档误读无声）。</summary>
        [SerializeField] int schemaVersion = 1;

        [Serializable]
        struct DeltaChunk
        {
            public int chunkX;            // 逻辑 chunk 坐标（floor 语义）
            public int chunkZ;
            public DeltaMode mode;
            public float[] values;        // 行主序，长度 = cellsPerChunk²
                                          // Additive: 增量（0=无）；Absolute: 目标高程或 NoTarget
        }

        // ⚠️ **不能有 readonly**：Unity 序列化管线会跳过 readonly 字段（即便带 [SerializeField]）。
        // 本轮在磁盘上取证：本资产保存后 YAML 里<b>只有 schemaVersion 一行</b>，chunks 整个消失
        // ⇒ 用户雕的山重开工程就没了（与 WaterNetworkAsset 同一写法、同一后果，两处一起改）。
        [SerializeField] System.Collections.Generic.List<DeltaChunk> chunks
            = new System.Collections.Generic.List<DeltaChunk>(16);

        // 增量清零容差：笔刷固定步长积分会累出 1e-8 级残差，超过就当"这格没内容"，触发稀疏剪枝。
        const float ZeroEps = 1e-6f;

        public int ChunkCount => chunks.Count;
        public bool IsEmpty => chunks.Count == 0;
        public int SchemaVersion => schemaVersion;

        /// <summary>一个逻辑 chunk 每边多少格（chunkSize/cellSize；CityGrid 构造已保证整除）。</summary>
        public static int CellsPerChunk(in CityGrid g)
            => g.ChunkSize > 0f && g.CellSize > 0f
                ? Mathf.Max(1, Mathf.RoundToInt(g.ChunkSize / g.CellSize)) : 0;

        /// <summary>叠加一笔增量（笔刷积分入口，01 §5.1）。全零后 chunk 条目被剪除（契约）。</summary>
        public void AddDelta(in CityGrid g, int cx, int cz, float delta)
            => WriteCell(g, cx, cz, DeltaMode.Additive, delta, accumulate: true);

        /// <summary>压平到绝对高程（01 §5.1「压平到指定值」；重放时直接赋值，噪声变化不摧毁绝对语义）。</summary>
        public void SetAbsolute(in CityGrid g, int cx, int cz, float targetHeight)
            => WriteCell(g, cx, cz, DeltaMode.Absolute, targetHeight, accumulate: false);

        /// <summary>清除某格的绝对目标（回到 NoTarget 哨兵；整块清空则剪除条目）。</summary>
        public void ClearAbsolute(in CityGrid g, int cx, int cz)
            => WriteCell(g, cx, cz, DeltaMode.Absolute, NoTarget, accumulate: false);

        void WriteCell(in CityGrid g, int cx, int cz, DeltaMode mode, float value, bool accumulate)
        {
            var cpc = CellsPerChunk(g);
            if (cpc <= 0) return;
            // 图外拒写：越界既不崩也不制造"永远读不到却占位"的幽灵格
            if (cx < 0 || cz < 0 || cx >= g.Columns || cz >= g.Rows) return;

            var ccx = cx / cpc;
            var ccz = cz / cpc;
            var local = (cz - ccz * cpc) * cpc + (cx - ccx * cpc);

            var idx = IndexOf(ccx, ccz, mode);
            if (idx < 0)
            {
                chunks.Add(new DeltaChunk
                {
                    chunkX = ccx, chunkZ = ccz, mode = mode,
                    values = mode == DeltaMode.Additive
                        ? new float[cpc * cpc]                        // 0 = 无增量
                        : Filled(cpc * cpc, NoTarget),                 // NaN = 无目标
                });
                idx = chunks.Count - 1;
            }

            var c = chunks[idx];
            c.values[local] = accumulate ? c.values[local] + value : value;
            if (mode == DeltaMode.Additive && Mathf.Abs(c.values[local]) < ZeroEps)
                c.values[local] = 0f;
            chunks[idx] = c;

            PruneIfEmpty(c);
        }

        int IndexOf(int ccx, int ccz, DeltaMode mode)
        {
            for (var i = 0; i < chunks.Count; i++)
                if (chunks[i].chunkX == ccx && chunks[i].chunkZ == ccz && chunks[i].mode == mode) return i;
            return -1;
        }

        static float[] Filled(int n, float v)
        {
            var a = new float[n];
            for (var i = 0; i < n; i++) a[i] = v;
            return a;
        }

        void PruneIfEmpty(DeltaChunk c)
        {
            // values 是引用：c 是 struct 拷贝但共享同一数组，故可按引用认领本条目。
            // 判空口径按模式分岔：Additive 的"无内容"是 0，Absolute 的"无内容"是 NoTarget(NaN)。
            var additive = c.mode == DeltaMode.Additive;
            for (var n = 0; n < c.values.Length; n++)
            {
                var v = c.values[n];
                if (additive ? v != 0f : !float.IsNaN(v)) return;      // 还有内容，不剪
            }

            for (var n = 0; n < chunks.Count; n++)
                if (chunks[n].values == c.values) { chunks.RemoveAt(n); return; }
        }

        /// <summary>
        /// 重放到工作数组（01 §3.2 第 3 步）。<b>绝不得写入 BaseHeights</b>：
        /// 约束目标平面从<b>基底</b>拟合，若把人工层混进基底，下一轮约束就会把上一轮的人工结果当输入
        /// → 逐轮漂移（01 §2.4 正反馈警告）。人工层只叠加进 Heights。
        /// <para>H-45：重放顺序必须<b>确定</b>，不得随条目插入顺序变——先跑 Absolute 遍（赋值目标高程），
        /// 再跑 Additive 遍（在其上叠加相对改动）。若单遍按插入序，"先抬 3m 再压到 7m"与
        /// "先压到 7m 再抬 3m"会得到 7 或 10 的不定值，且用户看不出规律。</para>
        /// <para>幂等性依赖调用方：重放前须先 <c>CommitBaseAsWorking()</c> 复位（同 S1b）。
        /// 本方法自身是累加式的，不复位连调两次会把增量加两遍——这是"重放"的正确语义，非缺陷。</para>
        /// </summary>
        public void ApplyTo(TerrainField field)
        {
            if (field == null) return;
            var g = field.Grid;
            var cpc = CellsPerChunk(g);
            if (cpc <= 0) return;
            ApplyPass(field, g, cpc, DeltaMode.Absolute);
            ApplyPass(field, g, cpc, DeltaMode.Additive);
        }

        void ApplyPass(TerrainField field, in CityGrid g, int cpc, DeltaMode pass)
        {
            for (var k = 0; k < chunks.Count; k++)
            {
                var c = chunks[k];
                if (c.mode != pass) continue;
                var baseCx = c.chunkX * cpc;
                var baseCz = c.chunkZ * cpc;
                for (var lz = 0; lz < cpc; lz++)
                {
                    var gz = baseCz + lz;
                    if (gz < 0 || gz >= g.Rows) continue;         // 边缘 chunk 越出图幅：跳过
                    for (var lx = 0; lx < cpc; lx++)
                    {
                        var gx = baseCx + lx;
                        if (gx < 0 || gx >= g.Columns) continue;
                        var v = c.values[lz * cpc + lx];
                        var idx = gz * g.Columns + gx;
                        if (pass == DeltaMode.Additive)
                        {
                            if (v != 0f) field.Heights[idx] += v;
                        }
                        else if (!float.IsNaN(v))
                        {
                            field.Heights[idx] = v;               // Absolute：赋值，非累加
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 把<b>有人工内容</b>的格写进标记位图（非零 Additive 增量或有绝对目标）。供平整烘焙做手绘豁免。
        /// <para>口径刻意是"有任何写入痕迹"而非"改动量超过某阈值"：笔刷按衰减核写，边缘格的量
        /// 小到 1e-7 也是用户划过的位置；拿阈值判会漏掉笔刷外圈，豁免区比实际笔划小一圈，
        /// 表现就是"笔刷边缘被自动层啃掉一道"（与软化裙边同族）。</para>
        /// <para><b>必须按稀疏条目枚举，绝不能反过来逐格调 HasContent</b>：后者是
        /// O(图幅格数 × 条目数) —— 8km 档 400 万格乘上千条目会退化成分钟级；本写法成本
        /// 与"实际覆盖格数"成正比（条目数 × 每块格数），与图幅无关。</para>
        /// </summary>
        public void MarkContentInto(bool[] flags, in CityGrid g)
        {
            if (flags == null) return;
            var cpc = CellsPerChunk(g);
            if (cpc <= 0) return;
            for (var k = 0; k < chunks.Count; k++)
            {
                var c = chunks[k];
                if (c.values == null) continue;
                var additive = c.mode == DeltaMode.Additive;
                var baseCx = c.chunkX * cpc;
                var baseCz = c.chunkZ * cpc;
                for (var lz = 0; lz < cpc; lz++)
                {
                    var gz = baseCz + lz;
                    if (gz < 0 || gz >= g.Rows) continue;         // 边缘 chunk 越出图幅：跳过
                    for (var lx = 0; lx < cpc; lx++)
                    {
                        var gx = baseCx + lx;
                        if (gx < 0 || gx >= g.Columns) continue;
                        var v = c.values[lz * cpc + lx];
                        if (additive ? v != 0f : !float.IsNaN(v))
                            flags[gz * g.Columns + gx] = true;
                    }
                }
            }
        }

        /// <summary>诊断（面板「压缩 delta」与工具截断提示用，01 §9）：非零 Additive 格数。</summary>
        public int CountAdditiveCells()
        {
            var n = 0;
            foreach (var c in chunks)
                if (c.mode == DeltaMode.Additive)
                    for (var i = 0; i < c.values.Length; i++)
                        if (c.values[i] != 0f) n++;
            return n;
        }

        /// <summary>诊断：有绝对目标的格数（排除 NoTarget 哨兵）。绝对 0m 计入。</summary>
        public int CountAbsoluteCells()
        {
            var n = 0;
            foreach (var c in chunks)
                if (c.mode == DeltaMode.Absolute)
                    for (var i = 0; i < c.values.Length; i++)
                        if (!float.IsNaN(c.values[i])) n++;
            return n;
        }

        public void ClearAll() => chunks.Clear();

        // ───────────── 意图记账（撤销可见的真源，见类注释）─────────────

        [Header("意图记账（不画进画面，但删掉它撤销就失效）")]
        [SerializeField] string stableId;
        [SerializeField] IntentChangeLedger ledger = new IntentChangeLedger();

        public string StableId => stableId;
        public IntentChangeLedger Ledger => ledger;
        public long ManualVersion => ledger != null ? ledger.ManualVersion : 0L;

        private void OnEnable() => EnsureRegistered();
        private void OnDisable() => IntentRegistry.Unregister(this);

        public void EnsureRegistered()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = IntentRegistry.NewStableId("delta");
            ledger ??= new IntentChangeLedger();      // 旧资产反序列化可能为 null（新增序列化字段）
            IntentRegistry.Register(this);
        }

        /// <summary>
        /// 一次手绘提交（<b>落笔抬键调一次</b>，不是每 tick；01 §5.2）。脏 DeltaStroke 通道并记影响面。
        /// <para>零尺寸脏盒会被下游当"无脏区"丢掉 ⇒ 增量重建退化成全图，故兜底成最小盒（同 H-63 口径）。</para>
        /// </summary>
        public void CommitChange(Bounds affected)
        {
            EnsureRegistered();
            var side = affected.size.x <= 0f && affected.size.z <= 0f
                ? MinAffectedSide
                : Mathf.Max(MinAffectedSide, Mathf.Max(affected.size.x, affected.size.z));
            // 长笔划的包围盒是<b>长方形</b>：必须逐轴保留，取长轴当正方形会虚报一大片 chunk
            // （SculptBrushTests 里那条"短轴不得被拉成方形"的断言就是钉这个的）。
            var sx = affected.size.x > 0f ? Mathf.Max(MinAffectedSide, affected.size.x) : side;
            var sz = affected.size.z > 0f ? Mathf.Max(MinAffectedSide, affected.size.z) : side;
            ledger.Bump(IntentKind.DeltaStroke,
                new Bounds(affected.center, new Vector3(sx, 1f, sz)));
        }

        const float MinAffectedSide = 32f;

        /// <summary>
        /// 人工层的最大绝对改动（米），面板「手绘 ±181m」用。
        /// <para>只扫<b>已存的稀疏条目</b>（非全图），成本与雕刻面积成正比、与图幅无关。</para>
        /// </summary>
        public float MaxAbsDelta()
        {
            var max = 0f;
            foreach (var c in chunks)
            {
                if (c.mode != DeltaMode.Additive || c.values == null) continue;
                for (var i = 0; i < c.values.Length; i++)
                {
                    var v = Mathf.Abs(c.values[i]);
                    if (v > max) max = v;
                }
            }
            return max;
        }
    }
}
