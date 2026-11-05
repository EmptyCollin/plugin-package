using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 运行时桥：编辑器面板（驱动方）与场景侧 View（消费方）之间唯一的接合点。
    /// <para>存在理由：View 在 Runtime 程序集，禁止引用 UnityEditor（§0.1 原则3），
    /// 而权威管线上下文由 Editor 面板持有。若让 View 自己找对象猜数据源，
    /// 就会出现"两份世界"（面板一份、View 一份）且必然分叉成"面板算了、画面没变"。</para>
    /// <para>协议：面板每完成一次 Commit 就 <see cref="MarkCommitted"/>（带脏区域并集，null = 全图）。
    /// View 只在 <see cref="CommitSeq"/> 前进时重建 —— 不用事件（域重载后面板与 View 的订阅时机
    /// 不一致会漏事件；序号轮询对两者都无害）。</para>
    /// <para>静态存储随域重载清空（R-07 同款纪律），显式 Reset 只是把语义钉死而非依赖巧合。</para>
    /// </summary>
    public static class CityRuntime
    {
        /// <summary>权威上下文（面板 Bind 时写入；null = 无面板驱动，View 走自建路径）。</summary>
        public static PipelineContext ActiveContext { get; private set; }

        /// <summary>提交序号：View 的唯一"要不要重建"判据。</summary>
        public static long CommitSeq { get; private set; }

        /// <summary>最近一次提交的影响范围（null = 全图）。</summary>
        public static Bounds? LastCommittedRegion { get; private set; }

        /// <summary>面板绑定/换项目：上下文整体替换，View 必须全量重建。</summary>
        public static void Activate(PipelineContext ctx)
        {
            ActiveContext = ctx;
            LastCommittedRegion = null;
            CommitSeq++;
        }

        public static void Deactivate()
        {
            ActiveContext = null;
            LastCommittedRegion = null;
            CommitSeq++;
        }

        public static void MarkCommitted(Bounds? region)
        {
            LastCommittedRegion = region;
            CommitSeq++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            ActiveContext = null;
            LastCommittedRegion = null;
            CommitSeq = 0;
        }
    }

    /// <summary>
    /// 场景侧地形显示宿主（Modules/01 §3.3 的 View 半边，AGENTS §0.1 原则1：所见即所得的第一屏）。
    /// <para>职责边界：<b>只读</b> —— 读 <see cref="TerrainField"/>、产出 chunk mesh；
    /// 不写任何意图、不参与任何生成判定（01 §6 表：显示 mesh 不可编辑、不落盘、不进撤销）。</para>
    /// <para>两条数据路径、<b>一条构建路径</b>：①面板驱动（编辑期权威）——消费 <see cref="CityRuntime"/>；
    /// ②自建（Play 模式 / 独立场景 / 无面板）——用 <see cref="projectForStandalone"/> 跑 S1a→S1b。
    /// 两条路径都经 <see cref="TerrainView.BuildChunk"/>，所以编辑态与 Play 态画面必然一致。</para>
    /// <para>派生态卫生：chunk 物体与 mesh 一律 <c>HideAndDontSave</c>，状态字段不序列化 ——
    /// 否则 <c>[ExecuteAlways]</c> 会把派生信息写进场景，用户什么都没改却要保存。</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CityTerrain))]
    [AddComponentMenu("Collin/CityMap/City Terrain View")]
    public sealed class CityTerrainView : MonoBehaviour
    {
        [Header("显示（只读派生，不参与生成）")]
        [SerializeField] bool showTerrain = true;
        [Tooltip("显示水面（02 改版：水 = 低于水位的连通域）。关掉便于单看地表雕刻。")]
        [SerializeField] bool showWater = true;
        [Tooltip("chunk 边长（米）；自动对齐到显示步长的整数倍，生效值见 EffectiveChunkSize")]
        [SerializeField, Min(16f)] float chunkSizeMeters = 128f;
        [Tooltip("UV 世界映射密度：uv = 世界坐标 / tileScale（01 §3.3，避免接缝拉伸）")]
        [SerializeField, Min(2f)] float uvTileScale = 16f;
        [Tooltip("顶点色 = 坡度带（调试着色；关掉则纯白，交给材质贴图）")]
        [SerializeField] bool slopeVertexColors = true;

        [Header("数据源")]
        [Tooltip("材质引用；留空则自动回退 URP Lit → Diffuse（本包不依赖任何 shader 包，01 §3.3）")]
        [SerializeField] Material materialOverride;
        [Tooltip("无面板驱动（Play 模式等）时，用这个项目自建上下文跑 S1a/S1b")]
        [SerializeField] CityProject projectForStandalone;

        /// <summary>诊断文本（<b>故意不序列化</b>，理由见类注释）。</summary>
        string _status = "未初始化";
        public string LastStatus => _status;
        public bool ShowTerrain => showTerrain;
        public float ChunkSizeMeters => chunkSizeMeters;
        public float UvTileScale => uvTileScale;
        public bool SlopeVertexColors => slopeVertexColors;
        public Material MaterialOverride => materialOverride;
        public CityProject StandaloneProject => projectForStandalone;
        public int ChunkObjectCount => _chunks.Count;
        public CityGrid? BoundGrid => _hasGrid ? _grid : (CityGrid?)null;

        /// <summary>
        /// 本 View 当前用于生成 mesh 的高度场（只读）。
        /// <para>存在理由：编辑器侧工具（山水笔刷的光圈、平滑取样）需要"贴在你看到的那张皮上"。
        /// 若工具自己去读 <c>CityRuntime.ActiveContext</c>，就只覆盖了面板驱动分支，
        /// 自建路径（Play / 面板未 Bind）下会拿到 null —— 光圈退化成画在 y=0 又被起伏埋掉（H-53）。
        /// 数据源真源在本 View 的 <see cref="ResolveData"/>，故由它对外只读暴露，工具不再自猜。</para>
        /// </summary>
        public TerrainField BoundField => _field;

        public float EffectiveChunkSize => _hasGrid
            ? TerrainView.EffectiveChunkSize(chunkSizeMeters, _grid.DisplayStep) : 0f;

        private sealed class ChunkHost
        {
            public GameObject Go;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Mesh Mesh;
        }

        private readonly Dictionary<Vector2Int, ChunkHost> _chunks = new Dictionary<Vector2Int, ChunkHost>(64);

        /// <summary>水面宿主单独一套容器：与水陆各自的脏区判据无关，且关水面时不能把地形一起拆了。</summary>
        private readonly Dictionary<Vector2Int, ChunkHost> _waterChunks = new Dictionary<Vector2Int, ChunkHost>(8);
        private Transform _waterRoot;
        private Material _waterMat;
        /// <summary>本轮解析到的水体派生（null = 无水面）。两条数据路径都在 ResolveData 里取。</summary>
        private WaterField _water;

        private Transform _root;
        private Material _autoMat;
        private CityTerrain _terrain;

        private CityGrid _grid;
        private bool _hasGrid;
        private TerrainField _field;
        private TerrainView.ViewParams _vp;

        // 面板驱动路径
        private long _seenSeq = -1;
        private PipelineContext _seenCtx;

        // 自建路径
        private PipelineContext _ownCtx;
        private PipelineRunner _ownRunner;
        // M4-j：按变化来源拆分的版本（项目/Profile/实例三者各自单调），
        // 用于分辨"该重跑噪声"还是"只需重放 delta+平整"。
        private long _ownProfileVer = -1, _ownTerrainVer = -1;
        /// <summary>世界参数键与水体键：<b>取代原 <c>_ownProjVer</c></b>（项目总版本会被海平面改动 bump，
        /// 用它判重建就会让"拖海平面"重跑 4.8s 噪声）。死字段必须删干净 —— 留着不但是 CS0414 警告
        /// （门禁要求零警告），更会让下一个人以为它还在起作用。</summary>
        private long _ownWorldKey = -1, _ownWaterKey = -1;
        private TerrainProfile _ownProfile;
        private CityTerrain _ownStageTerrain;
        private Bounds? _ownRegion;
        private bool _ownDataChanged;

        // 重建请求：本帧是否需要全量重建（同帧合并见 00 §4.2；增量路径由提交序号驱动，不需列表）
        private bool _wantFull;
        private ulong _configKey;

        private void OnEnable()
        {
            _terrain = GetComponent<CityTerrain>();
            _seenSeq = -1;
            _configKey = 0;
            _wantFull = true;
        }

        private void OnDisable()
        {
            DestroyAllChunks();
            DestroyAllWater();              // 水面宿主与材质同属派生态：不一起收就会留下隐藏物体
            DestroyObj(_waterMat);
            _waterMat = null;
            _ownCtx = null;
            _ownRunner = null;
            _field = null;
            _water = null;
            _hasGrid = false;
            _seenCtx = null;
        }

        // ───────────── 外部接线（面板/测试显式驱动，不依赖引擎调度时机） ─────────────

        /// <summary>立即重建一次。EditMode 测试与面板"刷新显示"按钮共用（同步、可断言）。</summary>
        public void RebuildNow()
        {
            _wantFull = true;
            DoUpdate();
        }

        /// <summary>
        /// 模拟一帧常规更新（<b>不强制</b>）。测试用它覆盖"提交序号未前进 ⇒ 不得重建"的防抖语义；
        /// 与 <see cref="RebuildNow"/> 的区别就是这条：强制入口永远看不出空转。
        /// </summary>
        public void TickOnce() => DoUpdate();

        /// <summary>强制按脏区域增量重建一次（面板在 Commit 后调用；无脏区域 = 全量）。</summary>
        public void RefreshFromRuntime()
        {
            _seenSeq = -1;                          // 让下一轮视为"序号变了"
            DoUpdate();
        }

        public void SetShowTerrain(bool on)
        {
            if (on == showTerrain) return;
            showTerrain = on;
            _wantFull = true;
        }

        /// <summary>水面显隐（面板"只看地表"用）。关掉时水面宿主整体回收，不留隐藏物体。</summary>
        public void SetShowWater(bool on)
        {
            if (on == showWater) return;
            showWater = on;
            if (!on) DestroyAllWater();
        }

        public bool ShowWater => showWater;
        /// <summary>已铺水面的 chunk 宿主数（含被清空的占位宿主，故<b>不等于</b>可见水块数；测试只用来断言"没漏回收"）。</summary>
        public int WaterChunkCount => _waterChunks.Count;
        /// <summary>本轮取到的水体派生（null = 无水或未跑出 S2）。测试与面板读数用。</summary>
        public WaterField BoundWater => _water;

        public void SetChunkSizeMeters(float m)
        {
            m = Mathf.Max(16f, m);
            if (Mathf.Approximately(m, chunkSizeMeters)) return;
            chunkSizeMeters = m;
            _wantFull = true;
        }

        public void SetStandaloneProject(CityProject p)
        {
            projectForStandalone = p;
            _ownCtx = null;
            _field = null;
            _wantFull = true;
        }

        private void LateUpdate() => DoUpdate();

        private void DoUpdate()
        {
            if (!showTerrain)
            {
                // 地形关了水面单独留着会是一张悬空的蓝膜 ⇒ 一起收掉
                if (_chunks.Count > 0) DestroyAllChunks();
                DestroyAllWater();
                _status = "地形显示已关闭";
                _wantFull = false;
                return;
            }

            if (!ResolveData())
            {
                _wantFull = false;
                return;
            }

            // 水面<b>先于</b>地形的重建判据独立刷新：两条数据路径都在 ResolveData 里取到 _water，
            // 而下面的面板/自建分支都会提前 return，把 TickWater 放它们后面就永远不会执行。
            // 且水变化<b>不该</b>触地形全量重建（289ms），地形变化也不该重铺水以外的事。
            TickWater();

            var key = ComputeConfigKey();
            var cfgChanged = key != _configKey;
            _configKey = key;

            // ① 面板驱动：权威上下文存在且本 View 已绑定它 ⇒ 只在提交序号前进时重建。
            //    这是编辑期唯一路径，也是"改参数即时反映"的实现点（§0.1 原则1）。
            var auth = CityRuntime.ActiveContext;
            if (auth != null && ReferenceEquals(_seenCtx, auth))
            {
                if (!_wantFull && !cfgChanged && CityRuntime.CommitSeq == _seenSeq) return;  // 无事发生
                _seenSeq = CityRuntime.CommitSeq;

                var region = CityRuntime.LastCommittedRegion;
                if (_wantFull || cfgChanged || region == null) RebuildAll();
                else RebuildRegion(region.Value);
                _wantFull = false;
                return;
            }

            // ② 自建路径（Play / 无面板）：没有提交序号，退化为"意图版本 + 配置指纹"驱动。
            //    Inspector 直接改显示字段不 bump 意图版本 ⇒ 靠配置指纹发现（指纹含项目/Profile 版本与显示参数）。
            //    ⚠️ 但 ComputeConfigKey() **不含 _terrain.ManualVersion**（实测：落笔后指纹逐位不变）——
            //    这不是遗漏可补：补进指纹会让 cfgChanged=true ⇒ 走 RebuildAll（289ms/笔），
            //    而雕刻只波及笔刷脏盒。所以"雕刻必须重建显示"改由下面的 _ownDataChanged 驱动 + 脏盒增量。
            //    M4-j 根因之一：旧版只有 cfgChanged 一条判据 ⇒ 雕刻算进了场、却一张 mesh 都不重建
            //    = 用户看到的「地表不同步」（数据侧实测 Heights−BaseHeights 最大差 181m，画面却没变）。
            var dataChanged = _ownDataChanged;
            _ownDataChanged = false;
            if (!_wantFull && !cfgChanged && !dataChanged) return;

            _seenSeq = CityRuntime.CommitSeq;
            if (_wantFull || cfgChanged || !_ownRegion.HasValue) RebuildAll();
            else RebuildRegion(_ownRegion.Value);        // 只重建笔刷波及的 chunk（实测 0~2ms vs 全量 289ms）
            _ownRegion = null;
            _wantFull = false;
        }

        /// <summary>解析数据源：面板上下文优先（必须已绑定到同一个上下文实例），否则自建。</summary>
        private bool ResolveData()
        {
            var profile = _terrain != null ? _terrain.Profile : null;

            var auth = CityRuntime.ActiveContext;
            if (auth != null && auth.Project != null)
            {
                if (!ReferenceEquals(_seenCtx, auth))
                {
                    _seenCtx = auth;
                    _seenSeq = CityRuntime.CommitSeq;
                    _wantFull = true;
                }
                _field = auth.GetSlot<TerrainField>(StageId.S1a_TerrainBase);
                if (_field == null)
                {
                    _hasGrid = false;
                    _status = "面板尚未跑出高度场（先执行 S1a）";
                    return false;
                }
                // 水体派生：S2 的槽位。<b>可为 null</b>（没加水就没水面），不能当错误处理。
                _water = auth.GetSlot<WaterField>(StageId.S2_River);
                _grid = auth.Grid;
                _hasGrid = true;
                _vp = MakeParams();
                return true;
            }

            _seenCtx = null;

            if (projectForStandalone == null || profile == null)
            {
                _status = projectForStandalone == null
                    ? "无权威上下文且未绑定自建项目（Play 里需指定项目）"
                    : "CityTerrain 未绑定 TerrainProfile";
                _hasGrid = false;
                return false;
            }

            // ── M4-j：自建路径按「变化来源」决定重算范围，不再无差别全量 ──
            // 实测根因：旧实现每次版本指纹变化就 `new PipelineContext` + 重注册阶段 + `MarkDirty(All)`，
            // 于是**落一笔雕刻也会重跑 S1a 噪声**（8000m@4m 实测 4860ms/笔）并重新分配
            // 上下文两张 4M 格数组（≈40MB）+ 阶段 scratch（≈50MB）⇒ 全部落在大对象堆 ⇒ 阻塞式 GC
            // = 用户看到的「每笔都要读条」。而 DESIGN **D-31 / A-2** 早已规定「雕刻不得脏 S1a」，
            // 自建路径没遵守 ⇒ 既慢、又与面板路径行为不一致（两套世界各算一份）。
            // 分层判据：
            //   项目/实例引用或版本变 → 必须重建上下文（Mask 与场数组尺寸绑死格网）⇒ 全量
            //   仅 Profile 变        → 复用上下文，只重跑 S1a（噪声参数）
            //   仅实例版本变（雕刻等）→ 复用上下文，只重放 delta + 平整（S1b），**绝不重跑噪声**
            var proj = projectForStandalone;
            // ⚠️ 判"要不要重建上下文/重跑噪声"必须用<b>世界参数键</b>，不能用 `proj.ManualVersion`。
            // 海平面／缓冲带也是 CityProject 的意图，也会 bump 项目版本 —— 但二者<b>完全不改变格网</b>。
            // 用版本号判定会让"拖一下海平面"变成"重建上下文 + 重跑 S1a 噪声"（M4-j 实测 4860ms/笔），
            // 正是我们刚在水面语义上立下的"改水位只重算水面与下游"承诺在自建路径被打破。
            var worldKey = WorldKey(proj);
            var profVer = profile.ManualVersion;
            // 手绘的账本在 delta 资产上（撤销还原的是那个资产，实例账本原地不动），
            // 所以"地表是否变了"必须<b>同时</b>看实例与 delta 资产两处的序列号。
            // 只看实例会让撤销在自建路径完全失效；只看资产会让 SetDelta/换 Profile 失效。
            var ownDelta = _terrain != null ? _terrain.Delta : null;
            var terrVer = unchecked((_terrain != null ? _terrain.ManualVersion : 0L)
                                    ^ (ownDelta != null ? ownDelta.ManualVersion * 31L : 0L));
            var water = _terrain != null ? _terrain.Water : null;
            // 水面输入 = 种子内容（坐标/高程/半径/启停）+ 全局缓冲带。
            // 02 二次改版后<b>没有"全局海平面"这一项了</b>（原写法会引用已删字段而编译不过）。
            var waterKey = (water != null ? (long)water.ComputeValueKey() : 0L)
                           ^ (long)(proj.WaterBufferMeters * 100f);

            var needCtx = _ownCtx == null
                          || !ReferenceEquals(_ownCtx.Project, proj)
                          || worldKey != _ownWorldKey
                          || (_terrain != null && !ReferenceEquals(_ownStageTerrain, _terrain));
            var needS1a = needCtx || !ReferenceEquals(_ownProfile, profile) || profVer != _ownProfileVer;
            var replayOnly = !needS1a && terrVer != _ownTerrainVer;
            var waterChanged = !needS1a && !replayOnly && waterKey != _ownWaterKey;

            if (!needS1a && !replayOnly && !waterChanged && _field != null)
            {
                _vp = MakeParams();
                return true;                                  // 无事发生（显示参数变化由 cfgChanged 负责）
            }

            if (needCtx) _ownCtx = new PipelineContext(proj);
            _ownRunner ??= new PipelineRunner();
            _ownWorldKey = worldKey;
            _ownWaterKey = waterKey;

            // 阶段对象只在 Profile/实例换了之后重注册并**长期复用**：FlattenSolveStage 的 scratch
            // 是实例级缓冲（见上方拆分说明），每笔重建就是每笔几十 MB 的 LOH 分配。
            if (needCtx || !ReferenceEquals(_ownProfile, profile) || !ReferenceEquals(_ownStageTerrain, _terrain))
            {
                _ownRunner.Unregister(StageId.S1a_TerrainBase);
                _ownRunner.Unregister(StageId.S1b_Flatten);
                _ownRunner.Unregister(StageId.S2_River);
                _ownRunner.Register(new TerrainBaseStage(profile, proj));
                // <b>自建路径同样必须注册 S2</b>：否则 Play / 面板未打开时水面永远不出现，
                // 而"未注册"是合法状态 ⇒ 不报错、只是看不见（M3 那批「只注册 S0」的同一坑）。
                _ownRunner.Register(new WaterStage(proj, _terrain));
                _ownRunner.Register(new FlattenSolveStage(profile, _terrain));
                _ownProfile = profile;
                _ownStageTerrain = _terrain;
            }

            _ownRunner.MarkDirty(needCtx ? DirtyBits.All
                                 : needS1a ? DirtyBits.Terrain
                                           : replayOnly ? DirtyBits.Flatten
                                                        : DirtyBits.River);   // 仅水体变：只重洪泛，不碰噪声/求解
            _ownRegion = null;
            if (replayOnly)
            {
                // 脏盒优先读 <b>delta 资产</b>的账本：撤销还原的就是它，实例那份不会回滚。
                var led = ownDelta != null && ownDelta.Ledger != null ? ownDelta.Ledger : _terrain?.Ledger;
                if (led != null)
                {
                    var b = led.BoundsOf(IntentKind.DeltaStroke);
                    if (b.size.x > 0f || b.size.z > 0f) _ownRegion = b;   // 只重建笔刷波及的 chunk
                }
            }
            _ownRunner.Tick(_ownCtx, RunMode.Commit);

            _ownProfileVer = profVer;
            _ownTerrainVer = terrVer;

            _field = _ownCtx.GetSlot<TerrainField>(StageId.S1a_TerrainBase);
            if (_field == null)
            {
                _hasGrid = false;
                _status = "自建路径未产出高度场（检查世界参数是否非法）";
                return false;
            }
            _water = _ownCtx.GetSlot<WaterField>(StageId.S2_River);
            _grid = _ownCtx.Grid;
            _hasGrid = true;
            _vp = MakeParams();
            // <b>只有地表真的变了</b>才置数据变更位：水面是独立宿主（TickWater 自己按 WaterCells/
            // SeaLevelUsed 判重铺），若这里无条件置 true，"拖一下海平面"就会连带地形 mesh
            // 全量重建（实测 289ms/次）—— 而地形高度<b>一点都没变</b>。
            // 这正是 M4-j 花力气消掉的那类浪费，不能从这条路上回来。
            if (!waterChanged || replayOnly || needS1a) _ownDataChanged = true;
            return true;
        }

        /// <summary>
        /// 世界参数键：只包含<b>会改变格网/掩码尺寸</b>的字段（尺寸、档位、外环、种子）。
        /// <para>存在的意义是把"改海平面"与"改世界尺寸"分开：二者都会 bump CityProject 版本，
        /// 但前者不该引发上下文重建（见 <see cref="ResolveData"/> 的 needCtx 判据）。</para>
        /// <para>外环宽度取 <c>GetGrid().BuildableInset</c>（CityProject 上没有该公开属性，
        /// 它在 CityGrid 上；GetGrid() 有缓存，不产生额外成本）。种子经 <see cref="CityRng.Mix"/>
        /// 混入而非裸 <c>(ulong)</c> 强转 —— 那是 long→ulong 的符号扩展（CS0675），
        /// 直接异或会让哈希退化。</para>
        /// </summary>
        static long WorldKey(CityProject p)
            => unchecked((long)(
                    CityRng.Mix((ulong)(int)(p.WorldSizeValue * 10f), (ulong)(int)(p.CellSize * 100f))
                  ^ CityRng.Mix((ulong)(int)(p.GetGrid().BuildableInset * 10f), p.Seed)));

        private TerrainView.ViewParams MakeParams()
            => new TerrainView.ViewParams(
                TerrainView.EffectiveChunkSize(chunkSizeMeters, _grid.DisplayStep),
                Mathf.Max(2f, uvTileScale), slopeVertexColors, TerrainView.SlopeRefDeg);

        /// <summary>配置指纹 = 显示参数 + 意图版本 + 图幅。任一项变 ⇒ 全量重建。
        /// 材质用 <c>GetEntityId().GetHashCode()</c>（Unity 6 起 GetInstanceID 废弃为 error，CS0619）。</summary>
        private ulong ComputeConfigKey()
        {
            unchecked
            {
                var h = 14695981039346656037UL;
                h = CityRng.Mix(h, (ulong)(showTerrain ? 1 : 0));
                h = CityRng.Mix(h, (ulong)(int)(chunkSizeMeters * 100f));
                h = CityRng.Mix(h, (ulong)(int)(uvTileScale * 100f));
                h = CityRng.Mix(h, (ulong)(slopeVertexColors ? 1 : 0));
                h = CityRng.Mix(h, (ulong)(materialOverride != null
                    ? materialOverride.GetEntityId().GetHashCode() : 0));
                var p = _terrain != null ? _terrain.Profile : null;
                if (p != null) h = CityRng.Mix(h, (ulong)p.ManualVersion);
                if (_hasGrid)
                {
                    h = CityRng.Mix(h, (ulong)(int)(_grid.WorldSize * 10f));
                    h = CityRng.Mix(h, (ulong)(int)(_grid.DisplayStep * 100f));
                }
                if (projectForStandalone != null) h = CityRng.Mix(h, (ulong)projectForStandalone.ManualVersion);
                return h;
            }
        }

        private void RebuildAll()
        {
            var per = TerrainView.ChunksPerSide(_grid.WorldSize, _vp.ChunkSize);

            // 图幅/尺寸缩小后多余的 chunk 宿主要先回收，否则残留旧地形
            List<Vector2Int> stale = null;
            foreach (var kv in _chunks)
                if (kv.Key.x >= per || kv.Key.y >= per) (stale ??= new List<Vector2Int>()).Add(kv.Key);
            if (stale != null)
                for (var i = 0; i < stale.Count; i++) DestroyChunk(stale[i]);

            var built = 0;
            for (var cz = 0; cz < per; cz++)
            for (var cx = 0; cx < per; cx++)
                if (RebuildChunk(new Vector2Int(cx, cz))) built++;

            _status = $"显示 {built}/{per * per} chunk · 逻辑 {_grid.Columns}²格 @{_grid.CellSize:0}m · " +
                      $"显示 {_grid.DisplayStep:0}m · chunk {_vp.ChunkSize:0}m";
        }

        private void RebuildRegion(Bounds region)
        {
            var per = TerrainView.ChunksPerSide(_grid.WorldSize, _vp.ChunkSize);
            var list = TerrainView.ChunksTouching(_grid, _vp.ChunkSize, region);
            var built = 0;
            for (var i = 0; i < list.Count; i++)
                if (RebuildChunk(list[i])) built++;
            _status = $"增量重建 {built}/{list.Count} chunk（脏盒 {region.size.x:0}×{region.size.z:0}m，" +
                      $"全图 {per * per}）";
        }

        // ─────────────────── 水面宿主（02 改版） ───────────────────
        //
        // 水面<b>独立于地形增量</b>重建：水变了不改地形几何，若把它挂进 RebuildAll 的触发链，
        // "点一下加水域"就要付 289ms 全量地形重建（M4-j 刚清掉的浪费不能从这条路回来）。
        // 代价与判据：水变化时重铺<b>全部</b>水面 chunk —— 无水的 chunk 在 BuildChunk 第一遍
        // 扫描（≈81 顶点）就早退，所以 3969 块也只几毫秒；地形的 mesh 一个都不碰。

        /// <summary>上一次铺水面时的水体身份戳（对象引用 + 关键统计），变了就重铺。</summary>
        WaterField _waterStamp;
        int _waterStampCells = -1;
        float _waterStampLevel = float.NaN;

        private void TickWater()
        {
            if (!showWater || _water == null || !_hasGrid)
            {
                if (_waterChunks.Count > 0) DestroyAllWater();
                return;
            }
            // 水掩码一变，S2 就交出一个<b>新的</b> WaterField 对象（见 WaterStage.Run），
            // 所以"引用相同 ⇒ 内容相同"成立；再带上淹水格数与<b>最高水面高程</b>兜底，
            // 防同一对象被就地改写（探针/测试会）。
            // ⚠️ 高程判据不可省：只改种子高程而面积不变（把水塘整体抬高 1m）时，
            // 引用与格数都不变 ⇒ 画面会停在旧高程的水面上，正是"改了数值不刷新"的一类。
            if (!ReferenceEquals(_waterStamp, _water)
                || _waterStampCells != _water.WaterCells
                || !Mathf.Approximately(_waterStampLevel, _water.MaxWaterLevel))
            {
                _waterStamp = _water;
                _waterStampCells = _water.WaterCells;
                _waterStampLevel = _water.MaxWaterLevel;
                RebuildWaterAll();
            }
        }

        private void RebuildWaterAll()
        {
            var per = TerrainView.ChunksPerSide(_grid.WorldSize, _vp.ChunkSize);
            var built = 0;
            for (var cz = 0; cz < per; cz++)
            for (var cx = 0; cx < per; cx++)
                if (RebuildWaterChunk(new Vector2Int(cx, cz))) built++;
            if (built != _waterChunks.Count)   // 无水块被清过，计数即实际铺水的块数
                _status += $" · 水面 {built} chunk";
        }

        private bool RebuildWaterChunk(Vector2Int c)
        {
            var geom = WaterView.BuildChunk(_water, _grid, c, _vp.ChunkSize);
            if (geom.IsEmpty)
            {
                if (_waterChunks.TryGetValue(c, out var empty))
                {
                    if (empty.Mesh != null && empty.Mesh.vertexCount > 0) empty.Mesh.Clear(false);
                    if (empty.Renderer != null) empty.Renderer.enabled = false;
                }
                return false;
            }
            if (!_waterChunks.TryGetValue(c, out var host))
            {
                EnsureWaterRoot();
                var go = new GameObject($"water_chunk_{c.x}_{c.y}")
                {
                    hideFlags = HideFlags.HideAndDontSave,      // 派生态不入库（01 §6）
                };
                go.transform.SetParent(_waterRoot, false);
                host = new ChunkHost
                {
                    Go = go,
                    Filter = go.AddComponent<MeshFilter>(),
                    Renderer = go.AddComponent<MeshRenderer>(),
                };
                _waterChunks[c] = host;
            }
            host.Mesh ??= NewMesh($"water_mesh_{c.x}_{c.y}");
            TerrainView.ApplyToMesh(geom, host.Mesh);           // 与地形同一写入口（含增量与 32bit 索引升级）
            host.Filter.sharedMesh = host.Mesh;
            host.Renderer.sharedMaterial = ResolveWaterMaterial();
            host.Renderer.enabled = true;
            host.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            host.Renderer.receiveShadows = false;               // 水面不投影：会把水底地形照成一团黑斑
            return true;
        }

        private void EnsureWaterRoot()
        {
            if (_waterRoot != null) return;
            _waterRoot = new GameObject("water_chunks (derived)")
            {
                hideFlags = HideFlags.HideAndDontSave,
            }.transform;
            _waterRoot.SetParent(transform, false);
        }

        /// <summary>
        /// 水面材质。<b>必须半透明</b>：本包自带的地形着色器是不透明的（01 §3.3 单 pass 顶点色），
        /// 拿它铺水面会<b>连地形一起盖成实心蓝</b>（我第一版用 alpha 羽化正是这个错）。
        /// <c>Sprites/Default</c> 是内置的透明 unlit 顶点色 shader，读 COLOR0 且已在透明队列，
        /// 首版够用且不引外部包。取不到时退回地形材质并<b>在状态里明说</b>，不静默画错。
        /// </summary>
        private Material ResolveWaterMaterial()
        {
            if (_waterMat != null) return _waterMat;
            var sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("UI/Default");
            if (sh == null)
            {
                _status += "（无透明 shader：水面暂用不透明材质，会盖住地形）";
                return ResolveMaterial();
            }
            _waterMat = new Material(sh)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "water_view_fallback",
            };
            return _waterMat;
        }

        private void DestroyAllWater()
        {
            foreach (var kv in _waterChunks)
            {
                DestroyObj(kv.Value.Mesh);
                DestroyObj(kv.Value.Go);
            }
            _waterChunks.Clear();
            if (_waterRoot != null) DestroyObj(_waterRoot.gameObject);
            _waterRoot = null;
            _waterStamp = null;
            _waterStampCells = -1;
            _waterStampLevel = float.NaN;
        }

        private bool RebuildChunk(Vector2Int c)
        {
            if (!_chunks.TryGetValue(c, out var host))
            {
                host = CreateChunk(c);
                _chunks[c] = host;
            }

            var geom = TerrainView.BuildChunk(_field, _grid, c, _vp);
            if (geom.IsEmpty)
            {
                if (host.Mesh != null && host.Mesh.vertexCount > 0)
                {
                    host.Mesh.Clear(false);
                    host.Renderer.enabled = false;
                }
                return false;
            }

            host.Mesh ??= NewMesh($"terrain_mesh_{c.x}_{c.y}");
            TerrainView.ApplyToMesh(geom, host.Mesh);
            host.Filter.sharedMesh = host.Mesh;
            host.Renderer.sharedMaterial = ResolveMaterial();
            host.Renderer.enabled = true;
            return true;
        }

        private ChunkHost CreateChunk(Vector2Int c)
        {
            EnsureRoot();
            var go = new GameObject($"terrain_chunk_{c.x}_{c.y}")
            {
                hideFlags = HideFlags.HideAndDontSave,          // 派生态不入库（01 §6）
            };
            go.transform.SetParent(_root, false);
            var h = new ChunkHost
            {
                Go = go,
                Filter = go.AddComponent<MeshFilter>(),
                Renderer = go.AddComponent<MeshRenderer>(),
            };
            h.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            h.Renderer.receiveShadows = true;
            return h;
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("terrain_chunks (derived)")
            {
                hideFlags = HideFlags.HideAndDontSave,
            }.transform;
            _root.SetParent(transform, false);
        }

        private Mesh NewMesh(string name)
            => new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };

        /// <summary>
        /// 材质回退链：引用 → 本包自带顶点色着色器 → URP Lit → Diffuse。
        /// <para>M3 交付缺口（用户实机反馈"地表没有材质，看起伏比较累"）：显示层已把坡度写进顶点色，
        /// 但 URP/Lit 与 Diffuse <b>都不读 COLOR 语义</b> ⇒ 色带等于白写。本包按 01 §3.3 自带
        /// 一个不依赖外部 shader 包的着色器（<c>Sample/Shaders/CityTerrainVertexColor.shader</c>），
        /// 优先级放在 URP Lit 之前。正式项目仍可用 <see cref="materialOverride"/> 换成自己的地形材质。</para>
        /// </summary>
        private Material ResolveMaterial()
        {
            if (materialOverride != null) return materialOverride;
            if (_autoMat != null) return _autoMat;

            var sh = Shader.Find(OwnShaderName);
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Diffuse");
            if (sh == null)
            {
                _status += "（无可用 shader：几何已产出但不可见）";
                return null;
            }
            _autoMat = new Material(sh)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "terrain_view_fallback",
            };
            return _autoMat;
        }

        /// <summary>本包自带地形着色器的名字（单一真源，与 shader 文件内声明一致）。</summary>
        public const string OwnShaderName = "Collin/CityMap/Terrain VertexColor";

        private void DestroyChunk(Vector2Int c)
        {
            if (!_chunks.TryGetValue(c, out var host)) return;
            _chunks.Remove(c);
            DestroyObj(host.Mesh);
            DestroyObj(host.Go);
        }

        private void DestroyAllChunks()
        {
            foreach (var kv in _chunks)
            {
                DestroyObj(kv.Value.Mesh);
                DestroyObj(kv.Value.Go);
            }
            _chunks.Clear();
            if (_root != null) DestroyObj(_root.gameObject);
            _root = null;
            DestroyObj(_autoMat);
            _autoMat = null;
        }

        /// <summary>销毁时机纪律：Play 中只能 <c>Destroy</c>（帧末执行），编辑态才可用 <c>DestroyImmediate</c>。</summary>
        private static void DestroyObj(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
