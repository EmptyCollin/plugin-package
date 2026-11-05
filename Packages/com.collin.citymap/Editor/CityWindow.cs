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
        /// 工作流相位（12 §1 三段式 + 未接线的第四步占位）：① 定世界 → ② 雕山水 → ③ 平整出地表 → ④ 铺路网落建筑。
        /// <para><b>相位是便捷动作、不是门禁</b>（00 A-24）：点一步只把该步该有的状态设好，
        /// 其它工具与参数仍随时可用。真实单一真源在场景实例的序列化字段上
        /// （<see cref="CityTerrain.FlattenEnabled"/> 与两个工具接管位，D-33／H-50／D-34），
        /// 面板按它们回推当前相位，避免"面板显示第③步、数据里闸门还关着"的两套状态打架。</para>
        /// <para>用户 2026-11-06 裁定：<b>每一步只显示该步的操作</b>，所以 OnGUI 按本枚举分支绘制，
        /// 不再把所有区块无条件堆叠。</para>
        /// </summary>
        public enum WorkflowStep { Params = 1, Sculpt = 2, Flatten = 3, Roads = 4 }

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

        // ⚠️ 这里原先挂着三个 public 静态入口：ActiveProject / ActiveRunner / CurrentMode。
        // 本轮全部删除，理由不是"没人用"这么轻：
        // · CurrentMode 是 EditorWindow 的<b>实例字段的静态投影</b>，随域重载归零 ⇒
        //   H-50 当年就是被它坑成「面板显示雕刻模式、笔刷按 Inspect 放行鼠标」（左键变框选+光圈消失）。
        //   真源已在场景实例的序列化字段上（CityTerrain.SculptToolActive／WaterToolActive，D-34）。
        //   只要这个属性还挂着，下一个人就会顺手读它 —— 删掉比注释警告更可靠。
        // · ActiveProject 原先给水工具读海平面；海平面已按用户裁定删除，它再无消费者。
        //   工具需要项目时一律即时读面板单源，不缓存（缓存会在换项目时过期，§0.1 原则2）。
        // · ActiveRunner 零消费者。面板与 View 的接合只走 CityRuntime（提交序号），不开放 runner 引用。

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

        /// <summary>
        /// 面板主结构（用户 2026-11-06 裁定重排）：
        /// <para>① <b>步骤是唯一导航</b>：每步只画该步的控件。原版把「城市项目／地形参数」两块
        /// <b>无条件</b>画在每一步下面（旧 329~331 行），于是雕山水时一整屏噪声与平整参数还挂着；
        /// 而水体区块却挂在右上角<b>工具</b>选择上（旧 339 行）⇒ 在②里反而看不到水。两套轴互相抢方向盘，
        /// 用户永远分不清该点哪个 —— 现在工具接管收进步骤②内部，右上角不再有工作流语义。</para>
        /// <para>② <b>右上角＝分层显隐 + 该层实测统计</b>（用户裁定："应该是分层的统计信息，而非和工作流的功能按键耦合"）。</para>
        /// <para>③ <b>流水线诊断默认收起</b>：脏集合/阶段耗时是信息不是主操作，原先常驻面板底部。</para>
        /// <para>④ 撤销/重做是 Unity 全局快捷键，不需要面板按钮（原「新建城…」是空壳：按下只画一行
        /// "M1 未实现"的 Label，却和真正能用的「重新生成」并排，观感上像坏掉 —— 已摘除）。</para>
        /// </summary>
        private void OnGUI()
        {
            var et = Event.current.type;
            EditorGUI.BeginChangeCheck();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("重新生成", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    Regenerate();
                GUILayout.Space(6f);
                _autoFollow = GUILayout.Toggle(_autoFollow,
                    new GUIContent("自动跟随", "关＝改动只挂起不生效（A-0-5，脏集合会显示未生效项）"),
                    EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                DrawLayerStats();
            }

            EditorGUILayout.Space(4f);
            DrawWorkflowStepBar();
            EditorGUILayout.Space(4f);

            switch (_step)
            {
                case WorkflowStep.Sculpt: DrawSculptSection(); break;
                case WorkflowStep.Flatten: DrawFlattenSection(); break;
                case WorkflowStep.Roads: DrawRoadsPlaceholder(); break;
                default:                   DrawParamsSection(); break;
            }

            EditorGUILayout.Space(4f);
            DrawDiagnostics();
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(_statusLine, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(_lastRunLine, EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();

            SettleOnInputEnd(et, EditorGUI.EndChangeCheck());
        }

        /// <summary>拖动中标志：松手那一帧要结算。</summary>
        bool _settlingDrag;

        /// <summary>
        /// <b>输入动作一结束就结算</b>（用户反馈②「数值变更时没有及时重建」的修复点）。
        /// <para>根因：<see cref="UndoHistoryTracker.MergeWindowMs"/>(250ms) 的本意是把<b>连续 Ctrl+Z</b>
        /// 合成一次重算（A-17），但它对所有意图改动一视同仁；而 <c>Scan()</c> 每检出一个差异就把
        /// 计时表拨回当下 ⇒ 拖滑条<b>全程</b>都处在"窗口内"，一次都不结算，只有手停满 250ms 才动一次。</para>
        /// <para>规则：拖动期间不结算（那正是防卡顿的用途）；<b>松手立刻</b>结束窗口；
        /// 键盘/失焦/勾选/按钮这类离散改动<b>当场</b>结束窗口。</para>
        /// <para>只结束窗口、<b>不自己提交</b>：脏位仍由 <see cref="OnEditorUpdate"/> 的既有链路消费
        /// （含 <c>LastDirtyBounds</c> 搬入）。另开提交口会与自动跟随叠成双跑，还绕过增量依据
        /// 让重建退化成全图（M4-j 实测 289ms/笔 vs 0~2ms/笔）。</para>
        /// </summary>
        private void SettleOnInputEnd(EventType et, bool changed)
        {
            if (et == EventType.MouseDown || et == EventType.MouseDrag)
            {
                if (changed) _settlingDrag = true;
                return;
            }
            if (et == EventType.MouseUp)
            {
                if (_settlingDrag || changed) UndoHistoryTracker.SettleNow();
                _settlingDrag = false;
                return;
            }
            // Layout 阶段 GUI.changed 不成立，跳过以免每帧重复拨表
            if (changed && et != EventType.Layout && et != EventType.Repaint)
                UndoHistoryTracker.SettleNow();
        }

        /// <summary>
        /// 右上角分层统计。每层只报<b>实测派生量</b>，未实现的层直接写“—”，不留假数字。
        /// <para>地表／水面／平整叠加三个芯片可点（纯显示态，不碰意图）；手绘与路/地块/建筑是只读读数 ——
        /// 把“还没接线”伪装成可点按钮，比不展示更糟。</para>
        /// </summary>
        private void DrawLayerStats()
        {
            if (_terrain == null) _terrain = FindTerrainInstance();
            if (_view == null && _terrain != null) _view = _terrain.GetComponent<CityTerrainView>();
            if (_terrain == null || _view == null)
            {
                EditorGUILayout.LabelField("无显示层", EditorStyles.miniLabel, GUILayout.Width(56f));
                return;
            }

            var g = _view.BoundGrid;
            var terrOn = GUILayout.Toggle(_view.ShowTerrain, new GUIContent(
                g.HasValue ? $"地表 {g.Value.Columns}²@{g.Value.CellSize:0}m" : "地表 —",
                "地形显示开关（纯显隐，不改任何数据）。"),
                EditorStyles.miniButton, GUILayout.Height(18f));
            if (terrOn != _view.ShowTerrain) _view.SetShowTerrain(terrOn);

            var wf = _view.BoundWater;
            var waterTip = wf == null
                ? "本轮没有水面派生：还没加水，或水都退干了。\n本芯片是水面显示开关。"
                : $"淹水 {wf.WaterCells} 格 · 缓冲带 {wf.BufferCells} 格\n" +
                  $"产水种子 {wf.ActiveSeeds}/{wf.TotalSeeds}\n" +
                  $"水面高程 {LevelText(wf.MinWaterLevel)} ~ {LevelText(wf.MaxWaterLevel)}m";
            var waterOn = GUILayout.Toggle(_view.ShowWater, new GUIContent(
                wf == null ? "水 —" : $"水 {wf.ActiveSeeds}·{wf.WaterAreaRatio * 100f:0.##}%", waterTip),
                EditorStyles.miniButton, GUILayout.Height(18f));
            if (waterOn != _view.ShowWater) _view.SetShowWater(waterOn);

            var d = _terrain.Delta;
            EditorGUILayout.LabelField(new GUIContent(
                d == null || d.IsEmpty ? "手绘 —" : $"手绘 {d.CountAdditiveCells()}格·±{d.MaxAbsDelta():0.#}m",
                "雕刻意图层的实测改动（只读）。"),
                EditorStyles.miniButton, GUILayout.Height(18f), GUILayout.Width(124f));

            var st = _terrain.FilterState;
            var fl = _terrain.FlattenFilter;
            var cells = 0; var maxDelta = 0f;
            if (fl != null) fl.Measure(out cells, out maxDelta);
            var flatLabel = st == CityTerrain.BakeState.Never || fl == null
                ? "平整 —"
                : $"平整 {cells}格·削{maxDelta:0.#}m{(st == CityTerrain.BakeState.Stale ? "·旧" : string.Empty)}";
            var flatOn = GUILayout.Toggle(_terrain.FlattenEnabled, new GUIContent(flatLabel,
                "已烘焙平整滤镜的<b>叠加</b>开关（关＝只看手绘意图地表）。\n" +
                "「旧」＝手绘或参数在烘焙之后又改过，画面挂的是上一次的结果。"),
                EditorStyles.miniButton, GUILayout.Height(18f), GUILayout.Width(128f));
            if (flatOn != _terrain.FlattenEnabled)
            {
                _terrain.SetFlattenEnabled(flatOn);
                MarkTerrainSceneDirty();
                UndoHistoryTracker.SettleNow();
            }

            EditorGUILayout.LabelField(new GUIContent("路 — 地块 — 建筑 —",
                "S3 路网 / S4 地块 / S6 建筑尚未接线（下一里程碑）。"),
                EditorStyles.miniLabel, GUILayout.Width(112f));
        }

        /// <summary>高程读数：-∞ = 没有水面，必须显示成“—”而不是“-∞”。</summary>
        static string LevelText(float v)
            => float.IsNegativeInfinity(v) || float.IsPositiveInfinity(v) ? "—" : v.ToString("0.#");

        /// <summary>显示层引用（分层统计读它的派生量；面板不创建它）。</summary>
        CityTerrainView _view;

        /// <summary>
        /// 工作流步骤条：① 定世界 → ② 雕山水 → ③ 平整出地表 → ④ 路网·建筑（未接线，灰置）。
        /// <para>相位是便捷动作、<b>不是门禁</b>（00 A-24）。但每一步<b>只画该步的控件</b>
        /// （用户 2026-11-06 裁定），所以这里决定的是"看什么"，不是"能用什么数据"。</para>
        /// </summary>
        private void DrawWorkflowStepBar()
        {
            EditorGUILayout.LabelField("工作流（每步只显示该步的操作）", EditorStyles.miniBoldLabel);
            if (_terrain == null) _terrain = FindTerrainInstance();
            SyncStepFromState();

            using (new EditorGUILayout.HorizontalScope())
            {
                StepButton(WorkflowStep.Params, "① 地图参数", true,
                    "城市项目绑定、地形噪声基底（山与起伏）、城区范围（噪声压平的作用域）。\n" +
                    "世界尺寸/格档位/随机种子在 CityProject 资产上改（Inspector），改完回来会自动重算。");
                StepButton(WorkflowStep.Sculpt, "② 雕山水", true,
                    "山水设计层：抬升/下压/平滑笔刷 + 水体种子。\n" +
                    "进入本步会关掉平整闸门（D-33）——否则城内强约束会当场把抬起来的山压回去（反馈②「抬了没反应」的正解）。");
                StepButton(WorkflowStep.Flatten, "③ 平整出地表", true,
                    "在②定稿的意图地表<b>之上</b>做整理：可平整对象、自然化、求解参数与「烘焙平整」。\n" +
                    "本步绝不改手绘层（D-41），关掉叠加就回到纯手绘。");
                StepButton(WorkflowStep.Roads, "④ 路网·建筑", false,
                    "尚未接线（S3 路网 / S4 地块 / S6 建筑）。当前平整只能用「自动试设地块」代演约束源。");
            }

            if (_terrain == null)
                EditorGUILayout.HelpBox("还没有地形实例：在第①步点「创建地形实例（含显示层）」。", MessageType.Info);
        }

        /// <summary>
        /// 真源只用来纠正<b>工具接管显示</b>（H-50：接管位在场景实例上，能跨域重载存活）。
        /// <para><b>刻意不再用工具模式反推步骤</b>（用户反馈①「两组按钮互相抢方向盘」的根因）：
        /// 原版是双向同步 —— 雕刻开着就把步骤强设成②，点步骤又反过来强改工具模式，
        /// 于是"我在②雕山水，右上角却显示检视"这类矛盾必然出现。
        /// 现在 <c>_mode</c> 只是步骤②内部的一个局部开关（左键归笔刷还是归水），
        /// 步骤不再被它劫持；只在"闸门与步骤明显矛盾"时归位一次（用户在 Inspector 直改闸门的情形）。</para>
        /// </summary>
        private void SyncStepFromState()
        {
            var sculptOn = FindSculptActiveTerrain();
            var waterOn = FindWaterActiveTerrain();
            if (sculptOn != null) _mode = ToolMode.TerrainSculpt;
            else if (waterOn != null) _mode = ToolMode.RiverBrush;
            else if (_mode == ToolMode.TerrainSculpt || _mode == ToolMode.RiverBrush)
            {
                if (HasAnyTerrainInstance()) _mode = ToolMode.Inspect;
                // 场景里一个实例都没有时不纠正：否则用户连"缺实例"的提示都看不到（等于把 UI 锁死）
            }

            if (_step != WorkflowStep.Sculpt) return;
            if (_terrain != null && _terrain.FlattenEnabled) _step = WorkflowStep.Flatten;
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

        /// <summary>场景里是否有把水工具接管开关打开的地形实例（同 D-34 真源）。</summary>
        internal static CityTerrain FindWaterActiveTerrain()
        {
            foreach (var t in IntentRegistry.All)
            {
                var ct = t as CityTerrain;
                if (ct != null && ct.enabled && ct.WaterToolActive) return ct;
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

        private void StepButton(WorkflowStep s, string label, bool enabled, string tip)
        {
            var prev = GUI.backgroundColor;
            if (_step == s) GUI.backgroundColor = new Color(0.42f, 0.72f, 1f);
            if (!enabled) GUI.enabled = false;
            // 未接线的步骤必须**看得见但点不动**：藏起来会让用户以为流程只有三步，
            // 而真实状态是"第四步还没做"——后者才是需要如实呈现的信息。
            if (GUILayout.Button(new GUIContent(label, tip), GUILayout.Height(24f))) EnterStep(s);
            GUI.enabled = true;
            GUI.backgroundColor = prev;
        }

        /// <summary>
        /// 点一步＝把该步的状态设好（便捷动作，不是门禁）。
        /// <para>写的一律是<b>场景实例的序列化真源</b>（H-50／D-34）：只改面板字段就会出现
        /// "面板显示在雕刻模式、笔刷按 Inspect 放行鼠标"。</para>
        /// </summary>
        private void EnterStep(WorkflowStep s)
        {
            _step = s;
            if (_terrain == null) _terrain = FindTerrainInstance();
            if (_terrain == null) { Repaint(); return; }

            switch (s)
            {
                case WorkflowStep.Sculpt:
                    _terrain.SetFlattenEnabled(false);      // D-33 闸门：雕山水时不叠加自动整理
                    _terrain.SetSculptToolActive(true);     // 默认把左键给笔刷；要加水在本步内切"加水种子"
                    EnsureDeltaLayer();                     // H-51：没雕刻层落笔必被 blocked
                    break;
                case WorkflowStep.Flatten:
                    _terrain.SetFlattenEnabled(true);
                    _terrain.SetSculptToolActive(false);
                    _terrain.SetWaterToolActive(false);     // 离开②必须收回左键，否则笔刷/水工具仍在抢
                    break;
                default:                                     // ① 与未接线的④：不接管场景视图
                    _terrain.SetSculptToolActive(false);
                    _terrain.SetWaterToolActive(false);
                    break;
            }
            _mode = ToolMode.Inspect;
            MarkTerrainSceneDirty();
            UndoHistoryTracker.SettleNow();                 // 闸门/接管是真改动，立刻见效
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

        bool _diagOpen;

        /// <summary>
        /// 流水线诊断（默认<b>收起</b>）：挂起脏集合、阶段命中与耗时。
        /// <para>这些信息对开发者排查有用（A-0-5 要求"关跟随时有 N 项未生效"可见），
        /// 但它不是用户的主操作；原先常驻面板底部把主操作挤成半屏。折叠后收起状态序列化在
        /// 面板字段上即可（纯视图态，丢了只是重开默认收起，无数据风险）。</para>
        /// </summary>
        private void DrawDiagnostics()
        {
            _diagOpen = EditorGUILayout.Foldout(_diagOpen, "流水线诊断（阶段 · 脏集合 · 耗时）", true);
            if (!_diagOpen)
            {
                // 收起时仍要呈现"有改动未生效"这一句（A-0-5 的底线）：
                // 用户可能正因为关了自动跟随而看到旧画面，这句话不能藏进折叠里。
                if (!_autoFollow && UndoHistoryTracker.PendingDirty != DirtyBits.None)
                    EditorGUILayout.HelpBox($"自动跟随已关闭，有 {CountBits(UndoHistoryTracker.PendingDirty)} 项改动未生效（A-0-5）",
                        MessageType.Warning);
                return;
            }
            DrawStageTree();
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

        /// <summary>
        /// 第①步上半：项目绑定 + 世界参数（尺寸/档位/外环/分块）。
        /// <para>这些原先只能去 CityProject 的 Inspector 上改，而 §0.1 原则1 要求
        /// "面板直接建在场景实例/参数对象上，不让用户跳去资产文件编辑"；
        /// 写一律走 <see cref="CityProject.SetWorldParams"/> 唯一入口（内部 bump WorldParam 通道，
        /// 会全量重算 —— 这是事实，不隐藏）。</para>
        /// <para><b>随机种子刻意只读</b>：08 §6.2 规定改种子必须先过"影响预览 + 自动快照"，
        /// 而那个预览面板还没实现。在没有预览的前提下给一个能一键毁掉整张图的按钮，
        /// 比只读展示更糟。</para>
        /// </summary>
        private void DrawProjectField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var next = (CityProject)EditorGUILayout.ObjectField("城市项目", _project,
                    typeof(CityProject), false);
                if (!ReferenceEquals(next, _project)) Bind(next);
                if (GUILayout.Button("创建", GUILayout.Width(50f))) CreateProject();
            }
            if (_project == null)
            {
                EditorGUILayout.HelpBox("还没有城市项目：点「创建」或把已有 CityProject 资产拖进来。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("世界（改动 ⇒ 全量重算，含地形噪声）", EditorStyles.boldLabel);

            var newSize = EditorGUILayout.FloatField(
                new GUIContent("世界边长(m)", "2000=Sample 档，8000=正式档。\n改动会重建格网与掩码 ⇒ 全量重算。"),
                _project.WorldSizeValue);
            var scale = (WorldScale)EditorGUILayout.Popup(
                new GUIContent("格档位", "Small=逻辑格 2m，Large=4m。\n档位决定精度与内存：8km@4m 是 2000² 格。"),
                Mathf.Max(0, System.Array.IndexOf(_scaleLabels, _project.ScaleProfile.ToString())), _scaleLabels);
            var inset = EditorGUILayout.FloatField(
                new GUIContent("外环禁建带(m)", "让城边界渐隐成郊野，并规避边界半格问题。"),
                _project.GetGrid().BuildableInset);

            var chunk = EditorGUILayout.FloatField(
                new GUIContent("计算分块(m)", "管线内部的逻辑 chunk 边长（必须是逻辑格整数倍）。"),
                _project.GetGrid().ChunkSize);
            var region = EditorGUILayout.FloatField(
                new GUIContent("区域(m)", "脏区合并粒度：改动落在同一 region 内就一起重算。"),
                _project.GetGrid().RegionSize);
            var sceneChunk = EditorGUILayout.FloatField(
                new GUIContent("导出切块(m)", "交付期切块尺寸（Q-12），必须也是逻辑格整数倍。"),
                _project.SceneChunkSize);

            if (newSize != _project.WorldSizeValue || scale != _project.ScaleProfile
                || !Mathf.Approximately(inset, _project.GetGrid().BuildableInset)
                || !Mathf.Approximately(chunk, _project.GetGrid().ChunkSize)
                || !Mathf.Approximately(region, _project.GetGrid().RegionSize)
                || !Mathf.Approximately(sceneChunk, _project.SceneChunkSize))
            {
                // 参数非法会抛（A-0-7：非法档位必须呈现给用户，不得静默取整）
                try
                {
                    Undo.RecordObject(_project, "改世界参数");
                    if (_project.SetWorldParams(newSize, scale, inset, chunk, region, sceneChunk))
                        MarkProjectDirty();
                }
                catch (System.Exception ex)
                {
                    _lastRunLine = "世界参数非法：" + ex.Message;
                    Repaint();
                }
            }

            EditorGUILayout.LabelField(new GUIContent(
                $"  生效格网：{_project.CellSize:0}m 逻辑格 · {_project.DisplayStep:0}m 显示步 · 种子 {_project.Seed}",
                "随机种子改一次全城重生成，需先过影响预览（未实现）⇒ 此处只读。\n" +
                "要换种子请在 CityProject 资产上改。"), EditorStyles.miniLabel);

            // ── 水体基准（02 二次改版：只剩缓冲带这一项全局参数）──
            if (_project != null)
            {
                var wb = EditorGUILayout.Slider(new GUIContent("水缓冲带(m)",
                        "每片水外扩的不可建筑带宽度。0 = 不设带。\n" +
                        "换算成格时<b>向上取整</b>（宁多不一格漏）。"),
                    _project.WaterBufferMeters, 0f, 60f);
                if (!Mathf.Approximately(wb, _project.WaterBufferMeters))
                {
                    if (_project.SetWaterBuffer(wb)) MarkProjectDirty();
                }
            }
        }

        static readonly string[] _scaleLabels = { "Small2km", "Large8km" };

        /// <summary>
        /// 水体面板（02 <b>二次改版</b>）：加/移水的模式、新种子默认参数、<b>种子列表编辑</b>
        /// （坐标、水面高程、扩散半径均可调，也可手动添加、启停、删除）。
        /// <para>面板必须显示"淹了多少格"——水面是派生量，只报种子数会让用户以为
        /// "有种子就有水"（实际可能种子所在格已高于自己的水面高程、水退干了）。</para>
        /// <para>已删除的整套东西（用户 2026-11-05 裁定"海平面不用单独做了"）：
        /// 全局海平面滑条、「高处点击先询问」开关、陆上水体待确认内联条、局部水位复选框与 +∞ 哨兵。
        /// 现在点一格就是一条完整的水：高程 = 地面 + 水深、范围 = 扩散半径，不问、不猜、没有隐式基准。</para>
        /// </summary>
        private void DrawWaterPanel()
        {
            if (_terrain == null) _terrain = FindTerrainInstance();
            EditorGUILayout.LabelField("水体（水 = 种子高程以下、半径以内的连通域）", EditorStyles.boldLabel);
            if (_terrain == null)
            {
                EditorGUILayout.HelpBox("先「创建地形实例」才能加水：水面按实例逐格派生。", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var add = GUILayout.Toggle(_terrain.WaterMode == CityTerrain.WaterToolMode.Add,
                    new GUIContent("加水种子", "点场景一格：在该格建一颗种子。\n"
                        + "水面高程 = 该格地面 + 下面的默认水深，向外扩散受默认半径限制。\n"
                        + "点山脊也只会得到一个小天池，不会淹掉整个山谷。"),
                    EditorStyles.toolbarButton);
                var rm = GUILayout.Toggle(_terrain.WaterMode == CityTerrain.WaterToolMode.Remove,
                    new GUIContent("移除种子", "点场景一格：删掉最近的种子（16 格内）。"),
                    EditorStyles.toolbarButton);
                var want = add ? CityTerrain.WaterToolMode.Add : CityTerrain.WaterToolMode.Remove;
                if (want != _terrain.WaterMode)
                {
                    _terrain.SetWaterToolMode(want);
                    MarkTerrainSceneDirty();
                }
            }

            // 接管权收进步骤②（不再依赖右上角工具按钮）：读写都落在场景实例的序列化真源上（H-50／D-34）。
            var take = EditorGUILayout.ToggleLeft(
                new GUIContent("左键接管场景视图（点一格加水／删种子）",
                    "开＝场景视图左键归水工具；与雕刻笔刷<b>互斥</b>（写入侧互相清掉）。\n" +
                    "关＝左键还给编辑器默认工具，此时只能在本面板编辑种子列表。"),
                _terrain.WaterToolActive);
            if (take != _terrain.WaterToolActive)
            {
                _terrain.SetWaterToolActive(take);
                if (take) _mode = ToolMode.RiverBrush;
                MarkTerrainSceneDirty();
            }

            var depth = EditorGUILayout.Slider(
                new GUIContent("新种子默认水深(m)",
                    "水面高程 = 点击点地面高程 + 此值。\n只影响<b>新建</b>的种子；已有种子在列表里各自可调。"),
                _terrain.WaterDepth, 0.1f, 60f);
            if (!Mathf.Approximately(depth, _terrain.WaterDepth)) _terrain.SetWaterDepth(depth);

            var rad = EditorGUILayout.Slider(
                new GUIContent("新种子默认半径(m)",
                    "水沿谷扩散的最大步数（换算成格、向上取整）。\n" +
                    "0 = 不限，用来做海湾/大湖；\n小值防止「点山脊 → 顺坡淹掉整个山谷」。\n只影响<b>新建</b>的种子。"),
                _terrain.WaterSeedRadius, 0f, 2000f);
            if (!Mathf.Approximately(rad, _terrain.WaterSeedRadius)) _terrain.SetWaterSeedRadius(rad);

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
            var grid = _ctx != null ? _ctx.Grid : (_view != null ? _view.BoundGrid ?? default : default);
            string toRemove = null;

            for (var i = 0; i < water.Count; i++)
            {
                var s = water[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    var en = EditorGUILayout.Toggle(
                        new GUIContent("启", "启用位：关掉≠删除，保留参数便于反复试验。"),
                        s.enabled, GUILayout.Width(20f));
                    if (en != s.enabled && water.SetSeedEnabled(s.stableId, en))
                        TouchWaterAsset(grid, s.cx, s.cz);

                    var px = EditorGUILayout.Vector2Field(new GUIContent("格", "种子所在<b>逻辑格号</b>（不是世界米）：\n"
                            + "换 worldSize / 格档位时米坐标会漂，格号不会。"),
                        new Vector2(s.cx, s.cz), GUILayout.Width(96f));
                    if ((int)px.x != s.cx || (int)px.y != s.cz)
                    {
                        if (!water.MoveSeed(s.stableId, (int)px.x, (int)px.y))
                            EditorGUILayout.LabelField(new GUIContent("占", "目标格已有另一颗种子 ⇒ 拒绝移动（同格两颗只会重复洪泛）"),
                                GUILayout.Width(20f));
                        TouchWaterAsset(grid, s.cx, s.cz);
                    }

                    var lv = EditorGUILayout.FloatField(new GUIContent("高程",
                            "这片水的水面高度（米）。格必须<b>严格低于</b>它才算水。\n" +
                            "抬地形/削高会让水自动退，改这里会让水涨落。"),
                        s.level, GUILayout.Width(58f));
                    if (!Mathf.Approximately(lv, s.level) && water.SetSeedLevel(s.stableId, lv))
                        TouchWaterAsset(grid, s.cx, s.cz);

                    var rd = EditorGUILayout.FloatField(new GUIContent("半径",
                            "沿谷扩散的最大步数（米，换算成格向上取整）。\n" +
                            "0 = 不限，用来做海湾/大湖；小值只成一个小水塘。"),
                        s.radiusMeters, GUILayout.Width(56f));
                    if (!Mathf.Approximately(rd, s.radiusMeters) && water.SetSeedRadius(s.stableId, rd))
                        TouchWaterAsset(grid, s.cx, s.cz);

                    if (GUILayout.Button(new GUIContent("×", "删除这颗种子"), GUILayout.Width(18f)))
                        toRemove = s.stableId;
                }
            }

            if (!string.IsNullOrEmpty(toRemove))
            {
                Undo.RecordObject(water, "移除水种子");
                if (water.TryGet(toRemove, out var gone))
                {
                    water.RemoveSeed(toRemove);
                    TouchWaterAsset(grid, gone.cx, gone.cz);   // 单一提交点：内部只 bump 一次
                }
                else water.RemoveSeed(toRemove);               // id 已失效（撤销/他人删除）：不虚报改动
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("手动加一个种子",
                    "加在图幅中心格：水面高程 = 该处地面 + 默认水深，半径 = 默认半径。\n" +
                    "加完在列表里调坐标与高程即可。"), GUILayout.Height(22f)))
                {
                    Undo.RecordObject(water, "添加水种子");
                    var cc = grid.CellCount > 0 ? grid.Columns / 2 : 0;
                    var rr = grid.CellCount > 0 ? grid.Rows / 2 : 0;
                    var ground = GroundHeightAt(cc, rr);
                    water.AddSeed(cc, rr, ground + _terrain.WaterDepth, _terrain.WaterSeedRadius, out _);
                    TouchWaterAsset(grid, cc, rr);
                }
                if (WaterBrushTool.TryGetHover(out var hcell, out var hh))
                {
                    EditorGUILayout.LabelField(
                        $"光标 格({hcell.x},{hcell.y}) 地面 {hh:0.##}m → 水面 {hh + _terrain.WaterDepth:0.##}m",
                        GUILayout.Width(220f));
                }
            }
        }

        /// <summary>
        /// 某格的地面高程（手动加种子时算默认水面用）。
        /// <para>取显示层的场 = <b>与画面同源</b>（同 H-53 纪律）：让工具自己去读
        /// <c>CityRuntime.ActiveContext</c> 只覆盖面板驱动分支，自建路径下会拿到 null。
        /// 取不到时退回 0 —— 用户随即能在列表里看到高程并改掉，比不画这颗种子更好（有反馈胜过静默）。</para>
        /// </summary>
        float GroundHeightAt(int cx, int cz)
        {
            var g = _view != null ? _view.BoundGrid : (CityGrid?)null;
            if (g == null && _ctx != null) g = _ctx.Grid;
            if (g == null || g.Value.CellCount <= 0) return 0f;
            var f = _view != null ? _view.BoundField : null;
            if (f == null) return 0f;
            var w = g.Value.CellCenterWorld(cx, cz);
            return f.GetHeight(w.x, w.y);
        }

        /// <summary>
        /// 种子改动：<b>唯一</b>提交点 —— 存盘 + 把脏位记在<b>种子资产</b>上 + 立刻结算 + 重绘。
        /// <para>02 二次改版后不再调 <c>_terrain.MarkWaterChanged()</c>：撤销登记的对象是种子资产，
        /// 脏位必须落在同一个对象上才会被 Tracker 看见（09 H-9 的纪律）；
        /// 而旧代码这里同时 bump 实例与资产 ⇒ 白跑两次洪泛（用户反馈②里那类"重复计算"）。</para>
        /// <para>这里<b>不</b>手动 MarkDirty/Tick：脏区搬入与 Tick 由 <see cref="OnEditorUpdate"/> 的
        /// 既有链路负责，另开提交口会与自动跟随叠成双跑，还会绕过增量依据退化成全图。</para>
        /// </summary>
        private void TouchWaterAsset(in CityGrid grid, int cx, int cz)
        {
            var water = _terrain != null ? _terrain.Water : null;
            if (water == null) return;
            EditorUtility.SetDirty(water);
            AssetDatabase.SaveAssets();
            water.CommitChange(WaterNetworkAsset.SeedBounds(grid, cx, cz));
            UndoHistoryTracker.SettleNow();     // 离散改动：不等 250ms 合并窗口
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
        /// 实例与 Profile 绑定区（各步共用的前置条件，返回 null = 调用方必须早退）。
        /// <para>存在理由：②③都依赖 Profile 与实例，若不先绑定就什么都没得调；
        /// 但把绑定控件<b>只</b>留在①会让②③变成"空白面板 + 无从归因"（H-51 同族教训）。</para>
        /// </summary>
        private TerrainProfile DrawInstanceAndProfile()
        {
            if (_terrain == null) _terrain = FindTerrainInstance();
            if (_terrain == null)
            {
                EditorGUILayout.HelpBox("场景里没有 CityTerrain 实例：地形无法计算与显示。", MessageType.Info);
                if (GUILayout.Button("创建地形实例（含显示层）", GUILayout.Width(190f))) CreateTerrainInstance();
                return null;
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
                return null;
            }
            if (_view == null) _view = _terrain.GetComponent<CityTerrainView>();
            return profile;
        }

        /// <summary>
        /// 第①步「地图参数」：世界参数 + 地形噪声基底 + 城区范围。
        /// <para><b>压平内缘/外缘归本步</b>（用户反馈③的整改之一）：取证确认这两个比例在
        /// 全工程<b>唯一</b>的消费者是 S1a 的噪声幅度衰减（TerrainBaseStage 的 smoothstep），
        /// 与平整烘焙毫无关系。它们原先摆在"平整滤镜"里，是"改了半天画面不动"的直接来源。</para>
        /// </summary>
        private void DrawParamsSection()
        {
            EditorGUILayout.LabelField("① 地图参数（世界 · 噪声基底 · 城区范围）", EditorStyles.boldLabel);
            DrawProjectField();
            EditorGUILayout.Space(4f);

            var profile = DrawInstanceAndProfile();
            if (profile == null) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("地形噪声基底（S1a：山与起伏）", EditorStyles.boldLabel);
            var amp = EditorGUILayout.Slider("起伏幅度(m)", profile.BaseAmplitude, 0f, 60f);
            var wave = EditorGUILayout.Slider("低频波长(m)", profile.BaseWavelength, 60f, 2000f);
            var octs = EditorGUILayout.IntSlider("八度", profile.Octaves, 1, 8);
            var gn = EditorGUILayout.Slider("增益", profile.Gain, 0.05f, 0.95f);
            var lac = EditorGUILayout.Slider("倍频间隔", profile.Lacunarity, 1.05f, 4f);
            var warp = EditorGUILayout.Slider("域扭曲", profile.DomainWarpAmp, 0f, 1f);
            if (amp != profile.BaseAmplitude || wave != profile.BaseWavelength || octs != profile.Octaves
                || gn != profile.Gain || lac != profile.Lacunarity || warp != profile.DomainWarpAmp)
                profile.SetBaseShape(amp, wave, octs, gn, lac, warp);

            var flat = EditorGUILayout.Slider(
                new GUIContent("城内平整度(0平 1起伏)", "噪声在城市范围内被压平的比例（S1a 的衰减核，不是烘焙）。"),
                profile.UrbanFlatness, 0f, 1f);
            if (!Mathf.Approximately(flat, profile.UrbanFlatness)) profile.SetUrbanFlatness(flat);

            var lockDetail = EditorGUILayout.Toggle(
                new GUIContent("冻结细节(只看规划)", "锁住高频细节便于只看大体形（作用于 S1a 噪声，不是烘焙开关）。"),
                profile.RoughnessLock);
            if (lockDetail != profile.RoughnessLock) profile.SetRoughnessLock(lockDetail);

            // ── 城区范围：噪声压平的作用域 ──
            // 「生效值」必须显式呈现：城心/半径都走「锚点→Profile回退→图幅中心/0.45×图幅」三层解析，
            // 只摆输入框会让用户以为 0 就是「城在 (0,0)、半径 0」（上一轮「开平整没变化」的困惑正源于此）。
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("城区范围（噪声压平的作用域）", EditorStyles.boldLabel);
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

            var inE = EditorGUILayout.Slider(
                new GUIContent("压平内缘（u≤ 完全压平）", "u = 到城心的距离 / 城区半径。\n" +
                    "u ≤ 本值：噪声被完全压平。\n<b>这是 S1a 噪声的参数，与③的烘焙无关。</b>"),
                profile.FlatnessInnerEdge, 0.1f, 1.45f);
            var outE = EditorGUILayout.Slider(
                new GUIContent("放开外缘（u≥ 满幅野性）", "u ≥ 本值：噪声满幅野性。\n<b>同样属于 S1a。</b>"),
                profile.FlatnessOuterEdge, 0.2f, 2f);
            if (!Mathf.Approximately(inE, profile.FlatnessInnerEdge) || !Mathf.Approximately(outE, profile.FlatnessOuterEdge))
                profile.SetUrbanEdgeRatio(inE, outE);
            if (profile.FlatnessOuterEdge <= profile.FlatnessInnerEdge)
                EditorGUILayout.HelpBox("外缘必须大于内缘，否则过渡带消失、城界成为硬折线。", MessageType.Warning);
            EditorGUILayout.LabelField($"  换算：{effR * profile.FlatnessInnerEdge:0}m 内完全压平 · " +
                                      $"{effR * profile.FlatnessOuterEdge:0}m 外满幅野性", EditorStyles.miniLabel);
        }

        /// <summary>
        /// 第②步「雕山水」：左键接管切换 + 雕刻笔刷 + 水体种子。
        /// <para>工具接管原先挂在右上角（<c>ToolMode</c>），而右上角现在是分层统计 ——
        /// 所以这里给出显式的接管开关，读写仍在场景实例序列化真源上（H-50／D-34）。</para>
        /// </summary>
        private void DrawSculptSection()
        {
            EditorGUILayout.LabelField("② 雕山水（笔刷雕刻 · 水体种子）", EditorStyles.boldLabel);
            var profile = DrawInstanceAndProfile();
            if (profile == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                var sculpt = GUILayout.Toggle(_terrain.SculptToolActive,
                    new GUIContent("雕刻笔刷", "左键由山水笔刷接管（拖动落笔）。与「加水种子」互斥。"),
                    EditorStyles.toolbarButton);
                var water = GUILayout.Toggle(_terrain.WaterToolActive,
                    new GUIContent("加水种子", "左键由水工具接管（点一格加/删种子）。与「雕刻笔刷」互斥。"),
                    EditorStyles.toolbarButton);
                if (sculpt != _terrain.SculptToolActive || water != _terrain.WaterToolActive)
                {
                    // 互斥由写入侧保证（两个 Set 都会清对方）；这里按"最后被点的"决定归属
                    if (sculpt) _terrain.SetSculptToolActive(true);
                    else if (water) { _terrain.SetWaterToolActive(true); EnsureWaterAsset(); }
                    else { _terrain.SetSculptToolActive(false); _terrain.SetWaterToolActive(false); }
                    MarkTerrainSceneDirty();
                    _mode = _terrain.SculptToolActive ? ToolMode.TerrainSculpt
                          : _terrain.WaterToolActive ? ToolMode.RiverBrush : ToolMode.Inspect;
                }
            }
            if (!_terrain.SculptToolActive && !_terrain.WaterToolActive)
                EditorGUILayout.HelpBox("左键当前归编辑器默认工具：要雕山或加水请点上面对应的接管开关。",
                    MessageType.Info);

            EditorGUILayout.Space(4f);
            DrawSculptPanel();
            EditorGUILayout.Space(6f);
            DrawWaterPanel();
        }

        /// <summary>
        /// 第③步「平整出地表」：可平整对象 + 自然化 + 求解参数 + 烘焙。
        /// </summary>
        private void DrawFlattenSection()
        {
            EditorGUILayout.LabelField("③ 平整出地表（烘焙式，不实时计算）", EditorStyles.boldLabel);
            var profile = DrawInstanceAndProfile();
            if (profile == null) return;

            // ── 可平整对象（M2 代演品；S4 落地后由真实地块/路网供数）──
            EditorGUILayout.LabelField("可平整对象", EditorStyles.boldLabel);
            var auto = EditorGUILayout.Toggle(
                new GUIContent("自动试设地块", "以城心为锚铺 N×N 个试设地块当约束源。\n" +
                    "⚠️ 它只拿城区半径定街距、<b>不受半径裁剪</b>：N=8 时能铺满整张图，把山削成水平台阶。\n" +
                    "默认关闭；确需批量找平再开，并配合下面的陡坡保护。"),
                _terrain.AutoFlattenAtCenter);
            var tiles = EditorGUILayout.IntSlider("地块数(N×N)", _terrain.AutoTileCount, 1, 12);
            if (auto != _terrain.AutoFlattenAtCenter || tiles != _terrain.AutoTileCount)
            {
                _terrain.SetAutoFlatten(auto, tiles);
                MarkTerrainSceneDirty();
            }
            var zones = _terrain.ManualZones;
            EditorGUILayout.LabelField($"  人工平整区：{zones.Count} 个" +
                (zones.Count == 0 ? "（面板暂不提供圈定，可在实例 Inspector 上加）" : string.Empty),
                EditorStyles.miniLabel);

            // ── 自然化（D-41：用户原话「消除明显的手绘痕迹」）──
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("自然化（只软化人工层，不碰 S1a 噪声）", EditorStyles.boldLabel);
            var nr = EditorGUILayout.Slider("软化核半径(m)", profile.NaturalizeRadius, 4f, 400f);
            var na = EditorGUILayout.Slider("软化强度（1=折痕全消）", profile.NaturalizeAmount, 0f, 1f);
            if (!Mathf.Approximately(nr, profile.NaturalizeRadius) || !Mathf.Approximately(na, profile.NaturalizeAmount))
                profile.SetNaturalize(nr, na);

            // ── 平整约束求解 ──
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("约束求解", EditorStyles.boldLabel);
            var slope = EditorGUILayout.Slider(
                new GUIContent("地块最大坡度(°)", "拟合面坡度超过它 ⇒ 退化为水平中位数面。"),
                profile.MaxParcelSlope, 0.5f, 20f);
            var freeW = EditorGUILayout.Slider(
                new GUIContent("自由权重(保留自然度)", "融合式里原始高程的权重：越大越贴原样、越小越贴目标平面。"),
                profile.FreeWeight, 0f, 3f);
            var inner = EditorGUILayout.Slider("核心内切(m)", profile.ConstraintInner, 0f, 40f);
            var extend = EditorGUILayout.Slider("过渡带外延(m)", profile.ConstraintExtend, 0.5f, 40f);
            if (slope != profile.MaxParcelSlope || !Mathf.Approximately(freeW, profile.FreeWeight)
                || !Mathf.Approximately(inner, profile.ConstraintInner)
                || !Mathf.Approximately(extend, profile.ConstraintExtend))
                profile.SetFlattenSolve(slope, freeW, inner, extend, profile.TargetPlaneMaxSlope);

            var steep = EditorGUILayout.Slider(
                new GUIContent("陡坡保护阈值(°)", "原始地面坡度超过本值的格<b>不参与压平</b>（D-32 的退让档）。\n" +
                    "这是「不把山削成水平台阶」的关键保护；调大＝更敢削山，调小＝更保守。"),
                profile.SteepProtectSlope, 0f, 60f);
            if (!Mathf.Approximately(steep, profile.SteepProtectSlope)) profile.SetSteepProtect(steep);

            var keepHand = EditorGUILayout.Toggle(
                new GUIContent("保留手绘格", "被雕刻笔刷碰过的格<b>不被滤镜改动</b>（人工优先于自动的结构化落实）。\n" +
                    "关掉才允许自动层覆盖手绘 —— 默认必须开。"),
                _terrain.ProtectHandCarved);
            if (keepHand != _terrain.ProtectHandCarved)
            {
                _terrain.SetProtectHandCarved(keepHand);
                MarkTerrainSceneDirty();
            }

            // ── 烘焙（D-41：用户裁定「不要实时算，点烘焙才算」）──
            EditorGUILayout.Space(4f);
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

            var flattenOn = EditorGUILayout.ToggleLeft(
                new GUIContent("叠加已烘焙的平整层（关＝只显示手绘意图地表）",
                    "开关只做「叠 / 不叠」，不触发任何求解。"),
                _terrain.FlattenEnabled);
            if (flattenOn != _terrain.FlattenEnabled)
            {
                _terrain.SetFlattenEnabled(flattenOn);
                MarkTerrainSceneDirty();
            }

            if (bakeState == CityTerrain.BakeState.Never)
                EditorGUILayout.HelpBox("尚未烘焙过 ⇒ 开关开/关看起来一样（没有可叠加的内容）。" +
                    "先勾「自动试设地块」或添加人工平整区，再点上面「烘焙平整」。", MessageType.Info);
            else if (bakeState == CityTerrain.BakeState.Stale)
                EditorGUILayout.HelpBox("手绘或参数在烘焙之后又改过 ⇒ 画面挂的是旧滤镜。点「重新烘焙」刷新。",
                    MessageType.Warning);
            if (!string.IsNullOrEmpty(_terrain.FlattenHint))
                EditorGUILayout.HelpBox(_terrain.FlattenHint, MessageType.Warning);

            // 烘焙反馈（用户反馈③「简直是灾难」的整改：改了多少必须报出来，不能只靠肉眼发现山没了）
            if (_terrain.LastConstraints > 0 || _terrain.LastCells > 0)
                EditorGUILayout.LabelField(
                    $"上次烘焙：约束 {_terrain.LastConstraints} 条 · 改动 {_terrain.LastCells} 格 · " +
                    $"跳过 {_terrain.LastRejectedConstraints} 条 · 最大削平 {_terrain.LastBakeMaxDelta:0.#}m",
                    EditorStyles.miniLabel);
        }

        /// <summary>第④步：路网与建筑尚未接线，如实呈现（灰置按钮进不来，这里兜底说明）。</summary>
        private void DrawRoadsPlaceholder()
        {
            EditorGUILayout.LabelField("④ 路网 · 场域 · 建筑", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "尚未实现（下一里程碑 S3 路网）。它也是平整的前置：设计文档要求的约束源是" +
                "真实地块与道路带（「路横平、纵缓坡」），现在的「自动试设地块」只是代演品。",
                MessageType.Info);
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
            var stage = new FlattenSolveStage(prof, _terrain);
            var res = stage.Bake(_ctx, filter);
            if (!res.Success) { _lastRunLine = "烘焙失败：" + res.Message; return; }

            EditorUtility.SetDirty(filter);
            AssetDatabase.SaveAssets();
            _terrain.SetFlattenFilter(filter);          // 让画面立刻重放新滤镜（只叠加，不再求解）
            _terrain.RecordBake(_project);              // 快照版本+手绘序列 ⇒ 之后改手绘/参数才判过期
            Undo.RecordObject(_terrain, "烘焙平整");
            EditorUtility.SetDirty(_terrain);

            // ★ 反馈（用户反馈③「简直是灾难」的整改核心）：改了多少、保住了多少，必须当场报出来。
            //   原先这里只写"约束 N 条 / 写入 M 格"，用户看不出它把山削了 60m，只能靠肉眼发现"山没了"。
            var ps = stage.LastProtectStats;
            _terrain.ReportBakeAmplitude(stage.LastMaxDelta);
            _lastRunLine = $"烘焙完成：改动 {res.CellsTouched} 格 · 最大高差 {stage.LastMaxDelta:0.#}m · " +
                           $"保护跳过 陡坡 {ps.steep} / 手绘 {ps.hand} / 水体 {ps.water} 格" +
                           (res.Warnings > 0 ? $" · 作废约束 {res.Warnings} 条" : string.Empty);
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
