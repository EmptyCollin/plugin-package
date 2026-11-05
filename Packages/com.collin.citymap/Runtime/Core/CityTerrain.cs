using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 场景侧地形实例（Modules/01 §2.2，AGENTS §0.1 原则2 的"Instance 半边"）。
    /// <para>判据：<see cref="TerrainProfile"/> 可被多个城市实例复用，因此
    /// <b>实例差异（人工平整区、自动试设开关、高度数据）一律序列化在本组件上，绝不写回 Profile</b>。</para>
    /// <para>M2 范围：只承载 S1a/S1b 所需的意图数据。
    /// 雕刻 delta（<c>SculptDeltaAsset</c>）与河流下切层在 M3/M4 接入，
    /// 接入时按 01 §3.2 的固定次序插在基底与约束之间。</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Collin/CityMap/City Terrain")]
    public sealed class CityTerrain : MonoBehaviour, IIntentTracked
    {
        [Header("共享配置（Model）")]
        [Tooltip("地形参数；可被多个城市实例复用")]
        [SerializeField] TerrainProfile profile;

        [Header("实例态：S1b 自动试设地块（M2 用于验证平整约束）")]
        [Tooltip("以城心为锚铺 N×N 个试设地块，M5 后由真实 parcel 供数")]
        [SerializeField] bool autoFlattenAtCenter = false;
        [SerializeField, Range(1, 12)] int autoTileCount = 3;

        [Header("实例态：人工平整区（意图层，存区域不存平面）")]
        [SerializeField] List<ManualFlattenZone> manualZones = new List<ManualFlattenZone>();

        [Header("实例态：山水雕刻人工层（01 §2.2/§2.3，实例差异不写 Model）")]
        [Tooltip("SculptDeltaAsset：笔刷增量与绝对压平目标；null = 无人工改动")]
        [SerializeField] SculptDeltaAsset delta;

        /// <summary>
        /// 水体意图层（02 改版）：<b>只存种子列表</b>，水面本身由 S2 每轮洪泛派生。
        /// <para>与 delta／滤镜同样"一物一资产"：水的意图（点了哪里、什么水位）与地形的手绘意图
        /// 是两件事，混存会让撤销粒度和版本迁移互相纠缠。</para>
        /// </summary>
        [Tooltip("WaterNetworkAsset：水体种子列表（添加水/移除水写入）。水面是派生量，不入库。")]
        [SerializeField] WaterNetworkAsset water;

        /// <summary>
        /// 平整滤镜层（M4-o，D-41）：<b>烘焙产物</b>，与 <see cref="delta"/> 物理分开存放。
        /// <para>存在理由（用户裁定的三条都靠这个分离成立）：
        /// ①「不能污染手绘的意图层数据」——滤镜与手绘若同存一个资产，"自动覆盖人工"就没有结构屏障，
        /// 一旦写错就永久毁掉用户设计且不可撤销（D-01-1 防的是同一类事故）；
        /// ②「不实时算」——本层只在按「烘焙平整」时被重写，平时 S1b 只做叠加；
        /// ③「开=加一层滤镜」——开关只在"叠 / 不叠"之间二选一。</para>
        /// </summary>
        [Tooltip("平整烘焙产物（自然化 + 地块/道路约束的合计修正）。与手绘 delta 分离，绝不被自动写入。")]
        [SerializeField] SculptFilterAsset flattenFilter;

        /// <summary>烘焙快照：三方版本 + 手绘通道序列（新鲜度判据，见 <see cref="FilterState"/>）。</summary>
        [SerializeField] long bakeProfileVersion, bakeProjectVersion, bakeDeltaSeq;
        /// <summary>显式作废位：改了「可平整对象」时由写入口置位，烘焙完成时清零。</summary>
        [SerializeField] bool bakeStale;

        [Header("阶段开关：自动平整（12 §2 三步工作流）")]
        [Tooltip("关＝只看「噪声基底 + 手绘雕刻」的原始意图地表；开＝在其上叠加已烘焙的平整滤镜层。\n"
                 + "本开关只做叠加/不叠加，不触发任何求解（求解＝点「烘焙平整」）。")]
        [SerializeField] bool flattenEnabled = true;

        /// <summary>
        /// 山水雕刻工具是否接管场景视图（H-50 根因修复）。
        /// <para>存在理由：这个开关原先只存在 Editor 面板的<b>静态字段</b>里，而静态字段随域重载
        /// 归零（改代码 / 重开场景 / 进 Play 都会触发），Unity 恢复窗口实例时不会恢复它 ⇒
        /// 面板显示仍在雕刻模式、笔刷却判定「不是雕刻模式」直接放行鼠标，
        /// 表现为「左键变框选 + 光圈消失」（用户反馈①②同源）。
        /// 真源必须落在<b>场景实例的序列化字段</b>上（§0.1 原则2：实例状态进组件，不进编辑器侧内存）。</para>
        /// <para>放 Runtime 而非 Editor：笔刷工具与面板都只是它的读写方，数据侧持有才能跨域重载存活。</para>
        /// </summary>
        [Header("工具态：场景视图接管权（H-50）")]
        [Tooltip("开＝场景视图左键由山水笔刷接管（拖动落笔）；关＝左键还给编辑器默认工具。")]
        [SerializeField] bool sculptToolActive = false;

        /// <summary>
        /// 水工具是否接管场景视图（02 改版）。真源同样是<b>场景实例的序列化字段</b>（H-50／D-34）：
        /// 放 EditorWindow 静态字段会随域重载归零 ⇒ "面板显示在水模式、点下去什么都没发生"。
        /// <para>与 <see cref="sculptToolActive"/> <b>互斥</b>（两个写入口都显式清对方）：
        /// 两者都抢左键，同时为真时行为取决于事件顺序、不可预期。互斥放在<b>写入侧</b>而不是
        /// "读取时看谁优先"——约定迟早分叉，而字段级互斥一眼可证。</para>
        /// </summary>
        [Tooltip("开＝场景视图左键由水工具接管（点一格加／移水种子）。与雕刻笔刷互斥。")]
        [SerializeField] bool waterToolActive = false;

        [SerializeField] WaterToolMode waterToolMode = WaterToolMode.Add;

        /// <summary>点击点高于海平面时是否先问"是否生成陆上水体"（用户裁定第 2 步）。</summary>
        [Tooltip("点在高处时先询问是否生成陆上水体（关掉＝直接生成并打日志）。")]
        [SerializeField] bool askBeforeLandlocked = true;

        /// <summary>陆上水体的默认水深（米）：局部水位 = 点击点高程 + 本值。</summary>
        [Tooltip("陆上水体的默认水深(m)：局部水位取点击点高程 + 此值。")]
        [SerializeField] float landlockedDepth = 4f;

        public enum WaterToolMode { Add = 0, Remove = 1 }

        public bool WaterToolActive => waterToolActive;
        public WaterToolMode WaterMode => waterToolMode;
        public bool AskBeforeLandlocked => askBeforeLandlocked;
        public float LandlockedDepth => landlockedDepth;

        [Header("身份")]
        [SerializeField] string stableId;
        [SerializeField] IntentChangeLedger ledger = new IntentChangeLedger();

        /// <summary>
        /// 生成统计（<b>故意不序列化</b>：S1b 在编辑期回写，若为序列化字段会因 ExecuteAlways
        /// 把场景标脏 ⇒ 用户什么都没改却要保存场景。呈现用，重启后归零无影响）。</summary>
        int lastConstraints, lastCells, lastRejectedConstraints;

        /// <summary>平整相关的<b>呈现用</b>提示（同上不序列化）：为空 = 无需提示。</summary>
        string flattenHint;

        public TerrainProfile Profile => profile;
        public SculptDeltaAsset Delta => delta;
        /// <summary>已烘焙的平整滤镜层（null = 从未烘焙）。</summary>
        public SculptFilterAsset FlattenFilter => flattenFilter;
        public bool AutoFlattenAtCenter => autoFlattenAtCenter;
        public bool FlattenEnabled => flattenEnabled;
        /// <summary>山水笔刷是否接管场景视图鼠标（H-50 真源，见字段注释）。纯工具态：改它不 bump 任何意图通道。</summary>
        public bool SculptToolActive => sculptToolActive;
        public int AutoTileCount => Mathf.Clamp(autoTileCount, 1, 12);
        public IReadOnlyList<ManualFlattenZone> ManualZones => manualZones;
        public string StableId => stableId;
        public IntentChangeLedger Ledger => ledger;
        public long ManualVersion => ledger?.ManualVersion ?? 0;
        public int LastConstraints => lastConstraints;
        public int LastCells => lastCells;
        public int LastRejectedConstraints => lastRejectedConstraints;
        public string FlattenHint => flattenHint;
        public long BakeProfileVersion => bakeProfileVersion;
        public long BakeProjectVersion => bakeProjectVersion;

        /// <summary>
        /// 滤镜新鲜度。判据＝<b>「烘焙时那份意图地表还在不在」</b>，不能用实例总版本号。
        /// <para>我第一版用 <c>ManualVersion</c>（所有通道的累计），被自己的用例抓出真 bug：
        /// <c>SetFlattenEnabled</c> 也 bump 总版本 ⇒ "烘焙完 → 拨一下开关 → 面板立刻提示已过期"，
        /// 自己把自己的成果判脏，用户会以为烘焙白做。开关是"叠/不叠"，不是"意图变了"。</para>
        /// <para>拆成两条正交判据：
        /// ① <b>连续量</b>：手绘通道序列（<c>DeltaStroke</c>，只有落笔才动）+ Profile 版本（噪声/求解参数）
        ///    + 项目版本（世界尺寸、城心锚点）——三者都是"内容通道"，开关与换滤镜都碰不到；
        /// ② <b>显式作废位</b> <see cref="InvalidateBake"/>：改「可平整对象」（人工平整区 / 试设地块）时
        ///    由写入口点名置位。这类改动与开关共用 <c>ParcelModel</c> 通道，靠嗅探必然误报，
        ///    所以必须显式声明 —— 宁可不自动，也不误报"已过期"。</para>
        /// <para>序列号都是 <c>[SerializeField]</c>，Ctrl+Z 回滚会连它一起还原 ⇒ 判据自洽，
        /// 无需每帧遍历百万格（面板每帧都要读这个属性）。</para>
        /// </summary>
        public BakeState FilterState
        {
            get
            {
                if (flattenFilter == null || flattenFilter.IsEmpty) return BakeState.Never;
                if (bakeStale) return BakeState.Stale;
                if (profile != null && profile.ManualVersion != bakeProfileVersion) return BakeState.Stale;
                if (_bakeProjectProbe != null && _bakeProjectProbe.ManualVersion != bakeProjectVersion)
                    return BakeState.Stale;
                if (DeltaSequence != bakeDeltaSeq) return BakeState.Stale;
                return BakeState.Fresh;
            }
        }

        /// <summary>手绘（落笔）通道的序列号 —— 内容变化的唯一连续判据。</summary>
        long DeltaSequence => ledger != null ? ledger.SequenceOf(IntentKind.DeltaStroke) : 0L;

        /// <summary>烘焙判据需要项目版本，但实例不持有项目引用（项目由面板/上下文供给）。</summary>
        [System.NonSerialized] CityProject _bakeProjectProbe;

        /// <summary>面板在读取新鲜度前把项目交给实例做版本探针（无副作用，不进序列化）。</summary>
        public void SetBakeProjectProbe(CityProject p) => _bakeProjectProbe = p;

        /// <summary>滤镜新鲜度三态。</summary>
        public enum BakeState { Never, Fresh, Stale }

        /// <summary>
        /// 改「可平整对象」时由写入口点名调用：把已烘焙滤镜标为过期。
        /// 不复用通道嗅探的理由见 <see cref="FilterState"/>。
        /// </summary>
        public void InvalidateBake() => bakeStale = true;

        /// <summary>烘焙完成后记录快照：三方版本 + 手绘通道序列，并清发作废位。</summary>
        public void RecordBake(CityProject project)
        {
            bakeProfileVersion = profile != null ? profile.ManualVersion : 0L;
            bakeProjectVersion = project != null ? project.ManualVersion : 0L;
            bakeDeltaSeq = DeltaSequence;
            bakeStale = false;
            _bakeProjectProbe = project;
        }

        /// <summary>由 S1b 回填的呈现值（不参与判定，重启即清）。</summary>
        public void ReportFlattenHint(string hint) => flattenHint = hint;

        /// <summary>
        /// 烘焙完成后由面板调用：bump 一次让画面重放新滤镜（求解已在 Bake 里做完，这里只是叠加刷新）。
        /// </summary>
        public void MarkFilterBaked() => Bump(IntentKind.ParcelModel);

        /// <summary>
        /// 换滤镜资产。走 <see cref="IntentKind.ParcelModel"/> 通道：换层必须触发一次 S1b <b>重放</b>
        /// 才能立刻看到画面 —— 是"叠加重放"不是"重新求解"。<b>不影响新鲜度</b>（不碰作废位与快照）。
        /// </summary>
        public void SetFlattenFilter(SculptFilterAsset f)
        {
            if (ReferenceEquals(f, flattenFilter)) return;
            flattenFilter = f;
            Bump(IntentKind.ParcelModel);
        }

        /// <summary>
        /// 自动平整开关（12 §2 三步工作流）。M4-o 起语义＝<b>叠加 / 不叠加已烘焙滤镜</b>的二选一。
        /// <para>仍走 ParcelModel 通道：不 bump 的话"从关到开"画面不会重放叠加，看着就像开关失效
        ///（正是上一轮用户反馈「开平整没变化」的机制之一）。<b>它不得让滤镜判过期</b>——
        /// 新鲜度不看总版本号，见 <see cref="FilterState"/>。</para>
        /// </summary>
        public void SetFlattenEnabled(bool on)
        {
            if (on == flattenEnabled) return;
            flattenEnabled = on;
            Bump(IntentKind.ParcelModel);
        }

        public void SetProfile(TerrainProfile p)
        {
            if (ReferenceEquals(p, profile)) return;
            profile = p;
            // 换 Profile 不 bump 本对象的 TerrainModel 通道（那是 Profile 自己的变化），
            // 但实例引用变了 ⇒ 必须重算地形
            Bump(IntentKind.TerrainModel);
        }

        /// <summary>
        /// 读写雕刻工具接管态（H-50）。<b>不走 Bump</b>：这是纯交互态，不影响任何派生层，
        /// 若误 bump 会让每次切工具都白跑一遍 S1b+S6（所见即所得≠每次都全重算）。
        /// </summary>
        public void SetSculptToolActive(bool on)
        {
            sculptToolActive = on;
            if (on) waterToolActive = false;      // 互斥落在<b>写入侧</b>，见 waterToolActive 注释
        }

        /// <summary>
        /// 读写水工具接管态（02 改版）。<b>不走 Bump</b>：纯交互态，误 bump 会让每次切工具
        /// 白跑一遍 S1b+S6。开启时清笔刷接管 ⇒ 两个工具不可能同时抢左键。
        /// </summary>
        public void SetWaterToolActive(bool on, WaterToolMode mode = WaterToolMode.Add)
        {
            waterToolActive = on;
            if (on) { waterToolMode = mode; sculptToolActive = false; }
        }

        public void SetWaterToolMode(WaterToolMode mode) => waterToolMode = mode;
        public void SetAskBeforeLandlocked(bool on) => askBeforeLandlocked = on;
        public void SetLandlockedDepth(float meters)
            => landlockedDepth = Mathf.Clamp(meters, 0.5f, 200f);

        /// <summary>
        /// 改「可平整对象」的写入口统一走这里：bump 地块通道 + <b>点名作废旧滤镜</b>。
        /// <para>必须显式作废、不能靠通道嗅探：开关 <c>SetFlattenEnabled</c> 也 bump 同一通道，
        /// 嗅探会把"拨开关"误判成"可平整对象变了"（M4-o 自查，被自己的用例抓出）。</para>
        /// </summary>
        void BumpFlattenSource()
        {
            Bump(IntentKind.ParcelModel);
            bakeStale = true;
        }

        public void SetAutoFlatten(bool on, int tiles)
        {
            tiles = Mathf.Clamp(tiles, 1, 12);
            if (on == autoFlattenAtCenter && tiles == autoTileCount) return;
            autoFlattenAtCenter = on;
            autoTileCount = tiles;
            // 地块级意图：走 ParcelModel 通道（S4→S1b→S6），不是地形噪声通道
            BumpFlattenSource();
        }

        /// <summary>新增一个人工平整区（意图提交点）。返回该区的 id。</summary>
        public string AddZone(Vector2 center, Vector2 fullSize, float absoluteHeight = 0f)
        {
            EnsureStableId();
            var zone = new ManualFlattenZone
            {
                stableId = IntentRegistry.NewStableId("zone"),
                center = center,
                fullSize = new Vector2(Mathf.Max(4f, fullSize.x), Mathf.Max(4f, fullSize.y)),
                absoluteHeight = Mathf.Max(0f, absoluteHeight),
            };
            manualZones.Add(zone);
            BumpFlattenSource();
            return zone.stableId;
        }

        public bool UpdateZone(string zoneId, Vector2 center, Vector2 fullSize, float extend,
                               float weight, float absoluteHeight, bool enabled)
        {
            var idx = manualZones.FindIndex(z => z.stableId == zoneId);
            if (idx < 0) return false;
            var z = manualZones[idx];
            if (z.center == center && z.fullSize == fullSize && Mathf.Approximately(z.extendMeters, extend)
                && Mathf.Approximately(z.weight, weight) && Mathf.Approximately(z.absoluteHeight, absoluteHeight)
                && z.enabled == enabled)
                return false;                                   // 无变化不 bump（防空重算风暴）

            z.center = center;
            z.fullSize = new Vector2(Mathf.Max(4f, fullSize.x), Mathf.Max(4f, fullSize.y));
            z.extendMeters = Mathf.Max(0.5f, extend);
            z.weight = Mathf.Max(0.05f, weight);
            z.absoluteHeight = Mathf.Max(0f, absoluteHeight);
            z.enabled = enabled;
            manualZones[idx] = z;
            BumpFlattenSource();
            return true;
        }

        public bool RemoveZone(string zoneId)
        {
            var removed = manualZones.RemoveAll(z => z.stableId == zoneId);
            if (removed == 0) return false;
            BumpFlattenSource();
            return true;
        }

        /// <summary>由 S1b 回填，仅用于呈现（不参与判定，见字段注释）。</summary>
        public void ReportSolveResult(int constraints, int cells, int rejected)
        {
            lastConstraints = constraints; lastCells = cells; lastRejectedConstraints = rejected;
        }

        public void EnsureStableId()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = IntentRegistry.NewStableId("terrainInst");
        }

        void Bump(IntentKind kind)
        {
            EnsureStableId();
            ledger ??= new IntentChangeLedger();
            ledger.Bump(kind, GetAffectedBounds());
        }

        /// <summary>
        /// 山水笔刷落笔提交点（工具在**落笔时调一次**，不是每 tick，01 §5.2）。
        /// <para>脏区域必须用<b>笔刷实际位置</b>而非实例 transform：
        /// 实例通常放在原点，用 `transform.position` 会让脏盒永远落在原点 ⇒
        /// 增量重建找错块，表现为"雕了别处、画面不动"（与 H-42 同族：算错影响范围）。</para>
        /// <para>影响面按用户裁定 = Flatten + Building（01 §5，DESIGN **D-31** 推翻旧「仅脏 S6」）：
        /// S1b 重算受波及地块的目标平面，S6 重排落位；<b>不含 Terrain</b> ⇒ 不重跑噪声基底（A-2）。</para>
        /// </summary>
        public void MarkDeltaChanged(Vector2 centerWorld, float brushRadius)
        {
            EnsureStableId();
            ledger ??= new IntentChangeLedger();
            var r = Mathf.Max(MinAffectedRadius, brushRadius * 2f);   // 笔刷外扩一档，覆盖过渡带波及
            ledger.Bump(IntentKind.DeltaStroke,
                new Bounds(new Vector3(centerWorld.x, 0f, centerWorld.y),
                           new Vector3(r * 2f, 1f, r * 2f)));
        }

        /// <summary>
        /// 按<b>整条笔划的实际影响包围盒</b>提交（H-63）。
        /// <para>存在理由：长笔划"点下→拖 600m→松手"时，若脏区仍以**落点**为圆心的圆，
        /// 中段那些已经写进 delta 的格就落在脏盒外 ⇒ S1b 与显示层都不重算它们
        /// ⇒ 用户看到"只有最后一段生效"（隔离实测：写入侧覆盖格号 x[238,411]，
        /// 而终点口径的脏盒只报世界 x[1499,1701]，中段全部漏报）。</para>
        /// <para>调用方传入的范围已含"笔刷中心 ± 半径"外扩（衰减核在半径外恒为 0）。
        /// <paramref name="perSideMargin"/> 是**再往外每边加的量**，由调用方按笔刷半径给：
        /// 平整开启时约束带/过渡带会波及笔刷边缘之外的格，少给就漏重建。
        /// 取 `R` 与旧圆心口径逐轴等价：旧口径半径 50.5 给到"边缘外 50.5"，这里也给 50.5
        /// ——**不得沿用 MinAffectedRadius(16)**，那会把短轴从 202m 缩到 133m，
        /// 长笔划修好了、侧向约束带却开始漏（我第一版正是这样，自查发现）。</para>
        /// <para>尺寸不做任何比例缩放——包围盒是长方形的，取长轴当正方形边长会漏掉短轴语义。</para>
        /// </summary>
        public void MarkDeltaChanged(Bounds strokeExtent, float perSideMargin = 0f)
        {
            EnsureStableId();
            ledger ??= new IntentChangeLedger();
            var grow = Mathf.Max(0f, perSideMargin) * 2f;
            // 零/负尺寸会被 PipelineContext.AddDirtyBound 当"无脏区"丢掉 ⇒ 与圆心口径同样兜底成最小盒
            var side = Mathf.Max(MinAffectedRadius * 2f, strokeExtent.size.x + grow);
            var front = Mathf.Max(MinAffectedRadius * 2f, strokeExtent.size.z + grow);
            ledger.Bump(IntentKind.DeltaStroke,
                new Bounds(strokeExtent.center, new Vector3(side, 1f, front)));
        }

        /// <summary>换 delta 资产：实例引用变了，按当前生效笔刷区域重新提交一次。</summary>
        public void SetDelta(SculptDeltaAsset d)
        {
            if (ReferenceEquals(d, delta)) return;
            delta = d;
            MarkDeltaChanged(transform.position, MinAffectedRadius);
        }

        public WaterNetworkAsset Water => water;

        /// <summary>绑水体意图层（面板「添加水」时自动建并绑，同 delta 的 H-51 处置：不静默 null）。</summary>
        public void SetWater(WaterNetworkAsset w)
        {
            if (ReferenceEquals(w, water)) return;
            water = w;
            MarkWaterChanged();
        }

        /// <summary>
        /// 水相关意图变化（种子增删改）⇒ bump <see cref="IntentKind.RiverStroke"/> 让 S2 重洪泛。
        /// <para>脏集合走 River 位：展开成 `Road|Parcel|Field|Building|Flatten`——
        /// 水界变了确实波及地块边界与临水打分，<b>绝不含 Terrain</b>（不得重跑噪声基底）。</para>
        /// </summary>
        public void MarkWaterChanged()
        {
            EnsureStableId();
            ledger ??= new IntentChangeLedger();
            ledger.Bump(IntentKind.RiverStroke,
                new Bounds(new Vector3(transform.position.x, 0f, transform.position.z),
                           Vector3.one * MinAffectedRadius));
        }

        /// <summary>
        /// 生效地表变了（烘焙完／开关滤镜）⇒ 水面必须跟着重算。
        /// <para>存在理由：水界由"地表是否低于水位"决定。烘焙把洼地填高、或关掉滤镜让洼地露出，
        /// 水面都该随之变化；不 bump 就会留下<b>水悬在半坡</b>——正是洪泛定义承诺不可能出现的东西。
        /// 显式点名而非靠 `Flatten ⇒ River` 传播规则：后者会让每次 S1b 重放都触发全图重洪泛，
        /// 而"重放"未必代表地表真的变了（同 D-39/D-41 的"显式作废优于嗅探"口径）。</para>
        /// </summary>
        public void MarkSurfaceChanged() => MarkWaterChanged();

        /// <summary>
        /// 实例变化的影响范围（保守上界）。
        /// <para>取所有启用人工区的外接半径，并钳到 ≥16m —— 全为 0 时不返回退化包围盒，
        /// 因为 <c>PipelineContext.AddDirtyRegion</c> 会忽略零尺寸盒而丢掉脏区域。
        /// S1b 施加约束时按<b>各自约束的外接盒</b>做局部重算，不依赖这里的粗粒度值（00 §4.2）。</para>
        /// </summary>
        const float MinAffectedRadius = 16f;

        Bounds GetAffectedBounds()
        {
            var radius = MinAffectedRadius;
            for (var i = 0; i < manualZones.Count; i++)
            {
                var z = manualZones[i];
                if (z == null || !z.enabled) continue;
                var r = Mathf.Max(z.fullSize.x, z.fullSize.y) * 0.5f + z.extendMeters;
                if (r > radius) radius = r;
            }
            return new Bounds(transform.position, new Vector3(radius * 2f, 1f, radius * 2f));
        }

        private void OnEnable()
        {
            EnsureStableId();
            ledger ??= new IntentChangeLedger();
            IntentRegistry.Register(this);
        }

        private void OnDisable() => IntentRegistry.Unregister(this);

        /// <summary>
        /// 自动试设地块网格布局（<b>单一真源</b>：S1b 求解、面板叠加绘制、EditMode 测试都走这里）。
        /// <para>存在理由：地块矩形位置决定"哪些格属于地块"。若测试自己另写一份半径判据来算坡度，
        /// 就会断言一个实现并不使用的区域 —— 断言看着通过、实际地块仍歪（M2 首版正是这样，半径 1.15×
        /// 覆盖到约束带外与两块之间的空隙，那里坡度仍是原始值）。</para>
        /// </summary>
        public static int EnumerateAutoTiles(CityTerrain terrain, TerrainProfile profile,
                                             CityProject project, CityGrid grid,
                                             System.Action<Vector2, Vector2> onTile)
        {
            if (terrain == null || profile == null || grid.Rows == 0 || onTile == null) return 0;
            if (!terrain.AutoFlattenAtCenter) return 0;

            var center = profile.ResolveUrbanCenter(project, grid);
            var radius = Mathf.Max(1f, profile.ResolveUrbanRadius(project, grid));
            var n = terrain.AutoTileCount;
            var tile = Mathf.Max(16f, radius * 2f / n);

            // H-31：相邻地块之间必须留街道间隙。间隙宽度按 tile 比例取，且不小于一个现实街宽
            // （03 §2.1 的 Collector/Local 红线 10~16m），否则过渡带无处展开。
            var street = Mathf.Max(10f, tile * 0.06f);
            if (street >= tile - 4f) street = Mathf.Max(0f, tile * 0.5f - 2f);
            var pitch = tile;                       // 中心间距
            var half = (tile - street) * 0.5f;      // 地块实际半尺寸
            var originX = center.x - pitch * n * 0.5f + street * 0.5f;
            var originZ = center.y - pitch * n * 0.5f + street * 0.5f;

            var emitted = 0;
            for (var tz = 0; tz < n; tz++)
            for (var tx = 0; tx < n; tx++)
            {
                var c = new Vector2(originX + (tx + 0.5f) * pitch, originZ + (tz + 0.5f) * pitch);
                // 完全在图幅外的 tile 无意义（拟合无样本）
                if (c.x < -half || c.y < -half || c.x > grid.WorldSize + half || c.y > grid.WorldSize + half)
                    continue;
                onTile(c, new Vector2(half, half));
                emitted++;
            }
            return emitted;
        }
    }
}
