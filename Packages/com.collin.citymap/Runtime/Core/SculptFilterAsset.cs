using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 平整滤镜层（M4-o，D-41）：「烘焙平整」的产物 —— 叠在意图地表之上的<b>自然化修正 delta</b>。
    /// <para>与 <see cref="SculptDeltaAsset"/> 的本质区别是<b>所有权</b>而非格式：
    /// delta = 用户手绘意图（永不自动污染，D-01-1）；本层 = 求解器产物，<b>每次烘焙整体重建</b>。
    /// 分开两个资产才能把这条纪律变成结构约束：写错代码去动 delta 时，测试与 diff 都会立刻可见；
    /// 若共用一个资产，"自动层混进人工层"就是一行都看不出、且永远撤不回的事故。</para>
    /// <para>只支持 Additive 语义（融合后 − 意图地表 的差值），没有绝对高程模式 —— 所以内部存储
    /// 独立实现而<b>不继承 delta 资产</b>：继承会把 <c>SetAbsolute/ClearAbsolute</c> 两个模式带进来，
    /// 凭空多出"绝对值语义 + 重放次序"两处必须维护却永不该存在的行为（YAGNI，且违背上面"所有权分开"的意图）。</para>
    /// <para>开关（<c>FlattenEnabled</c>）只做"叠不加"的二选一，<b>不触发任何求解</b>；
    /// 想更新内容必须点「烘焙」。这满足用户的三条裁定：滤镜不污染手绘层 / 不实时算 / 按钮驱动。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/CityMap/Sculpt Filter", fileName = "SculptFilter")]
    public sealed class SculptFilterAsset : ScriptableObject, IIntentTracked
    {
        /// <summary>存盘格式版本（同 delta 资产口径：无版本则旧档误读无声，09 H-24）。</summary>
        [SerializeField] int schemaVersion = 1;

        [Serializable]
        struct FilterChunk
        {
            public int chunkX;
            public int chunkZ;
            public float[] values;    // 行主序，长度 = cellsPerChunk²；0 = 无修正
        }

        // ⚠️ **不能有 readonly**（同 SculptDeltaAsset／WaterNetworkAsset 三处一起改）：
        // Unity 序列化管线跳过 readonly 字段，本资产在磁盘上的 YAML 里<b>只有 schemaVersion 一行</b>。
        // 后果正是用户反馈③里最难归因的那条：「烘焙完画面不动 / 开关开与关看起来一样」——
        // 滤镜内容从来没落过盘，任何一次重编译或重开场景都会把它清空。
        [SerializeField] System.Collections.Generic.List<FilterChunk> chunks
            = new System.Collections.Generic.List<FilterChunk>(16);

        // 与 delta 层同容差：亚厘米残差落格只会让资产膨胀、视觉无感（01 §9）
        const float ZeroEps = 1e-6f;

        public int ChunkCount => chunks.Count;
        public bool IsEmpty => chunks.Count == 0;
        public int SchemaVersion => schemaVersion;

        static int CellsPerChunk(in CityGrid g)
            => g.ChunkSize > 0f && g.CellSize > 0f
                ? Mathf.Max(1, Mathf.RoundToInt(g.ChunkSize / g.CellSize)) : 0;

        // 写入路径的加速位（M4-o 端到端实测发现：8km 全图烘焙 49.6 秒，根因在这里）
        //   292 万次 AddDelta，每次线性扫全部条目找块 + 整块 1024 格扫空判 ⇒ 约 4×10⁹ 次操作。
        //   烘焙的遍历顺序天然按块聚簇（融合循环按外接盒逐块走），所以"记住上一块"就把查块降为 O(1)。
        int _lastIdx = -1, _lastCcx = int.MinValue, _lastCcz = int.MinValue;
        int _filledCache = -1;                      // CountFilledCells 的惰性缓存（-1=需重算）

        /// <summary>
        /// 记录一格修正差值（烘焙循环调用；越界拒写，同 delta 纪律）。
        /// <para>与 delta 层的两处实现差异，都是为"百万格批量写"服务：
        /// ① <b>记住上一块</b>（烘焙按块聚簇遍历，避免每次全表线性查块）；
        /// ② <b>不做每写剪枝</b> —— delta 层剪枝是为了用户反复涂改后不积僵尸块，而本层的写入永远发生在
        /// <see cref="ClearAll"/> 之后、由 <see cref="Compact"/> 在烘焙结束时一次性收口（见那的方法注释）。</para>
        /// </summary>
        public void AddDelta(in CityGrid g, int cx, int cz, float delta)
        {
            var cpc = CellsPerChunk(g);
            if (cpc <= 0) return;
            if (cx < 0 || cz < 0 || cx >= g.Columns || cz >= g.Rows) return;

            var ccx = cx / cpc;
            var ccz = cz / cpc;
            var local = (cz - ccz * cpc) * cpc + (cx - ccx * cpc);

            var idx = _lastIdx;
            if (idx < 0 || idx >= chunks.Count || chunks[idx].chunkX != ccx || chunks[idx].chunkZ != ccz)
            {
                idx = -1;
                for (var i = 0; i < chunks.Count; i++)
                    if (chunks[i].chunkX == ccx && chunks[i].chunkZ == ccz) { idx = i; break; }
                if (idx < 0)
                {
                    chunks.Add(new FilterChunk { chunkX = ccx, chunkZ = ccz, values = new float[cpc * cpc] });
                    idx = chunks.Count - 1;
                }
                _lastIdx = idx; _lastCcx = ccx; _lastCcz = ccz;
            }

            var c = chunks[idx];
            c.values[local] += delta;
            if (Mathf.Abs(c.values[local]) < ZeroEps) c.values[local] = 0f;
            chunks[idx] = c;
            _filledCache = -1;                       // 落格数变了，缓存作废
        }

        /// <summary>叠加到工作数组（只进 Heights；<b>绝不进 BaseHeights</b> —— 同 D-01-1 防漂移理由）。</summary>
        public void ApplyTo(TerrainField field)
        {
            var g = field.Grid;
            var cpc = CellsPerChunk(g);
            if (cpc <= 0) return;
            for (var i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                if (c.values == null || c.values.Length != cpc * cpc) continue;   // 档变了：条目作废不猜
                var baseX = c.chunkX * cpc;
                var baseZ = c.chunkZ * cpc;
                for (var lz = 0; lz < cpc; lz++)
                {
                    var gz = baseZ + lz;
                    if (gz >= g.Rows) continue;
                    var row = gz * g.Columns;
                    var src = lz * cpc;
                    for (var lx = 0; lx < cpc; lx++)
                    {
                        var gx = baseX + lx;
                        if (gx >= g.Columns) continue;
                        var v = c.values[src + lx];
                        if (v != 0f) field.Heights[row + gx] += v;
                    }
                }
            }
        }

        /// <summary>
        /// 读一格的已烘焙修正（<b>只读取，不猜</b>：越界或无条目返回 false）。
        /// 存在理由：验收要断言"开关差异恰好等于滤镜落位"（逐格核对），
        /// 没有这个入口就只能像测试里那样"重放一张全零场再比数组"——那等于把整层复制一遍，
        /// 400 万格场景下每次断言要多花 16MB。delta 层将来若要同样能力，另加同名方法即可。
        /// </summary>
        public bool TryGetDelta(in CityGrid g, int cx, int cz, out float delta)
        {
            delta = 0f;
            var cpc = CellsPerChunk(g);
            if (cpc <= 0) return false;
            if (cx < 0 || cz < 0 || cx >= g.Columns || cz >= g.Rows) return false;

            var ccx = cx / cpc;
            var ccz = cz / cpc;
            for (var i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                if (c.chunkX != ccx || c.chunkZ != ccz || c.values == null) continue;
                var v = c.values[(cz - ccz * cpc) * cpc + (cx - ccx * cpc)];
                if (v == 0f) return false;
                delta = v;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 已落格的修正数（面板读数用，命名对齐 delta 层的 CountAdditiveCells）。
        /// <b>惰性缓存</b>：全图 292 万格时整表扫描约 12ms，而面板每帧都要读它 ⇒ 不缓存就是每秒 700 次全表扫描。
        /// </summary>
        public int CountFilledCells()
        {
            if (_filledCache >= 0) return _filledCache;
            var n = 0;
            for (var i = 0; i < chunks.Count; i++)
            {
                var vals = chunks[i].values;
                if (vals == null) continue;
                for (var j = 0; j < vals.Length; j++)
                    if (vals[j] != 0f) n++;
            }
            _filledCache = n;
            return n;
        }

        /// <summary>
        /// 整体清空 —— 烘焙的第一步就是 ClearAll：<b>本层内容以最后一次烘焙为准</b>，不增量累积。
        /// 同时复位写入缓存（否则下一轮会拿旧条目号写进新列表，越界或串块）。
        /// </summary>
        public void ClearAll()
        {
            chunks.Clear();
            _lastIdx = -1; _lastCcx = int.MinValue; _lastCcz = int.MinValue;
            _filledCache = -1;
        }

        /// <summary>
        /// 烘焙结束时收口：丢弃全零条目。<b>剪枝必须放这里、不能放每次写入</b>——
        /// M4-o 端到端实测全图烘焙 <b>49.6 秒</b>，根因就是每次写都扫整块判空（292 万次 × 1024 格）。
        /// 本层只在 ClearAll 之后被批量写入，"僵尸块"只可能来自本轮写入过程中恰好被抹平的块，
        /// 收尾扫一次（1428 块 × 1024 格 ≈ 1.5ms）即可，代价与写入次数无关。
        /// </summary>
        public void Compact()
        {
            for (var i = chunks.Count - 1; i >= 0; i--)
            {
                var vals = chunks[i].values;
                var empty = vals == null;
                if (!empty)
                    for (var n = 0; n < vals.Length; n++)
                        if (vals[n] != 0f) { empty = false; break; }
                if (empty) chunks.RemoveAt(i);
            }
            _lastIdx = -1; _lastCcx = int.MinValue; _lastCcz = int.MinValue;
            _filledCache = -1;
        }

        // ───────────── 意图记账（撤销烘焙/清空后画面才会重放）─────────────

        [Header("意图记账（不画进画面，但删掉它撤销烘焙就看不见）")]
        [SerializeField] string stableId;
        [SerializeField] IntentChangeLedger ledger = new IntentChangeLedger();

        public string StableId => stableId;
        public IntentChangeLedger Ledger => ledger;
        public long ManualVersion => ledger != null ? ledger.ManualVersion : 0L;

        private void OnEnable() => EnsureRegistered();
        private void OnDisable() => IntentRegistry.Unregister(this);

        public void EnsureRegistered()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = IntentRegistry.NewStableId("filter");
            ledger ??= new IntentChangeLedger();
            IntentRegistry.Register(this);
        }

        /// <summary>
        /// 烘焙或清空滤镜后提交一次：脏 <c>ParcelModel</c> 通道（S1b 的认领位），影响面为全图。
        /// <para>为什么走 ParcelModel 而不是新开一个位：滤镜是"叠/不叠"的内容层，S1b 本就认领该位；
        /// 而 S2 认领 <c>Flatten</c>（水界由终态地表决定），烘焙改了地表 ⇒ 水面必须跟着重算，
        /// 这条传播链已在 02 二次改版里钉死，不得在此自造新位绕开。</para>
        /// </summary>
        public void CommitChange(float worldSize)
        {
            EnsureRegistered();
            var w = Mathf.Max(64f, worldSize);
            ledger.Bump(IntentKind.ParcelModel,
                new Bounds(new Vector3(w * 0.5f, 0f, w * 0.5f), new Vector3(w, 1f, w)));
        }

        /// <summary>
        /// 滤镜修正的最大绝对高差（米）+ 改动格数，面板「滤镜 削平最大 62m」用。
        /// <para>存在理由（用户反馈③「平整滤镜简直是灾难」的根因之一）：烘焙完<b>一个数字都不报</b>，
        /// 用户无从知道它削了多少山、填了多少谷，只能靠肉眼发现"山没了"。
        /// 把改动幅度变成读数，才能让用户在烘焙完立刻判断合不合格，而不是事后归因。</para>
        /// <para>只扫稀疏条目，成本与滤镜覆盖格数成正比、与图幅无关。</para>
        /// </summary>
        public void Measure(out int cells, out float maxAbsDelta)
        {
            cells = 0; maxAbsDelta = 0f;
            foreach (var c in chunks)
            {
                if (c.values == null) continue;
                for (var i = 0; i < c.values.Length; i++)
                {
                    var v = c.values[i];
                    if (v == 0f) continue;
                    cells++;
                    var a = Mathf.Abs(v);
                    if (a > maxAbsDelta) maxAbsDelta = a;
                }
            }
        }
    }
}
