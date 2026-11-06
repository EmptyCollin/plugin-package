using Collin.Core;
using Collin.Core.Stealth;
using UnityEditor;
using UnityEngine;

namespace Collin.StealthPlanner.EditorTooling
{
    /// <summary>
    /// <b>相位时间轴窗口</b>（M2 = 静态设计 1 的可视化面）。
    /// <para>一条可拖动的 t 轴：把 A 包的 NPC 摆到该时刻（<see cref="IStealthScrubDriver.ScrubTo"/>），
    /// 同时在 Scene 视图叠三层热区——危险格、本桶可达、累计可达。三层的格子都来自
    /// 已烘的 <see cref="VisibilityTable"/>（经 <see cref="HeatmapGrid"/> 合并），
    /// <b>UI 一律不现算可见性</b>（DESIGN §4 判定唯一口径红线）。</para>
    /// <para><b>视图态刻意是窗口实例字段、不走静态</b>：窗口关闭即退订 SceneView 回调、
    /// 热区随之消失，不会污染其他会话（工程既有教训：工具态禁存 EditorWindow 静态字段）。</para>
    /// </summary>
    internal sealed class StealthTimelineWindow : EditorWindow
    {
        const string MenuPath = "Tools/Collin/Stealth Planner/相位时间轴";
        const string SampleDir = "Packages/com.collin.stealthplanner/Sample";
        const string TablePath = SampleDir + "/VisibilityTable.asset";
        const string SettingsPath = SampleDir + "/PlannerBakeSettings.asset";
        // 静态设计 4 导出目标：JSON 可 diff/版本化、SO 可进 Inspector（D-07 双形态）。
        // 固定包内路径而非弹保存框——SaveFilePanel 同样是模态窗口，一样卡指令通道。
        const string DatasetJsonPath = SampleDir + "/WaypointDataset.json";
        const string DatasetAssetPath = SampleDir + "/PlayerRouteDataset.asset";

        /// <summary>可达传播用的人行速度（米/秒）：示例场景同量级。做成面板字段供调参。</summary>
        const float DefaultWalkSpeed = 3.2f;

        [SerializeField] VisibilityTableAsset _tableAsset;
        [SerializeField] PlannerBakeSettings _settings;
        [SerializeField] Vector3 _startWorld = new Vector3(-24f, 0f, -24f);
        [SerializeField] Vector3 _goalWorld = new Vector3(24f, 0f, 24f);
        [SerializeField, Min(0.5f)] float _walkSpeed = DefaultWalkSpeed;
        [SerializeField] int _buckets = 24;
        [SerializeField] float _t;
        [SerializeField] bool _autoPlay;
        [SerializeField] bool _showDanger = true, _showReach = true, _showCumulative = true;
        [SerializeField] bool _showLanes = true;
        [SerializeField, Range(1, 16)] int _maxRoutes = 5;

        // 派生态（不进序列化，也不跨窗口共享）
        VisibilityTable _table;
        PhaseReachability.Field _field;
        byte[] _dangerMask = System.Array.Empty<byte>();
        byte[] _noData = System.Array.Empty<byte>();
        byte[] _cumBuf = System.Array.Empty<byte>();
        Mesh _meshDanger, _meshReach, _meshCum;
        Material _matDanger, _matReach, _matCum;
        int _startCell = -1;
        double _lastTick;
        string _status = "未载入表";
        string _warning;

        // 静态设计 2/3 的求解结果（点「求解可行性」后写入，纯文本展示，不驱动热区）
        string _solveSummary;
        // 静态设计 4：前 M 条短解的结果文本（点「求前 M 条短解」后写入）
        string _routesSummary;
        // 静态设计 4：导出结果文本（点「导出为路点数据集」后写入；非模态——§2.1.5 禁阻塞弹窗）
        string _exportSummary;
        // U-2 / D-13：建议参考相位时长的读数（Reload 时随表刷新，纯派生态、不序列化）。
        // 只读展示 + 「一键填入」由用户点击触发 ⇒ 算法永不自动改写手配的 P。
        PhaseReference.Result _phaseRef;
        readonly System.Collections.Generic.List<PhaseKShortest.Route> _routeBuf
            = new System.Collections.Generic.List<PhaseKShortest.Route>();

        /// <summary>每片「自身各桶看见多少格」的统计（只在表实例变化时重算，不每帧重扫）。</summary>
        int[][] _laneVisible;
        VisibilityTable _laneForTable;
        static GUIStyle _sRightMini;

        [MenuItem(MenuPath)]
        static void Open()
        {
            var w = GetWindow<StealthTimelineWindow>();
            w.titleContent = new GUIContent("相位时间轴");
            w.minSize = new Vector2(420f, 300f);
        }

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update += OnEditorUpdate;
            if (_tableAsset == null)
                _tableAsset = AssetDatabase.LoadAssetAtPath<VisibilityTableAsset>(TablePath);
            if (_settings == null)
                _settings = AssetDatabase.LoadAssetAtPath<PlannerBakeSettings>(SettingsPath);
            Reload();
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.update -= OnEditorUpdate;
            ReleaseGfx();
        }

        float Timestep => _table != null ? _table.TimeStep : 0.5f;
        int CurBucket => _table == null ? 0
            : Mathf.Clamp(Mathf.FloorToInt(_t / Mathf.Max(0.001f, Timestep)), 0, Mathf.Max(0, _buckets - 1));

        // ── UI ────────────────────────────────────────────────
        void OnGUI()
        {
            EditorGUILayout.LabelField("数据源", EditorStyles.boldLabel);
            _tableAsset = (VisibilityTableAsset)EditorGUILayout.ObjectField(
                "可见性表", _tableAsset, typeof(VisibilityTableAsset), false);
            _settings = (PlannerBakeSettings)EditorGUILayout.ObjectField(
                "烘焙设置", _settings, typeof(PlannerBakeSettings), false);

            if (_table == null)
            {
                EditorGUILayout.HelpBox(
                    "表不可用。先用「Tools ▸ Collin ▸ Stealth Planner ▸ 烘焙可见性表」生成。\n"
                    + "本窗口只读表、不现算可见性（判定口径唯一）。", MessageType.Info);
                if (GUILayout.Button("重新载入表")) Reload();
                return;
            }

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            _buckets = Mathf.Clamp(EditorGUILayout.IntField("时间桶数", _buckets), 1, 4096);
            var span = Mathf.Max(0.001f, (_buckets - 1) * Timestep);
            _t = EditorGUILayout.Slider("t（秒）", Mathf.Min(_t, span), 0f, span);
            _startWorld = EditorGUILayout.Vector3Field("可达起点（世界）", _startWorld);
            _walkSpeed = EditorGUILayout.FloatField("人速（m/s）", _walkSpeed);
            _autoPlay = EditorGUILayout.Toggle("自动播放", _autoPlay);

            EditorGUILayout.BeginHorizontal();
            _showDanger = EditorGUILayout.ToggleLeft("危险格", _showDanger);
            _showReach = EditorGUILayout.ToggleLeft("本桶可达", _showReach);
            _showCumulative = EditorGUILayout.ToggleLeft("累计可达", _showCumulative);
            _showLanes = EditorGUILayout.ToggleLeft("哨兵泳道", _showLanes);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck() && !_autoPlay) Recompute();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重算")) { _startCell = -1; Recompute(); }
            if (GUILayout.Button("t=0")) { _t = 0f; Recompute(); }
            if (GUILayout.Button("用设置默认桶数"))
            {
                _buckets = _settings != null ? _settings.BucketsPerPhase : _buckets;
                Recompute();
            }
            EditorGUILayout.EndHorizontal();

            // U-2 / D-13 / §6.7：建议参考相位时长。**只读展示**，不自动回写手配的 P；
            // 「一键填入」必须由用户点击触发（自动改写=偷改策划的设计意图，正是 D-13 否决的方案）。
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("建议参考相位时长（决议 D-13 / §6.7）", EditorStyles.boldLabel);
            if (!_phaseRef.Valid)
            {
                EditorGUILayout.LabelField(_phaseRef.Note, EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                var pv = _settings != null ? _settings.PhaseWindow : 0f;
                EditorGUILayout.LabelField("建议值（秒）", _phaseRef.ReferenceSeconds.ToString("0.##")
                    + (_phaseRef.FellBack ? "（近似：LCM 爆炸，已兜底 max(c_i)×2）"
                                          : "（严格：LCM = " + _phaseRef.AlignedBuckets + " 桶）"));
                EditorGUILayout.LabelField("当前手配 P（秒）", pv.ToString("0.##")
                    + "  ·  相位轮数 N=" + (_settings != null ? _settings.MaxPhases.ToString() : "-"));
                if (_phaseRef.FellBack) EditorGUILayout.HelpBox(_phaseRef.Note, MessageType.Warning);
                else EditorGUILayout.LabelField(_phaseRef.Note, EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUI.DisabledScope(_settings == null))
                {
                    if (GUILayout.Button("一键填入为手配 P（仅此按钮会改设置资产）"))
                    {
                        Undo.RecordObject(_settings, "填入建议相位时长");
                        _settings.SetPhaseWindow(_phaseRef.ReferenceSeconds);
                        EditorUtility.SetDirty(_settings);
                        _buckets = _settings.BucketsPerPhase;
                        Recompute();
                        Debug.Log("[Planner] 已按用户点击把相位窗 P 填入为 "
                            + _phaseRef.ReferenceSeconds.ToString("0.##") + "s（建议值="
                            + (_phaseRef.FellBack ? "兜底近似" : "严格 LCM") + "）");
                    }
                }
            }

            // 静态设计 2/3/4：可行性求解 + 前 M 条短解。与热区共用同一张表与人速，结果只以文本呈现，
            // 不驱动逐桶热区（那会再引入一套「什么时候算」的口径）。
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("可行性求解（静态设计 2 / 3 / 4）", EditorStyles.boldLabel);
            _goalWorld = EditorGUILayout.Vector3Field("撤离点（世界）", _goalWorld);
            using (new EditorGUI.DisabledScope(_table == null))
            {
                if (GUILayout.Button("求解有无可行解")) SolveFeasibility();
            }
            if (!string.IsNullOrEmpty(_solveSummary))
                EditorGUILayout.LabelField(_solveSummary, EditorStyles.wordWrappedMiniLabel);

            // 静态设计 4：前 M 条边不相交短解（口径见 PhaseKShortest，非严格 Yen）
            _maxRoutes = Mathf.Clamp(EditorGUILayout.IntField("短解条数上限 M", _maxRoutes), 1, 16);
            using (new EditorGUI.DisabledScope(_table == null))
            {
                if (GUILayout.Button("求前 M 条短解")) SolveRoutes();
            }
            if (!string.IsNullOrEmpty(_routesSummary))
                EditorGUILayout.LabelField(_routesSummary, EditorStyles.wordWrappedMiniLabel);

            // 静态设计 4 收口：把求得的 M 条动线导出为路点数据集（D-07 双形态）。
            // 刻意「非模态」：AGENTS §2.1.5 禁 DisplayDialog/SaveFilePanel（阻塞主线程=卡死指令通道），
            // 故固定导出到包内路径 + 按钮下方文本 + 日志读数三处确认，不弹任何对话框。
            using (new EditorGUI.DisabledScope(_table == null || _routeBuf.Count == 0))
            {
                if (GUILayout.Button("导出为路点数据集（JSON + SO）")) ExportRoutesAsDataset();
            }
            if (!string.IsNullOrEmpty(_exportSummary))
                EditorGUILayout.LabelField(_exportSummary, EditorStyles.wordWrappedMiniLabel);

            // 当前桶号在此统一取一次（泳道与状态行共用，避免两套 CurBucket 读数不同步）
            var bucket = CurBucket;

            EditorGUILayout.Space();
            if (_showLanes) DrawGuardLanes(bucket);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("状态", _status);
            if (!string.IsNullOrEmpty(_warning)) EditorGUILayout.HelpBox(_warning, MessageType.Warning);

            // 本桶/累计计数直接读 Field（不另算一套）
            var reachNow = _field != null ? _field.ReachableCountInBucket(bucket) : 0;
            var cum = _cumBuf.Length == 0 ? 0 : CountOnes(_cumBuf);
            EditorGUILayout.LabelField("本桶 " + bucket + " · 危险 "
                + CountOnes(_dangerMask) + " 格 · 本桶可达 " + reachNow + " · 累计可达 " + cum);
        }

        static int CountOnes(byte[] a)
        {
            if (a == null) return 0;
            var c = 0;
            for (var i = 0; i < a.Length; i++) if (a[i] != 0) c++;
            return c;
        }

        /// <summary>
        /// 逐哨兵画一条泳道（静态设计 1 的「每 NPC」一行）：横条 = 该哨兵<b>自身周期</b>的逐桶
        /// 可见格数（深浅），竖白线 = 当前 t 落在其周期内的那一桶；行尾给此刻位姿。
        /// <para>坐标一律取契约 <c>TryPoseAtWorld</c>（与 <c>ScrubTo</c> 同一求值路径），
        /// 不在本包重算折线；可见强度取表的片内桶统计，不自己扫位图。</para>
        /// </summary>
        void DrawGuardLanes(int bucket)
        {
            if (_table == null || _table.Slices.Count == 0) return;
            EnsureLaneStats();

            var tSec = bucket * Timestep;
            var patrol = Services.TryGet<IPatrolWorld>(out var pw) ? pw : null;

            EditorGUILayout.LabelField("哨兵泳道（横条=该桶可见格数，白线=当前 t）", EditorStyles.boldLabel);
            if (_sRightMini == null)
            {
                _sRightMini = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    clipping = TextClipping.Clip
                };
            }

            for (var s = 0; s < _table.Slices.Count; s++)
            {
                var sl = _table.Slices[s];
                var stats = s < _laneVisible.Length ? _laneVisible[s] : null;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(string.IsNullOrEmpty(sl.displayName)
                    ? ("#" + sl.actorId) : sl.displayName, GUILayout.Width(110f));

                var box = GUILayoutUtility.GetRect(60f, 14f, GUILayout.ExpandWidth(true));
                GUI.Box(box, GUIContent.none);
                if (stats != null && stats.Length > 0)
                {
                    var max = 1;
                    for (var i = 0; i < stats.Length; i++) if (stats[i] > max) max = stats[i];
                    var w = box.width / stats.Length;
                    for (var i = 0; i < stats.Length; i++)
                    {
                        var k = (float)stats[i] / max;
                        // 0 格也要画出色块边界，否则「全暗」与「没有这一桶」看起来一样
                        var col = stats[i] == 0
                            ? new Color(0.12f, 0.12f, 0.12f, 0.55f)
                            : Color.Lerp(new Color(0.95f, 0.75f, 0.25f, 0.35f),
                                         new Color(0.92f, 0.16f, 0.12f, 0.90f), k);
                        EditorGUI.DrawRect(new Rect(box.x + i * w, box.y, Mathf.Max(1f, w - 0.5f), box.height), col);
                    }
                    // 当前 t 用 BucketOf 落到该片自身周期（周期不整除 Δt 时不能按窗口桶号直接切）
                    var cur = VisibilityTable.BucketOf(tSec, sl.cycleSeconds, sl.buckets);
                    var cx = box.x + (cur + 0.5f) * (box.width / sl.buckets);
                    EditorGUI.DrawRect(new Rect(cx - 1f, box.y - 1f, 2f, box.height + 2f), Color.white);
                }

                var tail = "周期 " + sl.cycleSeconds.ToString("0.0") + "s";
                Vector3 wp;
                float yaw;
                if (patrol != null && sl.actorId >= 0
                    && patrol.TryPoseAtWorld(sl.actorId, tSec, out wp, out yaw))
                {
                    var inGrid = _table.TryCellIndex(wp, out var cell);
                    tail = "(" + wp.x.ToString("0.0") + ", " + wp.z.ToString("0.0") + ") "
                           + yaw.ToString("0") + "° "
                           + (inGrid ? "格" + cell : "格外")
                           + " · " + tail;
                }
                GUILayout.Label(tail, _sRightMini, GUILayout.Width(190f));
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>表实例变化时才重扫逐桶统计：每帧重扫 = 哨兵数 × 桶数 × 格数的全表扫描。</summary>
        void EnsureLaneStats()
        {
            if (!ReferenceEquals(_laneForTable, _table) || _laneVisible == null)
            {
                var n = _table.Slices.Count;
                _laneVisible = new int[n][];
                for (var s = 0; s < n; s++)
                {
                    var sl = _table.Slices[s];
                    var row = new int[Mathf.Max(1, sl.buckets)];
                    for (var b = 0; b < row.Length; b++) row[b] = _table.VisibleCellCountInSliceBucket(s, b);
                    _laneVisible[s] = row;
                }
                _laneForTable = _table;
            }
        }

        void Reload()
        {
            _table = _tableAsset != null ? _tableAsset.Table : null;
            _startCell = -1;
            if (_table == null)
            {
                // 必须连旧热区一起清掉：否则「表不可用」的面板上还留着上一份表算出的
                // 网格与计数（实测：先烘好表再打开窗口、然后把表字段清空，旧热区不会消失）。
                // SceneView 那侧靠 _table==null 早退不再绘制，但对象泄漏与陈旧读数是真的。
                ReleaseGfx();
                _field = null;
                _dangerMask = System.Array.Empty<byte>();
                _noData = System.Array.Empty<byte>();
                _cumBuf = System.Array.Empty<byte>();
                _laneVisible = null;
                _laneForTable = null;
                _status = "表未载入或版本不符";
                return;
            }

            if (_settings != null && _settings.BucketsPerPhase > 0) _buckets = _settings.BucketsPerPhase;

            // U-2 / §6.7：参考相位时长随表刷新。输入=各哨兵**自身周期**（与烘焙填桶同一份数据），
            // 时间步长取表的 Δt 而非设置资产的 Δt —— 表是实际烘焙出来的东西，设置可能已被改过未重烘，
            // 用设置值算会让建议值与表对不上（同一份口径纪律，D-22 同族）。
            var cycles = new float[_table.Slices.Count];
            for (var s = 0; s < cycles.Length; s++) cycles[s] = _table.Slices[s].cycleSeconds;
            _phaseRef = PhaseReference.Suggest(cycles, _table.TimeStep);

            // 两类来源合并在这份名单里：①位姿不可解析被整片跳过 ②跑完了但整条周期零可见。
            // 二者在表侧语义相同 —— 该哨兵的视野在表里等于不存在 ⇒ 它的视野区会被画成安全（谎报安全）。
            var unknown = _table.UnknownGuardNames;
            _warning = unknown != null && unknown.Count > 0
                ? unknown.Count + " 名哨兵的视野在这张表里等于不存在（未参与烘焙，或整条周期零可见），"
                  + "热区的「安全」对它们偏乐观，请核对场景后重烘："
                  + string.Join("、", unknown)
                : null;
            Recompute();
        }

        // ── 重算：推 NPC + 三层掩码 + 合并网格 ─────────────────
        void Recompute()
        {
            if (_table == null) { _status = "无表"; return; }

            var n = _table.CellCount;
            if (_dangerMask.Length != n) { _dangerMask = new byte[n]; _noData = new byte[n]; _cumBuf = new byte[n]; }
            // 每次重算先清零累计：任何早退路径（起点越界／传播失败）都不该留下上一次 t 的热区
            for (var i = 0; i < n; i++) _cumBuf[i] = 0;

            var bucket = CurBucket;
            var tBucket = bucket * Timestep;

            // ① 推 A 包位姿：让 Scene 里的 NPC 站到 t 时刻（静态设计 1 的「每 NPC 位置」）
            var driver = Services.TryGet<IStealthScrubDriver>(out var d) ? d : null;
            if (driver != null) driver.ScrubTo(tBucket);

            // ② 危险 = 可见 或 无数据（墙）。与传播同一口径：热区不会画出「可穿的墙」
            if (!_table.BuildDangerMask(tBucket, _dangerMask)) { _status = "掩码长度不足"; return; }
            if (_table.HasFootprint)
            {
                _table.BuildNoDataMask(_noData);
                for (var i = 0; i < n; i++) if (_noData[i] != 0) _dangerMask[i] = 1;
            }

            // ③ 可达传播：本桶读 Field.IsReachable，累计读 Field.FillReachableUpTo（都是 Field 的公开查询）
            // 必须每次按 _startWorld 重算格号：缓存 _startCell 会让「改了起点输入框」毫无反应
            // （实测就是这样报着「出发格已被看见」却怎么拖都不变），违反 §0.1 所见即所得。
            if (!_table.TryCellIndex(_startWorld, out _startCell))
            {
                _startCell = -1;
                _status = "起点落在网格外（世界坐标 " + _startWorld.ToString("0.0") + "）";
                _field = null;
                RebuildMeshes(bucket, null);
                SceneView.RepaintAll();
                return;
            }
            var step = PhaseReachability.StepCellsFor(_walkSpeed, Timestep, _table.CellSize);
            _field = PhaseReachability.Compute(_table, _startCell, _buckets, step, out var fail);
            if (!fail.ok)
            {
                _status = "传播失败：" + fail.reason;
                _field = null;          // 失败时不留「看着像半张表」的空场（会被状态行继续读）
                RebuildMeshes(bucket, null);
                SceneView.RepaintAll();
                return;
            }

            // 累计层 = 「截至本桶（含）已能到达」，不是整窗并集——后者拖 t 时恒定，看着像坏了
            _field.FillReachableUpTo(bucket, _cumBuf);
            var reachMask = CurrentBucketMask(bucket, n);

            RebuildMeshes(bucket, reachMask);
            _status = "t=" + tBucket.ToString("0.00") + "s（桶 " + bucket + "）步长 "
                      + step + " 格" + (driver == null ? " · 无回溯服务，NPC 未挪动" : "");
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 点「求解有无可行解」：复用同一张表 + 面板人速，走 <see cref="PhaseSolver"/>。
        /// <para>结果只写成文本，<b>不驱动逐桶热区</b>——求解用的是「视界=N 相位」的完整 horizon，
        /// 与时间轴滑条的 <c>_buckets</c> 是两个量，混在一起会让热区口径分裂。</para>
        /// </summary>
        void SolveFeasibility()
        {
            if (_table == null) { _solveSummary = "无表，先烘焙可见性表"; return; }
            if (_settings == null)
                _solveSummary = "缺烘焙设置：视界 N×⌈P/Δt⌉ 与 Top-K 无从取得，请先指定烘焙设置资产";
            else
            {
                var r = PhaseSolver.Solve(_table, _settings, _startWorld, _goalWorld, _walkSpeed,
                                          out var fail);
                if (!fail.ok)
                {
                    _solveSummary = "求解失败：" + fail.reason;
                }
                else if (r.Feasible)
                {
                    _solveSummary = "✅ 有解：最早 t=" + r.EarliestArrivalSeconds.ToString("0.0")
                        + "s（桶 " + r.ArrivalBucket + "）抵达撤离点；视界 " + r.HorizonBuckets
                        + " 桶、途中可达 " + r.CellsReached + " 格";
                }
                else
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("⛔ 视界 ").Append(r.HorizonBuckets).Append(" 桶内无可行解（途中可达 ")
                      .Append(r.CellsReached).Append(" 格）。距撤离点最近的可藏点 ")
                      .Append(r.FallbackTopK.Count).Append(" 个：");
                    for (var i = 0; i < r.FallbackTopK.Count; i++)
                    {
                        var cd = r.FallbackTopK[i];
                        sb.Append("\n  #").Append(i + 1).Append(" (")
                          .Append(cd.world.x.ToString("0.0")).Append(", ").Append(cd.world.z.ToString("0.0"))
                          .Append(") 距撤离点 ").Append(cd.DistCellsToGoal).Append(" 格 · 最早 ")
                          .Append(cd.EarliestSeconds.ToString("0.0")).Append("s");
                    }
                    if (r.FallbackTopK.Count == 0)
                        sb.Append("\n  （无——被墙隔断，玩家所在的连通分量根本到不了撤离点那侧）");
                    _solveSummary = sb.ToString();
                }
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// 点「求前 M 条短解」：复用同一张表 + 面板人速 + 视界，走 <see cref="PhaseKShortest"/>。
        /// <para>首条路线即<b>最早到达解</b>（单位代价分层图上第一个把撤离点纳入前沿的层），
        /// 与「求解有无可行解」同一 horizon、同一判据 ⇒ 首条到达桶必然等于可行性解，
        /// 无需在此再跑一遍 <see cref="PhaseSolver"/>（那会把整视界重扫一次）。</para>
        /// <para>结果只写成文本，不驱动逐桶热区（口径理由同 <see cref="SolveFeasibility"/>）。</para>
        /// </summary>
        void SolveRoutes()
        {
            if (_table == null) { _routesSummary = "无表，先烘焙可见性表"; return; }
            if (_settings == null)
            { _routesSummary = "缺烘焙设置：视界 N×⌈P/Δt⌉ 无从取得，请先指定烘焙设置资产"; return; }

            var horizon = PhaseSolver.Horizon(_settings, out var whyH);
            if (horizon < 0) { _routesSummary = "视界非法：" + whyH; return; }

            if (!_table.TryCellIndex(_startWorld, out var startCell))
            { _routesSummary = "起点落在网格外：" + _startWorld.ToString("0.0"); return; }
            if (!_table.TryCellIndex(_goalWorld, out var goalCell))
            { _routesSummary = "撤离点落在网格外：" + _goalWorld.ToString("0.0"); return; }

            var step = PhaseReachability.StepCellsFor(_walkSpeed, _table.TimeStep, _table.CellSize);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            PhaseKShortest.FindRoutes(_table, startCell, goalCell, horizon, step,
                                      _maxRoutes, _routeBuf, out var fail);
            var ms = sw.ElapsedMilliseconds;

            if (!fail.ok) { _routesSummary = "搜索失败：" + fail.reason; return; }
            if (_routeBuf.Count == 0)
            { _routesSummary = "视界 " + horizon + " 桶内无任何可达撤离点的边不相交动线（无解；用「求解有无可行解」看最近可藏点）。耗时 " + ms + "ms"; return; }

            var sb = new System.Text.StringBuilder();
            sb.Append("✅ 边不相交短解 ").Append(_routeBuf.Count).Append(" 条（≤M ")
              .Append(_maxRoutes).Append("）· 视界 ").Append(horizon).Append(" 桶 · 步长 ")
              .Append(step).Append(" 格 · 耗时 ").Append(ms).Append("ms\n")
              .Append("  口径：非严格 Yen K-shortest，而是逐条锁移动+等待边的边不相交真实动线；穷尽时少于 M 条不凑数。\n");
            for (var i = 0; i < _routeBuf.Count; i++)
            {
                var r = _routeBuf[i];
                // 等待拍数 = 层间转移总数 − 移动跳数（原地等待不产生新格）
                var waits = (r.CellsPerLayer.Count - 1) - r.MoveCount;
                var s1 = _table.CellCenterWorld(r.CellsPerLayer[r.CellsPerLayer.Count - 1]);
                sb.Append("  #").Append(i + 1).Append("  t=")
                  .Append(r.ArrivalSeconds.ToString("0.0")).Append("s（桶 ").Append(r.ArrivalBucket)
                  .Append("）移动 ").Append(r.MoveCount).Append(" 跳、原地等待 ").Append(waits)
                  .Append(" 拍 · 末站(").Append(s1.x.ToString("0.0")).Append(", ")
                  .Append(s1.z.ToString("0.0")).Append(")\n");
            }
            _routesSummary = sb.ToString();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 点「导出为路点数据集」：把 <see cref="SolveRoutes"/> 求得的路线导出成 D-07 的双形态——
        /// 包内固定路径的 <b>JSON</b>（可 diff、可版本化、注入器读这条）+ 同步生成的 <b>SO 资产</b>（进 Inspector）。
        /// <para>⚠️ <b>刻意非模态</b>：DESIGN §3.2 原写「弹窗确认」，但 AGENTS.md §2.1.5 禁止
        /// <c>DisplayDialog</c>（阻塞主线程 = 卡死所有会话共用的指令通道，校验器 channel-discipline 直接判红）。
        /// 确认信息改为三处可见：按钮下方文本 + 本方法日志读数 + 落盘产物本身。该偏离已记 ACCEPTANCE。</para>
        /// <para>SO 形态从<b>刚落盘的那份 JSON</b> 读回，而不是再复制一次内存对象——
        /// 这样两形态必然同源，且顺带证明「导出的 JSON 真能被解析」。</para>
        /// </summary>
        void ExportRoutesAsDataset()
        {
            if (_table == null) { _exportSummary = "无表，先烘焙可见性表"; return; }
            if (_routeBuf.Count == 0) { _exportSummary = "尚无路线：先点「求前 M 条短解」"; return; }

            var scratch = ScriptableObject.CreateInstance<PlayerRouteDataset>();
            try
            {
                if (!scratch.BuildFromRoutes(_table, _routeBuf))
                {
                    _exportSummary = "构建失败：路线集合为空";
                    return;
                }
                var json = scratch.ToJson(out var whyJson);
                if (!string.IsNullOrEmpty(whyJson) || string.IsNullOrEmpty(json))
                {
                    _exportSummary = "序列化失败：" + (whyJson ?? "JSON 为空");
                    return;
                }

                try
                {
                    var absolute = System.IO.Path.GetFullPath(DatasetJsonPath);
                    System.IO.File.WriteAllText(absolute, json);
                }
                catch (System.Exception e)
                {
                    _exportSummary = "写盘失败：" + e.Message;
                    return;
                }
                AssetDatabase.ImportAsset(DatasetJsonPath, ImportAssetOptions.ForceUpdate);

                var asset = AssetDatabase.LoadAssetAtPath<PlayerRouteDataset>(DatasetAssetPath);
                var isNew = asset == null;
                if (isNew)
                {
                    asset = ScriptableObject.CreateInstance<PlayerRouteDataset>();
                    AssetDatabase.CreateAsset(asset, DatasetAssetPath);
                }
                if (!asset.FromJson(json, out var whyLoad))
                {
                    _exportSummary = "SO 资产读回失败：" + whyLoad;
                    return;
                }
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();

                var bytes = new System.IO.FileInfo(System.IO.Path.GetFullPath(DatasetJsonPath)).Length;
                _exportSummary = "✅ 已导出 " + scratch.Count + " 条路线 →\n  " + DatasetJsonPath
                    + "（" + bytes + " 字节）\n  " + DatasetAssetPath
                    + "（SO" + (isNew ? "：新建" : "：覆盖") + "，读回校验 " + asset.Count + " 条）";
                Debug.Log("[StealthPlanner][Export] 路线=" + scratch.Count
                    + " JSON字节=" + bytes + " SO读回=" + asset.Count
                    + " 网格=" + _table.DimX + "x" + _table.DimZ
                    + " 格长=" + _table.CellSize + " Δt=" + _table.TimeStep
                    + " 版本=" + PlayerRouteDataset.SchemaVersion + " → " + DatasetJsonPath);
                SceneView.RepaintAll();
            }
            finally
            {
                // 临时 SO 不销毁会跨帧残留并可能在域重载时打 warning（条目 27 同族教训）
                if (scratch != null) Object.DestroyImmediate(scratch);
            }
        }

        /// <summary>把「本桶可达」拷成掩码——只读 Field 的公开查询，不复制其内部位图布局。</summary>
        byte[] CurrentBucketMask(int bucket, int n)
        {
            if (_field == null) return null;
            var m = new byte[n];
            for (var c = 0; c < n; c++) if (_field.IsReachable(bucket, c)) m[c] = 1;
            return m;
        }

        void RebuildMeshes(int bucket, byte[] reachMask)
        {
            if (_meshDanger != null) { Object.DestroyImmediate(_meshDanger); _meshDanger = null; }
            if (_meshReach != null) { Object.DestroyImmediate(_meshReach); _meshReach = null; }
            if (_meshCum != null) { Object.DestroyImmediate(_meshCum); _meshCum = null; }
            if (_table == null) return;

            _meshDanger = HeatmapGrid.BuildDanger(_table, _dangerMask);
            if (reachMask != null) _meshReach = HeatmapGrid.BuildReachable(_table, reachMask, 0.06f);
            if (_cumBuf.Length == _table.CellCount)
                _meshCum = HeatmapGrid.BuildReachable(_table, _cumBuf, 0.03f);
        }

        // ── 自动播放 ───────────────────────────────────────────
        void OnEditorUpdate()
        {
            if (!_autoPlay || _table == null) return;
            var now = EditorApplication.timeSinceStartup;
            var dt = now - _lastTick;
            _lastTick = now;
            if (dt <= 0.0 || dt > 0.5) return;      // 首帧/卡顿帧跳过，避免一步跳满
            var span = Mathf.Max(0.001f, (_buckets - 1) * Timestep);
            _t = Mathf.Repeat(_t + (float)dt, span);
            Recompute();
            Repaint();
        }

        // ── SceneView 绘制 ────────────────────────────────────
        // SceneView.duringSceneGui 的签名是 Action<SceneView>；其基类 View 是 internal，
        // 写成 View 会 CS0122 不可访问。
        void OnSceneGui(SceneView sv)
        {
            if (_table == null) return;
            var m = Matrix4x4.identity;
            const int layer = 0;

            if (_showCumulative && _meshCum != null)
                Graphics.DrawMesh(_meshCum, m, EnsureMat(ref _matCum, new Color(0.2f, 0.55f, 0.95f, 0.16f)),
                    layer, sv.camera, 0, null, false, true);
            if (_showReach && _meshReach != null)
                Graphics.DrawMesh(_meshReach, m, EnsureMat(ref _matReach, new Color(0.25f, 0.9f, 0.45f, 0.30f)),
                    layer, sv.camera, 0, null, false, true);
            if (_showDanger && _meshDanger != null)
                Graphics.DrawMesh(_meshDanger, m, EnsureMat(ref _matDanger, new Color(0.92f, 0.16f, 0.12f, 0.30f)),
                    layer, sv.camera, 0, null, false, true);

            if (_startCell >= 0 && _startCell < _table.CellCount)
            {
                // 必须用 Handles：Gizmos.* 在 duringSceneGui 里会抛
                // 「Gizmo drawing functions can only be used in OnDrawGizmos」——每次重绘一条 error，
                // 直接顶爆交锁门禁的 consoleErrors == 0。
                var old = Handles.color;
                Handles.color = Color.white;
                Handles.DrawWireDisc(_table.CellCenterWorld(_startCell) + Vector3.up * 0.05f,
                    Vector3.up, 0.55f);
                Handles.color = old;
            }
        }

        static Material EnsureMat(ref Material slot, Color c)
        {
            if (slot != null) return slot;
            // Sprites/Default：天然 Blend + 不写深度 ⇒ 不会与地面争深度（A 包同类坑的教训）。
            // 兜底顺序照抄 A 包实测可用的写法，不自己发明。
            var shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;
            slot = new Material(shader)
            {
                name = "StealthPlanner.Heatmap",
                hideFlags = HideFlags.HideAndDontSave
            };
            slot.color = c;
            return slot;
        }

        void ReleaseGfx()
        {
            if (_meshDanger != null) Object.DestroyImmediate(_meshDanger);
            if (_meshReach != null) Object.DestroyImmediate(_meshReach);
            if (_meshCum != null) Object.DestroyImmediate(_meshCum);
            if (_matDanger != null) Object.DestroyImmediate(_matDanger);
            if (_matReach != null) Object.DestroyImmediate(_matReach);
            if (_matCum != null) Object.DestroyImmediate(_matCum);
            _meshDanger = _meshReach = _meshCum = null;
            _matDanger = _matReach = _matCum = null;
        }
    }
}
