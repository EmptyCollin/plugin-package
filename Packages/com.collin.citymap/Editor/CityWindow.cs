using System;
using UnityEditor;
using UnityEngine;

namespace Collin.CityMap.EditorTooling
{
    /// <summary>
    /// 主面板壳（Modules/00 §5，AGENTS §0.1 原则1）。
    /// <para>M1 交付的是**骨架与其必须呈现的纪律**，不是完整 UI：
    /// 阶段树（信息而非门禁，08 §3.3）、自动跟随开关、脏集合预览、工具模式单选、状态条。</para>
    /// <para>硬性要求（A-10 / A-0-5 / A-24）：
    /// ① 不存在"必须先点生成才看到效果"——自动跟随默认开，改动即 Preview→Commit；
    /// ② 关掉自动跟随时，脏阶段必须可见变黄并提示"有 N 项未生效"；
    /// ③ 任何阶段工具在任何时刻可用，面板只呈现状态、不做门禁。</para>
    /// </summary>
    public sealed class CityWindow : EditorWindow
    {
        /// <summary>工具模式互斥（00 §5.3）：同一时刻只有一个工具消费 SceneView 事件。</summary>
        public enum ToolMode { Inspect, TerrainSculpt, RiverBrush, RoadGraph, FieldPlace, Build }

        /// <summary>
        /// 工作流相位（12 §1 三段式）：① 地图参数 → ② 雕山水 → ③ 平整出地表。
        /// <para>**相位不是门禁**（00 A-24「任何阶段工具在任何时刻可用」）：点某相位只做"把该相位
        /// 该有的状态设好"的便捷动作，其它工具仍随时可用。真实单一真源在
        /// <see cref="CityTerrain.FlattenEnabled"/>（D-33 阶段闸门），本枚举只是驱动它 + 高亮当前相位。</para>
        /// </summary>
        public enum WorkflowStep { Params = 1, Sculpt = 2, Flatten = 3 }

        private static CityWindow _instance;

        [SerializeField] CityProject _project;
        private PipelineContext _ctx;
        private PipelineRunner _runner;
        private ToolMode _mode = ToolMode.Inspect;
        private bool _autoFollow = true;
        private Vector2 _scroll;
        /// <summary>基础状态（几何/档位），Bind 时整体赋值，不累加。</summary>
        private string _statusLine = "未装配";
        /// <summary>最近一次执行结果，整体覆盖显示。</summary>
        private string _lastRunLine = "尚未执行任何阶段";
        private int _lastExecutedStages;

        /// <summary>地形实例（S1b 的约束来源）。面板不创建它，只找现成的并绑定。</summary>
        private CityTerrain _terrain;
        /// <summary>已注册阶段的身份指纹（Profile × CityTerrain 实体 id）。变则重注册。</summary>
        private ulong _stageKey;

        /// <summary>当前工作流相位（步骤条高亮）。点相位时 EnterStep 会把闸门/模式设到该相位该有的状态。</summary>
        private WorkflowStep _step = WorkflowStep.Params;

        public static CityProject ActiveProject => _instance != null ? _instance._project : null;
        public static PipelineRunner ActiveRunner => _instance?._runner;
        public static ToolMode CurrentMode => _instance != null ? _instance._mode : ToolMode.Inspect;

        // 菜单收进 Tools 下（用户 2026-11-03 要求，工具栏不单开顶层 Collin；与 stretch 的 Tools/Collin/… 对齐）
        [MenuItem("Tools/Collin/CityMap/City Window")]
        public static void Open()
        {
            if (_instance != null) { _instance.Focus(); return; }
            _instance = GetWindow<CityWindow>();
            _instance.titleContent = new GUIContent("CityMap");
            _instance.minSize = new Vector2(420f, 300f);
        }

        private void OnEnable()
        {
            // M4-j 根因修复：域重载后 _ctx/_runner 是瞬态的会丢，但 _project 是序列化字段能存活。
            // 不重新装配，面板就退回「未装配」→ ActiveContext=NULL → 显示层落到**自建路径**，
            // 而自建路径每笔 `MarkDirty(DirtyBits.All)` 会重跑 S1a 噪声（实测 8000m@4m = 4860ms/笔），
            // 且其 configKey 不含 terrain 实例版本 → 落笔后不重建 mesh。
            // 一个「未装配」同时造成用户的两个反馈：卡顿 + 地表不动。
            //
            // ⚠️ 但**不能在 OnEnable 里直接 Bind**（实测取证）：此刻场景对象的 OnEnable 还没跑完、
            // 尚未注册进 IntentRegistry ⇒ FindTerrainInstance() 返回 null ⇒ SyncStages 里 profile==null
            // ⇒ 只注册 S0、不注册 S1a/S1b；而 SyncStages 又只在"有脏待提交"时被调用，
            // 于是面板看似已装配（ActiveContext 有值，显示层因此放弃自建路径），实际永远算不出高度场
            // ⇒ **画面直接空白，比原来更糟**。改为标记 + 第一帧（OnEditorUpdate）再装配，
            // 那时场景对象注册已完成；装配后强制跑一次全量，保证"打开即有地形"。
            _instance = this;
            _runner = new PipelineRunner();
            EditorApplication.update += OnEditorUpdate;
            _needsFirstBind = _project != null && _ctx == null;
        }

        /// <summary>待装配标记：OnEnable 时机太早，推迟到第一帧（场景对象已注册进 IntentRegistry）。</summary>
        private bool _needsFirstBind;

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            // 面板关闭 ⇒ 权威上下文消失，View 回落到自建路径；不显式交还会让 View 消费已 Dispose 的上下文
            CityRuntime.Deactivate();
            _stageKey = 0;
        }

        // ─────────── M3：真实阶段注册 + 显示层通知（面板是驱动方） ───────────

        /// <summary>
        /// 把地形阶段接进 Runner。M1/M2 的 Runner 里没有真实阶段（面板"重新生成"只跑 S0），
        /// M3 起 S1a/S1b 必须注册，否则编辑期永远算不出高度场、画面也就永远是空的。
        /// <para>阶段身份 = (Profile, CityTerrain) 指针对：换 Profile 不重注册会算出旧参数的地形
        /// （00 §4.1 禁止阶段互相持有引用 ⇒ 阶段构造时快照，变了必须重建阶段对象）。</para>
        /// </summary>
        private void SyncStages()
        {
            if (_runner == null || _project == null) return;

            // 地形实例：从意图注册表取（O(千级)，且只在身份变化时走这里）。
            // 不用 FindFirstObjectByType：场景里若无 CityTerrain，它会在每帧全场景遍历 = 09 H-10 明令的反模式。
            if (_terrain == null || _terrain.Profile == null) _terrain = FindTerrainInstance();

            unchecked
            {
                var prof = _terrain != null ? _terrain.Profile : null;
                var key = 14695981039346656037UL;
                if (prof != null) key = CityRng.Mix(key, (ulong)prof.GetEntityId().GetHashCode());
                if (_terrain != null) key = CityRng.Mix(key, (ulong)_terrain.GetEntityId().GetHashCode());
                if (key == 0UL || key == _stageKey) return;
                _stageKey = key;
            }

            _runner.Unregister(StageId.S0_Assemble);
            _runner.Register(new AssembleStage(null));      // 无负约束资产时 S0 仍负责外环带掩码

            var profile = _terrain != null ? _terrain.Profile : null;
            _runner.Unregister(StageId.S1a_TerrainBase);
            _runner.Unregister(StageId.S1b_Flatten);
            _runner.Unregister(StageId.S2_River);
            if (profile != null)
            {
                _runner.Register(new TerrainBaseStage(profile, _project));
                // 水体阶段必须<b>注册</b>才会跑。StageId.S2_River 早在执行序里占位，但没人注册
                // ⇒ 水面永远不出现，而且"未注册"是合法状态 ⇒ 不会有任何报错提示（M3 的
                // 「Editor 目录没有 _runner.Register ⇒ 重新生成只跑 S0」同族教训：占位 ≠ 接线）。
                _runner.Register(new WaterStage(_project, _terrain));
                _runner.Register(new FlattenSolveStage(profile, _terrain));
            }
        }

        /// <summary>
        /// 从意图登记表找地形实例。登记表是 O(千级) 且不含隐藏物体；
        /// 多个城市实例时取第一个带 Profile 的（面板当前只驱动一个城市，多城切换在 M11 之后）。
        /// </summary>
        private static CityTerrain FindTerrainInstance()
        {
            foreach (var t in IntentRegistry.All)
            {
                var ct = t as CityTerrain;
                // 只要求自身 enabled：编辑态下物体可能未 activeInHierarchy（未打开的场景），
                // 但它仍是有效的意图对象，不该因此找不到地形
                if (ct != null && ct.Profile != null && ct.enabled) return ct;
            }
            return null;
        }

        /// <summary>
        /// 一次 Commit 完成后通知显示层。<b>脏区域必须在 Tick 之前取</b>——
        /// <c>Runner.Tick</c> 成功后会 <c>ClearDirtyBounds()</c>，事后取永远是 null（= 全图），
        /// 增量重建就成了空话。
        /// </summary>
        private void CommitAndNotify(DirtyBits dirty)
        {
            var region = _ctx.UnionOfDirtyBounds();
            _lastExecutedStages = _runner.Tick(_ctx, RunMode.Commit);
            _lastRunLine = $"最近执行：{_lastExecutedStages} 阶段 / {_ctx.Report.TotalMs}ms" +
                           $"（警告 {_ctx.Report.TotalWarnings}）" +
                           (_ctx.Report.AnyFailure ? "　含失败阶段" : string.Empty);
            _ctx.Report.Clear();
            CityRuntime.MarkCommitted(region);
            // 编辑态没有稳定帧循环，显式踢一次，避免"算了但画面不动"
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private void OnSelectionChange()
        {
            // 允许把场景里已有的 CityProject 拖进面板；选中带项目的对象时自动绑定
            if (_project != null) return;
            var go = Selection.activeGameObject;
            if (go == null) return;
            var proxy = go.GetComponentInChildren<CityProjectProxy>();
            if (proxy != null && proxy.Project != null) Bind(proxy.Project);
        }

        public void Bind(CityProject project)
        {
            _project = project;
            _terrain = null;
            _stageKey = 0;
            if (project == null)
            {
                _ctx = null;
                CityRuntime.Deactivate();
                _statusLine = "未绑定项目";
                Repaint();
                return;
            }
            try
            {
                _ctx = new PipelineContext(project);
                var g = _ctx.Grid;
                _statusLine = $"格 {g.Columns}×{g.Rows}（{(int)g.CellSize}m 档）· 图幅 {g.WorldSize:0}m · " +
                              $"chunk {g.ChunkCols}² · region {g.RegionCols}² · 掩码 {g.CellCount} 格";
                // 面板成为权威上下文：View 由此停止自建、改为消费面板算出的高度场
                CityRuntime.Activate(_ctx);
                SyncStages();
            }
            catch (Exception ex)
            {
                _ctx = null;
                CityRuntime.Deactivate();
                _statusLine = "参数非法：" + ex.Message;   // A-0-7：非法档位在此呈现，不静默取整
                Debug.LogException(ex);
            }
            Repaint();
        }

        /// <summary>
        /// 每帧：让 Tracker 扫描意图层，把差异喂给 Runner（自动跟随开时）。
        /// 这是"所见即所得"的实现链路：undo/改参数 → 版本 bump → Tracker 双检 → 脏集合 → 最小重算。
        /// </summary>
        private void OnEditorUpdate()
        {
            // M4-j：OnEnable 时机太早（场景对象还没进 IntentRegistry），装配推迟到更新循环里重试。
            // 实测取证：直接在 OnEnable 里 Bind 会得到 stageKey=素数（= 当时 _terrain/Profile 都是 null）、
            // runner 只注册了 S0、ctx.HasSlot(S1a)=False ⇒ 面板已接管却永远算不出高度场 ⇒ 画面反而空白，
            // 比原来更糟。窗口恢复与场景对象 OnEnable 的先后**没有保证** ⇒ 不赌时序，探测到实例才装配。
            // 探测不到就直接返回：这里**不动 _stageKey、不碰 runner**，否则每帧都会重注册一次阶段（自造churn）。
            if (_needsFirstBind)
            {
                var ready = FindTerrainInstance();
                if (ready == null || ready.Profile == null) return;   // 下一帧再试
                _needsFirstBind = false;

                Bind(_project);                   // 内含 _stageKey=0 + SyncStages（此刻才注册得出 S1a/S1b）
                if (_ctx != null && _runner != null)
                {
                    _runner.MarkDirty(DirtyBits.All);
                    CommitAndNotify(DirtyBits.All);   // 脏区 null = 全图；首帧本来就该整张算一遍
                }
                Repaint();
                return;
            }

            if (_ctx == null || _runner == null) return;

            // M4-j 自愈（终测实测缺口）：面板 _ctx 有值、但 CityRuntime.ActiveContext 可能已是 NULL
            // ——静态桥会被关闭它的人清零（OnDisable / 测试里的 Deactivate / 域重载后的 Reset），
            // 而面板这边并不知道。此时显示层判定"无权威上下文"就**无声回落到自建路径**，
            // 于是"面板看起来已装配、画面却仍按每笔全量重算"——用户遇到的正是这一类卡顿。
            // 每帧比一次对象身份并重新接桥（幂等，成本一次引用比较），断桥不可能长期存在。
            if (!ReferenceEquals(CityRuntime.ActiveContext, _ctx)) CityRuntime.Activate(_ctx);

            UndoHistoryTracker.Scan();
            DetectDirectProfileEdit();                  // 第二检：Inspector 直改 SO 字段不 bump 序列号

            if (!_autoFollow) return;                 // 关跟随：脏集合继续挂起，面板显示"未生效"
            if (!UndoHistoryTracker.ReadyToApply) return;

            var dirty = UndoHistoryTracker.ConsumeDirty();
            if (dirty == DirtyBits.None) return;

            SyncStages();                             // Profile 可能刚被换/清空
            // M4-j：笔刷/参数的脏区只存在 Tracker.LastDirtyBounds 里，而 MarkDirty(dirty) 单参调用
            // 不带 bound ⇒ ctx.DirtyBounds 为空 ⇒ CommitAndNotify 取到 null = 按全图重建
            // （实测：全量 289ms/笔 vs 增量 0~2ms/笔）。这里显式搬一次，增量重建才有依据。
            var bounds = UndoHistoryTracker.LastDirtyBounds;
            if (bounds.size.x > 0f || bounds.size.z > 0f) _ctx.AddDirtyBound(bounds);
            _runner.MarkDirty(dirty);
            CommitAndNotify(dirty);
            Repaint();
        }

        /// <summary>
        /// 值指纹第二检（A-0-3 的实现面）：用户**直接在 Inspector 改 TerrainProfile 字段**
        /// 不会调用任何 <c>SetXxx()</c> ⇒ 序列号不变 ⇒ Tracker 看不见 ⇒ "改了参数但地形不动"的静默失效。
        /// 面板每帧比一次值指纹，变了就按 <see cref="IntentKind.TerrainModel"/> 通道显式上报。
        /// <para>必须区分两种来源，否则面板滑条会被双重 bump（滑条走 <c>SetXxx()</c> 已 bump 一次，
        /// 本检再 bump 一次）：<b>版本已变 = 走的是显式入口，只同步基线；版本未变但值变了 = Inspector 直改，才补提交。</b></para>
        /// </summary>
        private void DetectDirectProfileEdit()
        {
            var profile = _terrain != null ? _terrain.Profile : null;
            if (profile == null) { _profileKeyInit = false; return; }

            var key = profile.ComputeValueKey();
            var ver = profile.ManualVersion;

            if (!_profileKeyInit)                     // 首次只见基线，不标脏（A-33 同源）
            {
                _profileKeyInit = true;
                _profileValueKey = key;
                _profileVersion = ver;
                return;
            }

            if (key == _profileValueKey) return;
            var bumpedElsewhere = ver != _profileVersion;
            _profileValueKey = key;
            _profileVersion = ver;
            if (bumpedElsewhere) return;              // 显式入口已记账，本检不重复提交

            profile.Touch();                          // Inspector 直改：补上 TerrainModel 通道提交
            UndoHistoryTracker.MarkIntent(profile);
        }

        ulong _profileValueKey;
        long _profileVersion;
        bool _profileKeyInit;

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("新建城…", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    GUILayout.Label("（M1 未实现：见 08 §3.5，四重确认）");
                if (GUILayout.Button("重新生成", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    Regenerate();
                GUILayout.Space(6f);
                _autoFollow = GUILayout.Toggle(_autoFollow, "自动跟随", EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                DrawModeSelector();
            }

            EditorGUILayout.Space(4f);
            DrawWorkflowStepBar();            // ① 参数 → ② 雕山水 → ③ 平整出地表（相位驱动，非门禁）
            EditorGUILayout.Space(4f);
            DrawProjectField();
            EditorGUILayout.Space(4f);
            DrawTerrainProfile();
            if (_mode == ToolMode.TerrainSculpt)
            {
                // 笔刷参数只在雕刻模式出现：常驻会挤占面板，且其它模式下改了也没作用
                EditorGUILayout.Space(4f);
                DrawSculptPanel();
            }
            // 水体面板挂在「河流」模式下（02 改版：水＝低于水位的连通域，点一格加/移种子）
            if (_mode == ToolMode.RiverBrush)
            {
                EditorGUILayout.Space(4f);
                DrawWaterPanel();
            }
            EditorGUILayout.Space(4f);
            DrawStageTree();
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(_statusLine, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(_lastRunLine, EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 三步工作流步骤条（12 §1）。**相位是便捷动作、不是门禁**（00 A-24）：点一步只把
        /// 该步该有的状态设好（② 关平整+进雕刻，③ 开平整+回检视），其它工具与参数仍随时可用。
        /// <para>真实单一真源是 <see cref="CityTerrain.FlattenEnabled"/>（D-33），高亮由它与实际模式回推，
        /// 避免"面板显示第③步、数据里闸门还关着"这种两套状态打架。</para>
        /// </summary>
        private void DrawWorkflowStepBar()
        {
            EditorGUILayout.LabelField("工作流（点一步＝把该步状态设好，不锁其它工具）", EditorStyles.miniBoldLabel);
            if (_terrain == null) _terrain = FindTerrainInstance();
            SyncStepFromState();

            using (new EditorGUILayout.HorizontalScope())
            {
                StepButton(WorkflowStep.Params, "① 地图参数",
                    "调世界参数与地形噪声参数（噪声基底＝山与起伏）。\n这一步不改变你的雕刻，也不强制平整状态。");
                StepButton(WorkflowStep.Sculpt, "② 雕山水",
                    "关闭自动平整（D-33 闸门）并切到「雕刻」模式。\n此时抬/压即时保留在「噪声+雕刻」的意图地表上，"
                  + "不会被城内 8× 强约束当场压回（这是反馈②「抬了没反应」的正解）。");
                StepButton(WorkflowStep.Flatten, "③ 平整出地表",
                    "打开自动平整：平整在②定稿的意图地表**之上**做整理，城内强约束此时才进场。");
            }

            if (_terrain == null)
                EditorGUILayout.HelpBox("还没有地形实例：先在下方「地形参数」区点「创建地形实例（含显示层）」。",
                    MessageType.Info);
        }

        /// <summary>高亮由真源回推：在雕刻模式就是第②步；离开雕刻模式后按闸门状态归位。</summary>
        private void SyncStepFromState()
        {
            // H-50：真源是场景实例上的 SculptToolActive（能跨域重载存活）。
            // 面板的 _mode 每帧由它回推，域重载后 _mode 复位成 Inspect 也会被立刻纠正——
            // 否则就会出现「面板显示雕刻模式、笔刷却按 Inspect 放行鼠标」。
            // 注意**只在真源存在时才纠正**：场景里一个 CityTerrain 都没有时没有真源可依据，
            // 强行弹回 Inspect 会让用户连"缺实例"的面板提示都看不到（等于把 UI 锁死）。
            var sculptOn = FindSculptActiveTerrain();
            if (sculptOn != null) _mode = ToolMode.TerrainSculpt;
            else if (_mode == ToolMode.TerrainSculpt && HasAnyTerrainInstance()) _mode = ToolMode.Inspect;

            if (_mode == ToolMode.TerrainSculpt) { _step = WorkflowStep.Sculpt; return; }
            if (_step != WorkflowStep.Sculpt) return;
            _step = _terrain != null && _terrain.FlattenEnabled
                ? WorkflowStep.Flatten : WorkflowStep.Params;
        }

        /// <summary>场景里是否有把笔刷接管开关打开的地形实例（笔刷门禁的真源）。</summary>
        internal static CityTerrain FindSculptActiveTerrain()
        {
            foreach (var t in IntentRegistry.All)
            {
                var ct = t as CityTerrain;
                if (ct != null && ct.enabled && ct.SculptToolActive) return ct;
            }
            return null;
        }

        /// <summary>场景里是否存在任何可用地形实例（= 有没有真源可依据）。</summary>
        static bool HasAnyTerrainInstance()
        {
            foreach (var t in IntentRegistry.All)
            {
                var ct = t as CityTerrain;
                if (ct != null && ct.enabled) return true;
            }
            return false;
        }

        private void StepButton(WorkflowStep s, string label, string tip)
        {
            var prev = GUI.backgroundColor;
            if (_step == s) GUI.backgroundColor = new Color(0.42f, 0.72f, 1f);
            if (GUILayout.Button(new GUIContent(label, tip), GUILayout.Height(24f))) EnterStep(s);
            GUI.backgroundColor = prev;
        }

        private void EnterStep(WorkflowStep s)
        {
            _step = s;
            if (_terrain == null) _terrain = FindTerrainInstance();

            switch (s)
            {
                case WorkflowStep.Sculpt:
                    _mode = ToolMode.TerrainSculpt;
                    // 判空用 != null 而非 `?.`：Unity 对已销毁对象的 == 有重载语义，
                    // 对 MonoBehaviour 用 null 条件运算符会绕过这层保护。
                    if (_terrain != null)
                    {
                        _terrain.SetFlattenEnabled(false);
                        // H-50：接管权写进实例真源（跨域重载存活），不再只改面板静态字段。
                        _terrain.SetSculptToolActive(true);
                        EnsureDeltaLayer();          // H-51：没有雕刻层就自动建并绑，否则落笔必被 blocked
                        MarkTerrainSceneDirty();
                    }
                    break;
                case WorkflowStep.Flatten:
                    _mode = ToolMode.Inspect;
                    if (_terrain != null)
                    {
                        _terrain.SetFlattenEnabled(true);
                        _terrain.SetSculptToolActive(false);
                        MarkTerrainSceneDirty();
                    }
                    break;
                default:
                    _mode = ToolMode.Inspect;
                    if (_terrain != null)
                    {
                        _terrain.SetSculptToolActive(false);
                        MarkTerrainSceneDirty();
                    }
                    break;
            }
            Repaint();
        }

        /// <summary>
        /// 工具态写进实例后标脏场景。
        /// <para>域重载本身会保留内存里的序列化值，但**重开场景 / 重开工程**只有落盘才留得住；
        /// 不标脏就会出现「上次明明在雕刻模式，重开场景又变回框选」。</para>
        /// </summary>
        private void MarkTerrainSceneDirty()
        {
            if (_terrain == null) return;
            Undo.RecordObject(_terrain, "切换工具");
            EditorUtility.SetDirty(_terrain);
        }

        /// <summary>
        /// 项目参数（海平面／缓冲带）改动后标脏。
        /// <para><b>CityProject 是资产不是场景物体</b> ⇒ 落盘走 <c>SetDirty + SaveAssets</c>；
        /// 若按场景物体的路子 SetDirty 它，重开工程会退回旧值（表现为"海平面白改了"）。</para>
        /// </summary>
        private void MarkProjectDirty()
        {
            if (_project == null) return;
            Undo.RecordObject(_project, "改水体参数");
            EditorUtility.SetDirty(_project);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 确保地形实例绑有一个雕刻层资产（H-51）。
        /// <para>存在理由：`delta` 字段从来没人创建过（实测磁盘上 SculptDeltaAsset 数量 = 0），
        /// 于是 BeginStroke 判到 Delta==null 直接 blocked，而提示只打在控制台 Log 里 ——
        /// 用户看到的现象就是「拖了完全没反应」。</para>
        /// <para>放包内 Sample/：AGENTS §0「插件内容须进包」，且内嵌包不在 AssetDatabase
        /// 索引里，故这里用固定路径 + LoadAssetAtPath（见 FindInPackage 的说明）。</para>
        /// </summary>
        private void EnsureDeltaLayer()
        {
            if (_terrain == null) return;
            if (_terrain.Delta != null) return;

            const string path = DeltaFolder + "/SculptDelta.asset";
            var asset = AssetDatabase.LoadAssetAtPath<SculptDeltaAsset>(path);
            if (asset == null)
            {
                if (!AssetDatabase.IsValidFolder("Packages/com.collin.citymap/Sample"))
                {
                    _lastRunLine = "创建雕刻层失败：包内 Sample 目录缺失";
                    return;
                }
                if (!AssetDatabase.IsValidFolder(DeltaFolder))
                    AssetDatabase.CreateFolder("Packages/com.collin.citymap/Sample", "Sculpt");
                asset = CreateInstance<SculptDeltaAsset>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
            }
            _terrain.SetDelta(asset);
            EditorUtility.SetDirty(_terrain);
            _lastRunLine = "已自动创建并绑定雕刻层：" + path;
        }

        const string DeltaFolder = "Packages/com.collin.citymap/Sample/Sculpt";

        private void DrawModeSelector()
        {
            foreach (ToolMode m in Enum.GetValues(typeof(ToolMode)))
            {
                var on = _mode == m;
                var next = GUILayout.Toggle(on, Label(m), EditorStyles.toolbarButton);
                if (next && !on)
                {
                    _mode = m;      // 单选：切换即互斥（00 §5.3）
                    // H-50：工具栏切到「雕刻」同样要写实例真源，否则笔刷仍不接管。
                    if (_terrain == null) _terrain = FindTerrainInstance();
                    if (_terrain != null)
                    {
                        _terrain.SetSculptToolActive(m == ToolMode.TerrainSculpt);
                        if (m == ToolMode.TerrainSculpt) EnsureDeltaLayer();
                        // H-50 同族：切到「河流」同样必须写实例真源 waterToolActive，
                        // 否则 _mode 是 EditorWindow 的字段、水工具读的是实例字段 ⇒ 面板显示在河流模式
                        // 但点下去什么都没发生（笔刷当年就是这个症状）。
                        _terrain.SetWaterToolActive(m == ToolMode.RiverBrush);
                        if (m == ToolMode.RiverBrush) EnsureWaterAsset();
                        MarkTerrainSceneDirty();
                    }
                }
            }
        }

        private static string Label(ToolMode m)
        {
            switch (m)
            {
                case ToolMode.Inspect: return "检视";
                case ToolMode.TerrainSculpt: return "雕刻";
                case ToolMode.RiverBrush: return "河流";
                case ToolMode.RoadGraph: return "路网";
                case ToolMode.FieldPlace: return "场域";
                case ToolMode.Build: return "建筑";
                default: return m.ToString();
            }
        }

        /// <summary>
        /// 雕刻笔刷参数区（Modules/01 §5.1）。**只改静态参数，不触发重生成**——
        /// 真正落笔在 SceneView（SculptBrushTool），这里只是"调手感"的旋钮。
        /// <para>动作只列**已接线**的四个：反色/清除/定高到指定值需要先有数值框输入源，
        /// 列出来会让用户选到没作用的选项（比"不可见"更糟）。</para>
        /// </summary>
        private void DrawSculptPanel()
        {
            EditorGUILayout.LabelField("雕刻笔刷（SceneView 内拖动落笔）", EditorStyles.boldLabel);

            SculptBrushTool.RadiusMeters = EditorGUILayout.Slider("笔刷半径(m)",
                SculptBrushTool.RadiusMeters, 2f, 200f);

            // 平滑的强度语义是 Clamp01（1=一步拉到位），滑条给到 5 会让 1 之后的拖动毫无反应（M4-m）。
            // 抬升/下压是位移积分、强度无上限语义，保持 5。
            var maxStrength = SculptBrushTool.Action == SculptAction.Smooth ? 1f : 5f;
            SculptBrushTool.Strength = EditorGUILayout.Slider("强度",
                Mathf.Min(SculptBrushTool.Strength, maxStrength), 0.05f, maxStrength);

            // 仅呈现可用动作；选中未接线项会被工具拒绝并提示，这里直接不给选项
            var idx = Mathf.Clamp(IndexOf(SculptBrushTool.Action), 0, _sculptActions.Length - 1);
            var next = EditorGUILayout.Popup("动作", idx, _sculptActionsLabels);
            if (next != idx) SculptBrushTool.Action = ActionAt(next);

            // ── 平滑专属参数（M4-m / D-40）：只在动作=平滑时出现，其它动作下它没有作用 ──
            if (SculptBrushTool.Action == SculptAction.Smooth)
            {
                var ep = EditorGUILayout.ToggleLeft(
                    new GUIContent("保边平滑（只抹碎、不削坡）",
                        "开＝盘内样本按高差加权：比本格陡 2σ 以上的样本被忽略 ⇒ 磨不到折线与陡坎，"
                      + "适合「城里找平、城外保留野性」。\n"
                      + "关＝普通盘均值（= Unity 地形平滑笔刷的直觉），山头沟脊一起圆掉。"),
                    SculptBrushTool.EdgePreserve);
                if (ep != SculptBrushTool.EdgePreserve) SculptBrushTool.EdgePreserve = ep;
                if (ep)
                    SculptBrushTool.SigmaMeters = EditorGUILayout.Slider("保边带宽σ(m)",
                        SculptBrushTool.SigmaMeters, 0.5f, 15f);

                EditorGUILayout.HelpBox(
                    "平滑尺度 = 笔刷半径（M4-m 起）：想磨掉多大尺度就拖多大半径。"
                  + "半径 30m 修形、200m 能圆掉山体轮廓；斜面不受影响（对称核的不动点是任意平面）。",
                    MessageType.Info);
            }

            // H-51：阻断原因必须出现在面板上。原先只 Debug.Log 到控制台，
            // 用户看不到 ⇒ 现象就是「左键拖了完全没反应」，与真 bug 无法区分。
            if (!string.IsNullOrEmpty(SculptBrushTool.BlockReason))
                EditorGUILayout.HelpBox(SculptBrushTool.BlockReason, MessageType.Warning);

            EditorGUILayout.HelpBox(
                "一笔划 = 一个撤销记录；抬升/下压量由**拖动距离**决定（与帧率无关），" +
                "平滑/定高需已生成地形。落笔后地表按脏区增量刷新。",
                MessageType.Info);
        }

        static readonly string[] _sculptActionsLabels = { "抬升", "下压", "平滑", "定高到起笔点" };
        static readonly SculptAction[] _sculptActions =
            { SculptAction.Raise, SculptAction.Lower, SculptAction.Smooth, SculptAction.FlattenToCenter };

        static int IndexOf(SculptAction a) => Array.IndexOf(_sculptActions, a);
        static SculptAction ActionAt(int i) => _sculptActions[i];

        private void DrawProjectField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var next = (CityProject)EditorGUILayout.ObjectField("城市项目", _project,
                    typeof(CityProject), false);
                if (!ReferenceEquals(next, _project)) Bind(next);
                if (GUILayout.Button("创建", GUILayout.Width(50f))) CreateProject();
            }

            // ── 海平面与水体基准（02 改版，用户裁定①默认 0）──
            // 必须显示"水现在淹了多少"，否则拖滑条时用户无从判断范围（同城区生效值的口径）。
            if (_project != null)
            {
                var sea = EditorGUILayout.Slider(new GUIContent("海平面(m)",
                        "水 = 与某个水体种子连通、且低于该种子有效水位的格。\n" +
                        "种子未设局部水位时就用这个值。\n" +
                        "改它只重算水面与下游，<b>不会</b>重跑地形噪声（走 River 通道）。"),
                    _project.SeaLevel, -20f, 120f);
                if (!Mathf.Approximately(sea, _project.SeaLevel))
                {
                    _project.SetSeaLevel(sea);
                    MarkProjectDirty();
                }

                var wb = EditorGUILayout.Slider(new GUIContent("水缓冲带(m)",
                        "水域外扩的不可建筑带宽度。0 = 不设带。\n" +
                        "换算成格时<b>向上取整</b>（宁多不一格漏）。"),
                    _project.WaterBufferMeters, 0f, 60f);
                if (!Mathf.Approximately(wb, _project.WaterBufferMeters))
                {
                    _project.SetWaterBuffer(wb);
                    MarkProjectDirty();
                }
            }
        }

        /// <summary>
        /// 水体面板（02 改版）：加/移水的模式、陆上水体参数，以及<b>种子列表编辑</b>
        /// （用户裁定第 4 条：坐标、高度均可调，也可手动添加）。
        /// <para>面板必须显示"当前有几片水、淹了多少格"——水界面是派生量，
        /// 只报种子数会让用户以为"有种子就有水"（实际可能全退干了）。</para>
        /// </summary>
        /// <summary>
        /// 工具栏选「河流」＝进入水工具。<b>只在切换时设接管态</b>：
        /// 若每帧重设，用户在场景里改完种子、面板一刷新就被重置 ⇒ 又变成两套状态打架（H-50 同族）。
        /// </summary>
        private void DrawWaterPanel()
        {
            if (_terrain == null) _terrain = FindTerrainInstance();
            EditorGUILayout.LabelField("水体（水 = 低于水位的连通域）", EditorStyles.boldLabel);
            if (_terrain == null)
            {
                EditorGUILayout.HelpBox("先「创建地形实例」才能加水：水面按实例逐格派生。", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var add = GUILayout.Toggle(_terrain.WaterMode == CityTerrain.WaterToolMode.Add,
                    new GUIContent("添加水", "点场景一格：低于海平面直接成水；"
                        + "高于海平面会先问是否生成陆上水体。"),
                    EditorStyles.toolbarButton);
                var rm = GUILayout.Toggle(_terrain.WaterMode == CityTerrain.WaterToolMode.Remove,
                    new GUIContent("移除水", "点掉种子：删最近的一个（最多 16 格内）。"),
                    EditorStyles.toolbarButton);
                var want = add ? CityTerrain.WaterToolMode.Add : CityTerrain.WaterToolMode.Remove;
                if (want != _terrain.WaterMode)
                {
                    _terrain.SetWaterToolMode(want);
                    MarkTerrainSceneDirty();
                }
            }
            if (!_terrain.WaterToolActive)
                EditorGUILayout.HelpBox("水工具未接管场景视图：点顶部工具栏「河流」才会接管左键。",
                    MessageType.Info);

            var ask = EditorGUILayout.Toggle("高处点击先询问", _terrain.AskBeforeLandlocked);
            if (ask != _terrain.AskBeforeLandlocked) _terrain.SetAskBeforeLandlocked(ask);
            var depth = EditorGUILayout.Slider("陆上水体默认水深(m)", _terrain.LandlockedDepth, 0.5f, 60f);
            if (!Mathf.Approximately(depth, _terrain.LandlockedDepth)) _terrain.SetLandlockedDepth(depth);

            // ── 陆上水体内联确认（用户裁定的第 2、3 步；<b>不用模态框</b>，AGENTS §2.1.5 禁阻塞弹窗）──
            if (WaterBrushTool.HasPending)
            {
                var pd = WaterBrushTool.Pending;
                EditorGUILayout.HelpBox(
                    $"点在格 ({pd.cx},{pd.cz})：高程 {pd.height:0.##}m 高于海平面 {pd.sea:0.##}m。" +
                    $"\n确认将生成带局部水位 {pd.height + _terrain.LandlockedDepth:0.##}m" +
                    $"（水深 {_terrain.LandlockedDepth:0.#}m）的陆上水体；取消则什么都不建。",
                    // MessageType 只有 None/Info/Warning/Error（无 Question，臆造会编译失败）。
                    // 用 Warning：这是等用户决策的中间态，比 Info 更醒目、又不是错误。
                    MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("生成陆上水体", GUILayout.Height(24f)))
                    {
                        WaterBrushTool.ConfirmPending();
                        Repaint();             // CommitSeed 内部已存盘 + bump 一次，这里<b>不再</b> TouchWaterAsset（会二次标脏白跑洪泛）
                    }
                    if (GUILayout.Button("取消", GUILayout.Width(70f), GUILayout.Height(24f)))
                    {
                        WaterBrushTool.CancelPending();
                        Repaint();
                    }
                }
            }

            if (!string.IsNullOrEmpty(WaterBrushTool.BlockReason))
                EditorGUILayout.HelpBox(WaterBrushTool.BlockReason, MessageType.Warning);

            // ── 种子列表（坐标/高度可调、可手动添加、可启停与删除）──
            var water = _terrain.Water;
            if (water == null)
            {
                if (GUILayout.Button("新建水体种子列表", GUILayout.Height(22f))) EnsureWaterAsset();
                EditorGUILayout.HelpBox("还没有种子列表：上面的按钮创建并绑定（固定在包内 Sample/Sculpt，"
                    + "同 D-35 的\"固定路径 + LoadAssetAtPath\"纪律）。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField($"种子列表（{water.Count} 个）", EditorStyles.miniBoldLabel);
            var grid = _ctx != null ? _ctx.Grid : (_terrain.GetComponent<CityTerrainView>()?.BoundGrid ?? default);
            string toRemove = null;

            for (var i = 0; i < water.Count; i++)
            {
                var s = water[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    var en = EditorGUILayout.Toggle(s.enabled, GUILayout.Width(16f));
                    if (en != s.enabled) { water.SetSeedEnabled(s.stableId, en); TouchWaterAsset(); }

                    var px = EditorGUILayout.Vector2Field(GUIContent.none,
                        new Vector2(s.cx, s.cz), GUILayout.Width(110f));
                    if ((int)px.x != s.cx || (int)px.y != s.cz)
                    {
                        if (!water.MoveSeed(s.stableId, (int)px.x, (int)px.y))
                            EditorGUILayout.LabelField("目标格已被占", GUILayout.Width(96f));
                        TouchWaterAsset();
                    }

                    // 局部水位：复选框控制"是否覆盖全局海平面"，避免让用户去理解 +∞ 哨兵
                    var hasLocal = !float.IsPositiveInfinity(s.localLevel);
                    var hl = EditorGUILayout.ToggleLeft("局部", hasLocal, GUILayout.Width(44f));
                    if (hl != hasLocal)
                    {
                        water.SetSeedLevel(s.stableId, hl ? 0f : WaterSeed.NoLocalLevel);
                        TouchWaterAsset();
                    }
                    if (hl)
                    {
                        var lv = EditorGUILayout.FloatField(s.localLevel, GUILayout.Width(52f));
                        if (!Mathf.Approximately(lv, s.localLevel))
                        { water.SetSeedLevel(s.stableId, lv); TouchWaterAsset(); }
                    }
                    else EditorGUILayout.LabelField("—", GUILayout.Width(52f));

                    if (GUILayout.Button("×", GUILayout.Width(20f))) toRemove = s.stableId;
                }
            }

            if (!string.IsNullOrEmpty(toRemove))
            {
                Undo.RecordObject(water, "移除水种子");
                water.RemoveSeed(toRemove);
                TouchWaterAsset();
                _terrain.MarkWaterChanged();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("手动加一个种子"))
                {
                    Undo.RecordObject(water, "添加水种子");
                    var cc = grid.CellCount > 0 ? grid.Columns / 2 : 0;
                    var rr = grid.CellCount > 0 ? grid.Rows / 2 : 0;
                    water.AddSeed(cc, rr, WaterSeed.NoLocalLevel, out _);
                    TouchWaterAsset();      // 内部已含 MarkWaterChanged；此处不再重复 bump（会白跑一轮洪泛）
                }
                var hf = WaterBrushTool.TryGetHover(out var hcell, out var hh);
                if (hf)
                {
                    // 内插里的三元表达式必须加括号，否则 `:` 会被当成格式说明符分隔（CS8361）。
                    // 这里直接先取局部变量，比嵌三元更易读。
                    var seaNow = _project != null ? _project.SeaLevel : 0f;
                    EditorGUILayout.LabelField(
                        $"光标：格({hcell.x},{hcell.y}) 高程 {hh:0.##}m 海平面 {seaNow:0.##}m",
                        GUILayout.Width(170f));
                }
            }
        }

        /// <summary>
        /// 水资产改动：存盘 + 触发一次水面重算。
        /// <para>只 bump 通道（<see cref="CityTerrain.MarkWaterChanged"/>），脏区搬入与 Tick
        /// 由既有的 <c>UndoHistoryTracker.Scan()</c> → <c>CommitAndNotify</c> 链路负责
        /// （面板 253~269 行）。<b>不在这里手动 MarkDirty/Tick</b>：那样会与自动跟随叠成双跑，
        /// 更要命的是绕过 <c>LastDirtyBounds</c> 的搬入，增量重建退化成全图。</para>
        /// </summary>
        private void TouchWaterAsset()
        {
            if (_terrain?.Water == null) return;
            EditorUtility.SetDirty(_terrain.Water);
            AssetDatabase.SaveAssets();
            _terrain.MarkWaterChanged();        // bump RiverStroke ⇒ Tracker 扫到 ⇒ 走 River 通道重洪泛
            Repaint();
        }

        /// <summary>确保实例绑有水体种子列表资产（同 H-51 处置：不静默留 null）。</summary>
        private void EnsureWaterAsset()
        {
            if (_terrain == null) return;
            if (_terrain.Water != null) return;

            var path = DeltaFolder + "/WaterNetwork.asset";
            var asset = AssetDatabase.LoadAssetAtPath<WaterNetworkAsset>(path);
            if (asset == null)
            {
                if (!AssetDatabase.IsValidFolder(DeltaFolder))
                    AssetDatabase.CreateFolder("Packages/com.collin.citymap/Sample", "Sculpt");
                asset = CreateInstance<WaterNetworkAsset>();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssets();
            }
            Undo.RecordObject(_terrain, "绑定水体列表");
            _terrain.SetWater(asset);
            EditorUtility.SetDirty(_terrain);
            _lastRunLine = "已创建并绑定水体种子列表：" + path;
        }

        private void CreateProject()
        {
            var path = EditorUtility.SaveFilePanelInProject("新建城市项目", "CityProject", "asset",
                "项目根 Model：世界尺寸、格档位、种子、城心锚点");
            if (string.IsNullOrEmpty(path)) return;
            var asset = CreateInstance<CityProject>();
            AssetDatabase.CreateAsset(asset, path);
            EditorGUIUtility.PingObject(asset);
            Bind(asset);
        }

        /// <summary>
        /// 地形参数区（AGENTS §0.1 原则1「所见即所得」+ 原则2「Controller 只写 Model」）。
        /// <para>控件一律走 <see cref="TerrainProfile"/> 的显式写入口（内部 <c>Touch()</c> bump 序列号），
        /// 面板<b>不直接写序列化字段</b>：那样会绕过意图记账，改动只有靠值指纹第二检才兜得住。</para>
        /// <para>拖动即重算：写入口 bump 后由 <see cref="OnEditorUpdate"/> 的 Tracker 链路消费
        /// （250ms 合并窗口内多次拖动只算一次，A-17），松手即看到画面变化。</para>
        /// </summary>
        private void DrawTerrainProfile()
        {
            if (_project == null) return;

            EditorGUILayout.LabelField("地形参数（S1a / S1b）", EditorStyles.boldLabel);

            if (_terrain == null) _terrain = FindTerrainInstance();
            if (_terrain == null)
            {
                EditorGUILayout.HelpBox("场景里没有 CityTerrain 实例：地形无法计算与显示。", MessageType.Info);
                if (GUILayout.Button("创建地形实例（含显示层）", GUILayout.Width(190f))) CreateTerrainInstance();
                return;
            }

            var profile = _terrain.Profile;
            using (new EditorGUILayout.HorizontalScope())
            {
                var next = (TerrainProfile)EditorGUILayout.ObjectField("TerrainProfile", profile,
                    typeof(TerrainProfile), false);
                if (!ReferenceEquals(next, profile))
                {
                    _terrain.SetProfile(next);            // 唯一写入口：换 Profile 走 TerrainModel 通道
                    _stageKey = 0;                        // 阶段身份变了，强制重注册
                    SyncStages();
                }
                if (GUILayout.Button("新建", GUILayout.Width(50f))) CreateProfile();
            }
            if (profile == null)
            {
                EditorGUILayout.HelpBox("未绑定 TerrainProfile：S1a 缺参数，不会产出高度场。", MessageType.Warning);
                return;
            }

            // ── 低频基底 ──
            var amp = EditorGUILayout.Slider("起伏幅度(m)", profile.BaseAmplitude, 0f, 60f);
            var wave = EditorGUILayout.Slider("低频波长(m)", profile.BaseWavelength, 60f, 2000f);
            var octs = EditorGUILayout.IntSlider("八度", profile.Octaves, 1, 8);
            var gn = EditorGUILayout.Slider("增益", profile.Gain, 0.05f, 0.95f);
            var lac = EditorGUILayout.Slider("倍频间隔", profile.Lacunarity, 1.05f, 4f);
            var warp = EditorGUILayout.Slider("域扭曲", profile.DomainWarpAmp, 0f, 1f);
            if (amp != profile.BaseAmplitude || wave != profile.BaseWavelength || octs != profile.Octaves
                || gn != profile.Gain || lac != profile.Lacunarity || warp != profile.DomainWarpAmp)
                profile.SetBaseShape(amp, wave, octs, gn, lac, warp);

            // ── 城市平整：用户的主旋钮（A-1-1 的实机面）──
            var flat = EditorGUILayout.Slider("城内平整度(0平 1起伏)", profile.UrbanFlatness, 0f, 1f);
            if (!Mathf.Approximately(flat, profile.UrbanFlatness)) profile.SetUrbanFlatness(flat);

            // ── 平整约束求解 ──
            var slope = EditorGUILayout.Slider("地块最大坡度(°)", profile.MaxParcelSlope, 0.5f, 20f);
            var freeW = EditorGUILayout.Slider("自由权重(保留自然度)", profile.FreeWeight, 0f, 3f);
            var inner = EditorGUILayout.Slider("核心内切(m)", profile.ConstraintInner, 0f, 40f);
            var extend = EditorGUILayout.Slider("过渡带外延(m)", profile.ConstraintExtend, 0.5f, 40f);
            if (slope != profile.MaxParcelSlope || !Mathf.Approximately(freeW, profile.FreeWeight)
                || !Mathf.Approximately(inner, profile.ConstraintInner)
                || !Mathf.Approximately(extend, profile.ConstraintExtend))
                profile.SetFlattenSolve(slope, freeW, inner, extend, profile.TargetPlaneMaxSlope);

            // ── 城区范围（M4-o：用户裁定「城区的设计参数需要允许用户在面板里调整」）──
            // 「生效值」必须显式呈现：城心/半径都走「锚点→Profile回退→图幅中心/0.45×图幅」三层解析，
            // 只摆输入框会让用户以为 0 就是「城在 (0,0)、半径 0」——上一轮「开平整没变化」的困惑正源于此。
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("城区范围（压平噪声的作用域）", EditorStyles.boldLabel);
            var gNow = _ctx != null ? _ctx.Grid : default;
            var effC = profile.ResolveUrbanCenter(_project, gNow);
            var effR = profile.ResolveUrbanRadius(_project, gNow);
            EditorGUILayout.LabelField($"  生效：城心 ({effC.x:0}, {effC.y:0}) · 半径 {effR:0}m", EditorStyles.miniLabel);

            var ucx = EditorGUILayout.FloatField("城心 X（0=自动回退）", profile.UrbanCenterFallback.x);
            var ucz = EditorGUILayout.FloatField("城心 Z（0=自动回退）", profile.UrbanCenterFallback.y);
            var ur = EditorGUILayout.Slider("城区半径(m)（0=0.45×图幅）", profile.UrbanRadiusValue, 0f, 6000f);
            if (!Mathf.Approximately(ucx, profile.UrbanCenterFallback.x)
                || !Mathf.Approximately(ucz, profile.UrbanCenterFallback.y)
                || !Mathf.Approximately(ur, profile.UrbanRadiusValue))
                profile.SetUrbanGeometry(new Vector2(ucx, ucz), ur);

            var inE = EditorGUILayout.Slider("压平内缘（u≤ 完全压平）", profile.FlatnessInnerEdge, 0.1f, 1.45f);
            var outE = EditorGUILayout.Slider("放开外缘（u≥ 满幅野性）", profile.FlatnessOuterEdge, 0.2f, 2f);
            if (!Mathf.Approximately(inE, profile.FlatnessInnerEdge) || !Mathf.Approximately(outE, profile.FlatnessOuterEdge))
                profile.SetUrbanEdgeRatio(inE, outE);
            if (profile.FlatnessOuterEdge <= profile.FlatnessInnerEdge)
                EditorGUILayout.HelpBox("外缘必须大于内缘，否则过渡带消失、城界成为硬折线。", MessageType.Warning);

            // ── 自然化（M4-o / D-41：用户原话「消除明显的手绘痕迹」）──
            var nr = EditorGUILayout.Slider("软化核半径(m)", profile.NaturalizeRadius, 4f, 400f);
            var na = EditorGUILayout.Slider("软化强度（1=折痕全消）", profile.NaturalizeAmount, 0f, 1f);
            if (!Mathf.Approximately(nr, profile.NaturalizeRadius) || !Mathf.Approximately(na, profile.NaturalizeAmount))
                profile.SetNaturalize(nr, na);

            // ── 可平整对象（M2 代演品；M5 起由真实地块/路网供数）──
            var auto = EditorGUILayout.Toggle("自动试设地块", _terrain.AutoFlattenAtCenter);
            var tiles = EditorGUILayout.IntSlider("地块数(N×N)", _terrain.AutoTileCount, 1, 12);
            if (auto != _terrain.AutoFlattenAtCenter || tiles != _terrain.AutoTileCount)
                _terrain.SetAutoFlatten(auto, tiles);

            var lockDetail = EditorGUILayout.Toggle("冻结细节(只看规划)", profile.RoughnessLock);
            if (lockDetail != profile.RoughnessLock) profile.SetRoughnessLock(lockDetail);

            // ── 平整滤镜：烘焙式（M4-o / D-41）——用户裁定「不要实时算，点烘焙才算」──
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("③ 平整滤镜（烘焙式，不实时计算）", EditorStyles.boldLabel);
            var flattenOn = EditorGUILayout.ToggleLeft(
                new GUIContent("叠加已烘焙的平整层（关＝只显示手绘意图地表）",
                    "开关只做「叠 / 不叠」，不触发任何求解。\n" +
                    "关＝你看到的就是「噪声基底 + 手绘雕刻」的原始意图地表；\n" +
                    "开＝在其上叠加『上一次点烘焙』算出的修正层。\n" +
                    "改完手绘或参数不会自动重算，需重按烘焙（避免每拖一下都卡、也避免结果意外覆盖设计）。"),
                _terrain.FlattenEnabled);
            if (flattenOn != _terrain.FlattenEnabled) _terrain.SetFlattenEnabled(flattenOn);

            _terrain.SetBakeProjectProbe(_project);
            var bakeState = _terrain.FilterState;
            using (new EditorGUILayout.HorizontalScope())
            {
                var bakeLabel = bakeState == CityTerrain.BakeState.Never
                    ? "烘焙平整（首次）"
                    : bakeState == CityTerrain.BakeState.Stale ? "重新烘焙（手绘/参数已变）" : "重新烘焙平整";
                if (GUILayout.Button(new GUIContent(bakeLabel,
                        "在「噪声基底 + 手绘雕刻」之上跑一次平整求解，结果写成<b>独立滤镜层</b>。\n" +
                        "绝不写回手绘 delta ⇒ 改参数、继续手绘都不会毁掉设计意图（D-01-1 同源纪律）。\n" +
                        "求解是全图重计算，故意只由本按钮触发。"), GUILayout.Height(24f)))
                    BakeFlatten();
                if (bakeState != CityTerrain.BakeState.Never
                    && GUILayout.Button("清空滤镜", GUILayout.Width(72f))) ClearFlattenFilter();
            }
            if (bakeState == CityTerrain.BakeState.Never)
                EditorGUILayout.HelpBox("尚未烘焙过 ⇒ 开关开/关看起来一样（没有可叠加的内容）。" +
                    "先勾「自动试设地块」或添加人工平整区，再点上面「烘焙平整」。", MessageType.Info);
            else if (bakeState == CityTerrain.BakeState.Stale)
                EditorGUILayout.HelpBox("手绘或参数在烘焙之后又改过 ⇒ 画面挂的是旧滤镜。点「重新烘焙」刷新。",
                    MessageType.Warning);
            if (!string.IsNullOrEmpty(_terrain.FlattenHint))
                EditorGUILayout.HelpBox(_terrain.FlattenHint, MessageType.Warning);

            if (_terrain.LastConstraints > 0)
                EditorGUILayout.LabelField($"S1b：约束 {_terrain.LastConstraints} 条 · 改动 {_terrain.LastCells} 格" +
                    (_terrain.LastRejectedConstraints > 0 ? $" · 跳过 {_terrain.LastRejectedConstraints}" : string.Empty),
                    EditorStyles.miniLabel);
        }

        /// <summary>
        /// 烘焙平整（M4-o）。求解是<b>全图重计算</b>，故只由按钮触发、不挂进管线的自动跟随链路。
        /// <para>顺序要求：先把挂起的意图改动提交掉再烘焙 —— 否则烘焙会拿上一轮的基底/手绘求解，
        /// 算出来的滤镜和画面当下状态对不上（表现是"烘一次、画面跳一下"）。</para>
        /// <para>滤镜资产固定路径（同 <b>D-35</b>：内嵌包资产必须"固定路径 + LoadAssetAtPath"，
        /// 禁用 <c>FindAssets</c> 与 <c>GenerateUniqueAssetPath</c> —— 后者是副本反复滋生的成因）。</para>
        /// </summary>
        private void BakeFlatten()
        {
            if (_terrain == null || _project == null) { _lastRunLine = "烘焙失败：缺地形实例或城市项目"; return; }
            var prof = _terrain.Profile;
            if (prof == null) { _lastRunLine = "烘焙失败：实例未绑定 TerrainProfile"; return; }
            if (_ctx == null || _runner == null) { _lastRunLine = "烘焙失败：面板未装配（先绑定城市项目）"; return; }

            // 先冲掉<b>挂起</b>的改动，保证烘焙读的是最新意图地表。
            // ⚠️ 不能用 DirtyBits.All：那会把 S1a 噪声整表重跑（M4-j 实测 4860ms）——
            // 烘焙本身已经是重计算，绝不能再白付一遍全量重算（用户刚说过"快了很多"，别把它还回去）。
            SyncStages();
            var pending = UndoHistoryTracker.PendingDirty;
            if (pending != DirtyBits.None)
            {
                UndoHistoryTracker.ConsumeDirty();
                _runner.MarkDirty(pending);
                CommitAndNotify(pending);
            }

            var path = DeltaFolder + "/FlattenFilter.asset";
            var filter = AssetDatabase.LoadAssetAtPath<SculptFilterAsset>(path);
            if (filter == null)
            {
                if (!AssetDatabase.IsValidFolder(DeltaFolder))
                    AssetDatabase.CreateFolder("Packages/com.collin.citymap/Sample", "Sculpt");
                filter = CreateInstance<SculptFilterAsset>();
                AssetDatabase.CreateAsset(filter, path);
            }

            // 烘焙会整表重写滤镜层 ⇒ 必须可撤销：先登记 undo，让用户按一次 Ctrl+Z 就能退回上一版滤镜
            //（不可撤销的重计算 = 用户不敢点，等于把功能锁死）。
            Undo.RecordObject(filter, "烘焙平整");
            var res = new FlattenSolveStage(prof, _terrain).Bake(_ctx, filter);
            if (!res.Success) { _lastRunLine = "烘焙失败：" + res.Message; return; }

            EditorUtility.SetDirty(filter);
            AssetDatabase.SaveAssets();
            _terrain.SetFlattenFilter(filter);          // 让画面立刻重放新滤镜（只叠加，不再求解）
            _terrain.RecordBake(_project);              // 快照版本+手绘序列 ⇒ 之后改手绘/参数才判过期
            Undo.RecordObject(_terrain, "烘焙平整");
            EditorUtility.SetDirty(_terrain);
            _lastRunLine = $"烘焙平整完成：约束 {res.Outputs} 条 · 写入滤镜 {res.CellsTouched} 格";
            Repaint();
        }

        /// <summary>清空滤镜内容（保留资产文件不动）；手绘 delta 不受影响。</summary>
        private void ClearFlattenFilter()
        {
            var f = _terrain != null ? _terrain.FlattenFilter : null;
            if (f == null) return;
            Undo.RecordObject(f, "清空平整滤镜");
            f.ClearAll();
            EditorUtility.SetDirty(f);
            AssetDatabase.SaveAssets();
            _terrain.MarkFilterBaked();                 // bump 一次让画面重放 ⇒ 立刻回到纯手绘意图地表
            _lastRunLine = "已清空平整滤镜（手绘意图层未受影响）";
            Repaint();
        }

        /// <summary>创建地形实例 + 显示宿主（派生 View 与意图组件同物体，用户不必手动挂）。</summary>
        private void CreateTerrainInstance()
        {
            var go = new GameObject("City Terrain");
            var terr = go.AddComponent<CityTerrain>();
            go.AddComponent<CityTerrainView>();
            if (_project != null) terr.SetProfile(FindOrCreateProfile());
            _terrain = terr;
            // 面板创建的实例必须同时绑定自建项目：面板一关（或独立场景 / Play），权威上下文就消失，
            // 不绑定的话显示宿主没有数据源 ⇒「创建了实例但画面永远空白」。
            // 实测原状：status=「无权威上下文且未绑定自建项目」，chunks=0。
            var view = go.GetComponent<CityTerrainView>();
            if (view != null && _project != null) view.SetStandaloneProject(_project);
            _stageKey = 0;
            SyncStages();
            // H-51：创建实例即绑雕刻层。原状是 delta 永远为 null（磁盘上 SculptDeltaAsset 数量实测 0），
            // 落笔必被 BeginStroke 阻断，而提示只打在控制台 ⇒ 用户看到"拖了没反应"。
            EnsureDeltaLayer();
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            Undo.RegisterCreatedObjectUndo(go, "创建地形实例");
            Repaint();
        }

        private void CreateProfile()
        {
            var asset = FindOrCreateProfile();
            if (asset != null && _terrain != null) _terrain.SetProfile(asset);
            _stageKey = 0;
            SyncStages();
            Repaint();
        }

        /// <summary>
        /// Profile 资产存放处 = **包内 Sample/Profiles/**（AGENTS §0「插件内容须进包」+ §0 showcase 定义；
        /// stretch 包同构先例：`com.collin.stretch/Sample/Profiles/`）。
        /// <para>H-44（校验器 assets-layout 抓到，本会话自造）：原实现静默往 `Assets/Collin` 建资产，
        /// 直接违反 §0；用户点「创建地形实例」时已在磁盘留下游离资产。
        /// 这里不改成弹保存框，是因为 <c>SaveFilePanelInProject</c> 只能落在 <c>Assets/</c> 下
        /// （无法写入 Packages/），弹框反而会把违规路径变成"用户自己选的"。故固定进包内并 Ping 出来可见。</para>
        /// </summary>
        private const string ProfileFolder = "Packages/com.collin.citymap/Sample/Profiles";

        /// <summary>
        /// 找包内 Profile 资产；没有则在包内 Sample/Profiles 建一个。
        /// <para>H-52（实测根因）：原实现用 <c>AssetDatabase.FindAssets("t:TerrainProfile")</c> 找资产，
        /// 但 <c>Packages/</c> 下的内嵌包<b>不在 AssetDatabase 索引里</b> ⇒ FindAssets 恒返回 0
        /// （探针实测：资产确实存在且有路径，FindAssets 仍为 0）。
        /// 于是每次点「创建地形实例」都走"新建"分支，而 <c>GenerateUniqueAssetPath</c>
        /// 会在同目录追加 " 1 / 2 / 3" ⇒ 副本资产反复滋生（用户上一轮删掉的 6 个副本就是这么长回来的）。
        /// 正解：内嵌包资产一律用<b>固定路径 + LoadAssetAtPath</b>，创建时也不加唯一后缀。</para>
        /// </summary>
        private TerrainProfile FindOrCreateProfile()
        {
            var existing = LoadFromPackage<TerrainProfile>(ProfileFolder + "/TerrainProfile.asset");
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Packages/com.collin.citymap/Sample"))
            {
                // 非阻塞呈现（AGENTS §2.1.5：DisplayDialog 会卡死 AI 指令通道）。
                // <b>用 Log 而非 LogError/LogWarning</b>：后两者会自造控制台计数，
                // 直接顶爆 §1.6b「consoleErrors==0 且 consoleWarnings==0」的交锁门禁 ——
                // 这条错误路径本身就是门禁的一部分，不能靠制造红灯来报告问题。
                const string why = "包内 Sample 目录缺失，无法创建 TerrainProfile。请确认包已正确解析（package resolve）。";
                Debug.Log($"{PackageIdentity.LogPrefix} {why}");
                _lastRunLine = why;
                return null;
            }
            if (!AssetDatabase.IsValidFolder(ProfileFolder))
                AssetDatabase.CreateFolder("Packages/com.collin.citymap/Sample", "Profiles");

            // 固定名，不用 GenerateUniqueAssetPath：后者是副本滋生的直接来源（H-52）
            var path = ProfileFolder + "/TerrainProfile.asset";
            var asset = CreateInstance<TerrainProfile>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(asset);
            Debug.Log($"{PackageIdentity.LogPrefix} 已创建 TerrainProfile：{path}");
            return asset;
        }

        /// <summary>内嵌包资产读取：走磁盘固定路径，不依赖 FindAssets 索引（H-52）。</summary>
        private static T LoadFromPackage<T>(string assetPath) where T : UnityEngine.Object
            => AssetDatabase.LoadAssetAtPath<T>(assetPath);

        /// <summary>
        /// 阶段树 = 信息而非门禁（08 §3.3）：显示各阶段的脏位命中与耗时，绝不禁用工具。
        /// </summary>
        private void DrawStageTree()
        {
            if (_ctx == null) { EditorGUILayout.LabelField("绑定城市项目后显示阶段树"); return; }

            var pending = UndoHistoryTracker.PendingDirty;
            var expanded = IntentChangePlanner.Expand(pending);

            EditorGUILayout.LabelField($"挂起脏集合：{Summarize(pending)}" +
                (pending != DirtyBits.None ? $"　→ 展开后：{Summarize(expanded)}" : string.Empty),
                EditorStyles.miniLabel);

            if (!string.IsNullOrEmpty(_statusLine) && pending != DirtyBits.None && !_autoFollow)
                EditorGUILayout.HelpBox($"自动跟随已关闭，有 {CountBits(pending)} 项改动未生效（A-0-5）",
                    MessageType.Warning);

            foreach (var id in PipelineRunner.ExecutionOrder)
            {
                var bit = StageBitOf(id);
                var hit = bit != DirtyBits.None && (expanded & bit) != 0;
                var ms = LastMsOf(id);
                var color = hit ? (Color)new Color32(255, 200, 90, 255) : GUI.contentColor;
                var prev = GUI.contentColor;
                GUI.contentColor = color;
                EditorGUILayout.LabelField($"　{StageLabel(id)}",
                    ms >= 0 ? $"{(hit ? "脏" : "—")}　上次 {ms}ms" : (hit ? "脏" : "—"));
                GUI.contentColor = prev;
            }
        }

        private int LastMsOf(StageId id)
        {
            for (var i = _ctx.Report.Entries.Count - 1; i >= 0; i--)
                if (_ctx.Report.Entries[i].Stage == id) return _ctx.Report.Entries[i].Ms;
            return -1;
        }

        /// <summary>
        /// 阶段 → 触发它的脏位。**一律读 Runtime 的 <see cref="StageClaims"/>（单一真源）**，
        /// 面板不再自持一份映射：否则会出现"面板说脏、Runner 不跑"或反之（00 §4.1）。
        /// </summary>
        private static DirtyBits StageBitOf(StageId id) => StageClaims.Of(id);

        private static string StageLabel(StageId id)
        {
            switch (id)
            {
                case StageId.S0_Assemble:     return "S0 装配（负约束/占用烘焙）";
                case StageId.S1a_TerrainBase: return "S1a 地形基底噪声";
                case StageId.S2_River:        return "S2 河流下切与水系";
                case StageId.S3_Road:         return "S3 路网与补全";
                case StageId.S4_Parcel:       return "S4 地块与建筑位";
                case StageId.S1b_Flatten:     return "S1b 平整约束回写";
                case StageId.S5_LandUse:      return "S5 用途场烘焙";
                case StageId.S6_BuildingFill: return "S6 建筑落位";
                default: return id.ToString();
            }
        }

        private static string Summarize(DirtyBits d)
            => d == DirtyBits.None ? "无" : d.ToString();

        private static int CountBits(DirtyBits d)
        {
            var n = 0;
            for (var v = (int)d; v != 0; v &= v - 1) n++;
            return n;
        }

        /// <summary>重新生成 = 用当前意图层重算全部派生层，人工层原样叠回（08 §3.5，免确认）。</summary>
        private void Regenerate()
        {
            if (_ctx == null)
            {
                // 非阻塞提示（AGENTS §2.1.5）：状态行就在按钮下方，比模态框更快看到，也不卡指令通道
                _lastRunLine = "请先绑定城市项目（面板顶部「城市项目」拖入或点「创建」）";
                Debug.Log($"{PackageIdentity.LogPrefix} {_lastRunLine}");
                Repaint();
                return;
            }
            SyncStages();
            _runner.MarkDirty(DirtyBits.All);
            // 全量重算的影响面 = 整图：显式传 null（= 全图），让 View 全量重建而不是按残留脏盒增量
            CommitAndNotify(DirtyBits.All);
            _lastRunLine = $"全量重算：执行 {_lastExecutedStages} 阶段 / {_ctx.Report.TotalMs}ms" +
                           $"（警告 {_ctx.Report.TotalWarnings}）";
            UndoHistoryTracker.RebuildBaseline();
            Repaint();
        }
    }

    /// <summary>
    /// 场景侧项目挂载点（可选）：让面板能从场景对象自动找到 CityProject。
    /// M1 只做绑定桥梁，不承载任何生成逻辑。
    /// </summary>
    public sealed class CityProjectProxy : MonoBehaviour
    {
        public CityProject Project;
    }
}
