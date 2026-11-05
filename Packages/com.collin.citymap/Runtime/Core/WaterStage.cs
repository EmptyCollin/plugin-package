using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// S2 水体阶段（Modules/02 改版，2026-11-05 用户裁定的「水位洪泛」定义）。
    /// <para>职责：把<b>最终生效地表</b>（噪声基底 + 手绘 delta + 已烘焙滤镜）与<b>水体种子列表</b>
    /// 一起洪泛，产出水掩码 / 缓冲带 / 到水距离场，写进上下文槽位供 S3~S6 消费。</para>
    /// <para><b>它不求解、不改地形、不写任何人工层</b>：水在内存与掩码里，纯派生。
    /// 所以本阶段可以任意次数重跑而无副作用（幂等由"先清派生位再洪泛"保证）。</para>
    /// <para>取代草案里的 D8 填洼 + 汇流阈值 + Strahler 一整套 —— 那些机制解决的是
    /// "画线如何变成合理水系"的正确性问题，而洪泛定义让那类问题不可能发生（详见 02 §3.4 改版说明）。</para>
    /// </summary>
    public sealed class WaterStage : IPipelineStage
    {
        public StageId Id => StageId.S2_River;

        /// <summary>
        /// 认领位必须与 <see cref="StageClaims"/> 一致：水面的输入是<b>地形</b>（水往哪流由地形决定）
        /// 与<b>占用</b>（禁水负约束变了 ⇒ 水界必须重算），外加自己的 <c>River</c> 位（种子/水位变化）。
        /// <para>刻意不认领 <c>Flatten</c>：平整是 S1b、在 S2 <b>之后</b>执行（01 §3.2 重放次序
        /// base → delta → <b>river</b> → flatten 的固定顺序）。若在此认领 Flatten，就会出现
        /// "水面按未经平整的地形算、之后地形又被平整改动"的<b>画面与掩码不一致</b> —— 而本阶段
        /// 拿不到重跑 S2 的机会（Runner 按固定序单向执行，不会回头）。见下方 <see cref="Run"/> 的对策。</para>
        /// </summary>
        public DirtyBits DependsOn => StageClaims.Of(StageId.S2_River);

        readonly CityProject _project;
        readonly CityTerrain _terrain;

        /// <summary>复用的 BFS 队列与访问标记（实例级，同 scratch 纪律：静态会串两个上下文）。</summary>
        int[] _queue, _visited;
        float[] _surfaceBuf;

        public WaterStage(CityProject project, CityTerrain terrain)
        {
            _project = project;
            _terrain = terrain;
        }

        /// <summary>本轮洪泛的产物（内存态，不入库；下次重跑覆盖）。</summary>
        public WaterField LastField { get; private set; }

        public StageResult Run(PipelineContext ctx, RunMode mode)
        {
            if (ctx == null) return StageResult.Fail("ctx 空");
            if (_terrain == null) return StageResult.Fail("缺 CityTerrain");

            var water = _terrain.Water;
            var grid = ctx.Grid;
            var mask = ctx.Mask;

            // 清上一轮的派生位（绝不动 NoWater —— 那是 S0 的意图位）
            WaterFlood.ClearWaterMask(mask);
            LastField = null;
            if (water == null || water.IsEmpty)
                return StageResult.Ok(0, 0);      // 无水是合法状态，不是失败

            // 组装最终生效地表：base → delta → 滤镜。<b>必须自己组装、不能直接读 field.Heights</b>：
            // 执行序是 S2 <b>在 S1b 之前</b>（01 §3.2：base → delta → river → flatten），
            // 此时本轮的滤镜还没叠上去 ⇒ 水界会<b>滞后一轮</b>
            // （表现：烘焙把洼地填高后水不退，再动一下才退 —— 用户裁定③"所见即所得"当场被破）。
            // 而 M4-o 之后 S1b 的 Run 只做 base→delta→滤镜（不再求解），
            // 所以这个组装式<b>恰好等于</b> S1b 的最终输出 ⇒ 一轮到位。
            // 写进共享 field.Heights 是安全的：S1b 进来第一件事就是 CommitBaseAsWorking() 复位。
            // ⚠️ 若将来 S1b 又引入"实时求解"（推翻 D-41），本假设失效，水界必须改挂到 S1b 之后。
            var field = TerrainBaseStage.EnsureField(ctx);
            var surface = EnsureSurface(grid.CellCount);
            field.CommitBaseAsWorking();
            var d = _terrain.Delta;
            if (d != null) d.ApplyTo(field);
            // 只在滤镜<b>开着</b>时叠加：用户关掉平整就是想看原始手绘意图，水也必须跟着原始意图走。
            var f = _terrain.FlattenFilter;
            if (_terrain.FlattenEnabled && f != null && !f.IsEmpty) f.ApplyTo(field);
            System.Array.Copy(field.Heights, surface, grid.CellCount);

            var buffer = _project != null ? _project.WaterBufferCells(grid) : 0;

            EnsureScratch(grid);       // 漏了这一行 ⇒ 首次洪泛拿到 null 队列直接 NRE（自查抓到）
            // 逐格<b>水面高度</b>：渲染必须要它。多种子各有水面高度时（天池 160m 与谷底水塘 20m 并存），
            // 只有"有水/没水"一个位就画不出正确高程 —— 天池会被画到 0m，直接埋进地形里看不见。
            var cellLevel = EnsureCellLevel(grid.CellCount);
            for (var i = 0; i < grid.CellCount; i++) cellLevel[i] = float.NegativeInfinity;
            var cells = WaterFlood.Flood(water, grid, surface, mask, _queue, _visited,
                                         out var seedsFlooded, cellLevel);
            if (cells == 0)
                return StageResult.Ok(0, 0);      // 种子都在水面高度之上 = 水退干了，合法

            var buffered = WaterFlood.ApplyBuffer(mask, grid, buffer, _queue);

            // 距离场<b>不在这里建</b>（实测与淹水面积无关，固定 170~266ms：两遍全图扫描）——
            // 它的消费者是 S5 场域与 S7 建筑临水偏好，两者目前都还没实现。
            // 每笔雕刻都为"没人读的东西"付 266ms，等于把 M4-j 换来的 4ms 手感直接毁掉。
            // 改为按需：谁要用谁调 WaterField.DistanceMeters（首次才付钱，之后复用）。
            // 面板统计要的"最大/最小水面高度"同样是 O(格数) 的遍历，放在这里<b>只在产水时</b>算一次。
            LastField = new WaterField(cells, buffered, seedsFlooded, water.Count,
                                       mask, cellLevel, grid, this);
            ctx.SetSlot(StageId.S2_River, LastField);

            return StageResult.Ok(cells, seedsFlooded);
        }

        /// <summary>逐格水面高度缓冲（复用；每轮洪泛前先填 -∞ 再由 Flood 写实际值）。</summary>
        float[] EnsureCellLevel(int cells)
        {
            if (_cellLevelBuf == null || _cellLevelBuf.Length < cells) _cellLevelBuf = new float[cells];
            return _cellLevelBuf;
        }
        float[] _cellLevelBuf;

        /// <summary>供 <see cref="WaterField"/> 按需构建距离场（缓冲复用，避免每次新分配 16MB）。</summary>
        internal float[] BuildDistanceOnDemand(ushort[] mask, in CityGrid grid)
        {
            var n = grid.CellCount;
            if (_distBuf == null || _distBuf.Length < n) _distBuf = new float[n];
            WaterFlood.BuildDistanceField(mask, grid, _distBuf);
            return _distBuf;
        }
        float[] _distBuf;

        float[] EnsureSurface(int cells)
        {
            if (_surfaceBuf == null || _surfaceBuf.Length < cells) _surfaceBuf = new float[cells];
            return _surfaceBuf;
        }

        void EnsureScratch(in CityGrid grid)
        {
            var n = grid.CellCount;
            if (_queue == null || _queue.Length < n)
            {
                _queue = new int[n];
                _visited = new int[n];
            }
        }
    }

    /// <summary>
    /// 水体派生结果（内存槽位）。
    /// <para>下游消费：04 用掩码判水界与缓冲、05 用距离场出 <c>WaterProximity</c>、
    /// 07 用临水偏好打分、03 用"是否跨水"插桥/涵洞。</para>
    /// </summary>
    public sealed class WaterField
    {
        public int WaterCells { get; }
        public int BufferCells { get; }
        /// <summary>本轮<b>真正产水</b>的种子数（其余 = 种子高于水位、水已退去）。面板须区分显示，
        /// 否则"3 个种子全退干"会被读成"有 3 片水"。</summary>
        public int ActiveSeeds { get; }
        public int TotalSeeds { get; }

        /// <summary>水面覆盖比例（0~1）。面板分层统计直接读它，不再让用户自己换算格数。</summary>
        public float WaterAreaRatio { get; }
        /// <summary>本轮<b>最低</b>水面高度（米）。无水时为 <see cref="float.NegativeInfinity"/>。</summary>
        public float MinWaterLevel { get; private set; }
        /// <summary>本轮<b>最高</b>水面高度（米）。无水时为 <see cref="float.NegativeInfinity"/>。</summary>
        public float MaxWaterLevel { get; private set; }

        readonly ushort[] _mask;
        readonly float[] _cellLevel;      // 每格水面高度（多种子各有高程的关键：不能一律画在同一平面上）
        readonly CityGrid _grid;
        readonly WaterStage _owner;
        float[] _distance;          // 惰性：首次被真要用的消费者才建（见 DistanceMeters）

        public WaterField(int waterCells, int bufferCells, int activeSeeds, int totalSeeds,
                          ushort[] mask, float[] cellLevel,
                          in CityGrid grid, WaterStage owner)
        {
            WaterCells = waterCells; BufferCells = bufferCells; ActiveSeeds = activeSeeds;
            TotalSeeds = totalSeeds;
            _mask = mask; _cellLevel = cellLevel; _grid = grid; _owner = owner;
            WaterAreaRatio = grid.CellCount > 0 ? (float)waterCells / grid.CellCount : 0f;
            MeasureLevels();
        }

        /// <summary>
        /// 统计水面高程区间（面板"水 3片·12.4%·18~160m"用）。
        /// <para>代价：一次 O(图幅格数) 扫描。只在<b>确有水</b>时构造本对象（S2 里 cells==0 早退），
        /// 所以无水场景一分钱不花；有水时与洪泛本身同量级。若将来要每帧调，
        /// 应把 min/max 移进 <see cref="WaterFlood.Flood"/> 的循环里顺手记（一次写入点），
        /// 而不是在此再加一遍扫描。</para>
        /// </summary>
        void MeasureLevels()
        {
            MinWaterLevel = float.PositiveInfinity;
            MaxWaterLevel = float.NegativeInfinity;
            if (WaterCells <= 0) { MinWaterLevel = float.NegativeInfinity; return; }
            var waterBit = (ushort)CellFlag.Water;
            for (var i = 0; i < _cellLevel.Length; i++)
            {
                if ((_mask[i] & waterBit) == 0) continue;
                var v = _cellLevel[i];
                if (float.IsNegativeInfinity(v)) continue;      // 理论上不会发生（Flood 必写），留兜底不污染统计
                if (v < MinWaterLevel) MinWaterLevel = v;
                if (v > MaxWaterLevel) MaxWaterLevel = v;
            }
            if (float.IsPositiveInfinity(MinWaterLevel)) MinWaterLevel = float.NegativeInfinity;
        }

        /// <summary>水位是否落在该格（04/03 直接查，不需要距离场）。</summary>
        public bool IsWater(int cx, int cz)
            => (_mask[cz * _grid.Columns + cx] & (ushort)CellFlag.Water) != 0;

        /// <summary>
        /// 该格的水面高度（米）。<b>非水格返回 -∞</b> —— 用"没有水面"而不是"水面=0"表达，
        /// 否则地形渲染会把无水印成 0m 平面。取 <see cref="float.NegativeInfinity"/> 也避开了
        /// "用 0 当哨兵"的老坑（0m 是完全合法的水面高度）。
        /// </summary>
        public float WaterSurfaceAt(int cx, int cz)
        {
            if (!IsWater(cx, cz)) return float.NegativeInfinity;
            return _cellLevel[cz * _grid.Columns + cx];
        }

        /// <summary>整张逐格水面高度数组（渲染按显示网格采样用）。<b>只读，不得修改</b>。</summary>
        public float[] CellLevels => _cellLevel;

        public ushort[] Mask => _mask;
        public CityGrid Grid => _grid;
        public int Columns => _grid.Columns;

        /// <summary>
        /// 到水距离场（米，切比雪夫近似）。<b>首次访问才构建</b>，之后复用同一数组。
        /// <para>惰性理由（实测）：两遍全图扫描固定 ~266ms，与淹水面积无关；而它的消费者
        /// （S5 场域、S7 建筑临水偏好）都还没实现。每笔雕刻都为没人读的东西付 266ms，
        /// 等于把 M4-j 换来的每笔 4ms 手感直接毁掉。</para>
        /// <para><b>缓存的正确性前提</b>：水掩码一变，本对象整体换新（S2 每轮 new 一个
        /// WaterField）⇒ 距离场不会跨轮脏读。<paramref name="distance"/> 交出后<b>不得修改</b>。</para>
        /// </summary>
        public float[] DistanceMeters
        {
            get
            {
                if (_distance == null) _distance = _owner.BuildDistanceOnDemand(_mask, _grid);
                return _distance;
            }
        }
    }
}
