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
            _runner = new PipelineRunner();
            EditorApplication.update += OnEditorUpdate;
        }

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
            if (profile != null)
            {
                _runner.Register(new TerrainBaseStage(profile, _project));
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
            if (_ctx == null || _runner == null) return;

            UndoHistoryTracker.Scan();
            DetectDirectProfileEdit();                  // 第二检：Inspector 直改 SO 字段不 bump 序列号

            if (!_autoFollow) return;                 // 关跟随：脏集合继续挂起，面板显示"未生效"
            if (!UndoHistoryTracker.ReadyToApply) return;

            var dirty = UndoHistoryTracker.ConsumeDirty();
            if (dirty == DirtyBits.None) return;

            SyncStages();                             // Profile 可能刚被换/清空
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
            DrawProjectField();
            EditorGUILayout.Space(4f);
            DrawTerrainProfile();
            EditorGUILayout.Space(4f);
            DrawStageTree();
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(_statusLine, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(_lastRunLine, EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
        }

        private void DrawModeSelector()
        {
            foreach (ToolMode m in Enum.GetValues(typeof(ToolMode)))
            {
                var on = _mode == m;
                var next = GUILayout.Toggle(on, Label(m), EditorStyles.toolbarButton);
                if (next && !on) _mode = m;      // 单选：切换即互斥（00 §5.3）
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

        private void DrawProjectField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var next = (CityProject)EditorGUILayout.ObjectField("城市项目", _project,
                    typeof(CityProject), false);
                if (!ReferenceEquals(next, _project)) Bind(next);
                if (GUILayout.Button("创建", GUILayout.Width(50f))) CreateProject();
            }
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

            // ── 自动试设地块（M2 起用于验证约束；M5 后由真实路网供数）──
            var auto = EditorGUILayout.Toggle("自动试设地块", _terrain.AutoFlattenAtCenter);
            var tiles = EditorGUILayout.IntSlider("地块数(N×N)", _terrain.AutoTileCount, 1, 12);
            if (auto != _terrain.AutoFlattenAtCenter || tiles != _terrain.AutoTileCount)
                _terrain.SetAutoFlatten(auto, tiles);

            var lockDetail = EditorGUILayout.Toggle("冻结细节(只看规划)", profile.RoughnessLock);
            if (lockDetail != profile.RoughnessLock) profile.SetRoughnessLock(lockDetail);

            if (_terrain.LastConstraints > 0)
                EditorGUILayout.LabelField($"S1b：约束 {_terrain.LastConstraints} 条 · 改动 {_terrain.LastCells} 格" +
                    (_terrain.LastRejectedConstraints > 0 ? $" · 跳过 {_terrain.LastRejectedConstraints}" : string.Empty),
                    EditorStyles.miniLabel);
        }

        /// <summary>创建地形实例 + 显示宿主（派生 View 与意图组件同物体，用户不必手动挂）。</summary>
        private void CreateTerrainInstance()
        {
            var go = new GameObject("City Terrain");
            var terr = go.AddComponent<CityTerrain>();
            go.AddComponent<CityTerrainView>();
            if (_project != null) terr.SetProfile(FindOrCreateProfile());
            _terrain = terr;
            _stageKey = 0;
            SyncStages();
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

        /// <summary>找同名 Profile 资产；没有则在项目资产旁建一个（避免每次弹保存框打断手感）。</summary>
        private TerrainProfile FindOrCreateProfile()
        {
            var guids = AssetDatabase.FindAssets("t:TerrainProfile");
            if (guids != null && guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<TerrainProfile>(AssetDatabase.GUIDToAssetPath(guids[0]));

            var dir = "Assets/Collin";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Collin");
            var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/TerrainProfile.asset");
            var asset = CreateInstance<TerrainProfile>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

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
            if (_ctx == null) { EditorUtility.DisplayDialog("CityMap", "请先绑定城市项目", "好"); return; }
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
