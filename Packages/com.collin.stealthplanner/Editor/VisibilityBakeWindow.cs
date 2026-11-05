using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Collin.StealthPlanner.EditorTooling
{
    /// <summary>
    /// <b>烘焙窗口</b>（M1 的交互入口）：把当前场景的哨兵可见性烘成
    /// <see cref="VisibilityTableAsset"/>。菜单
    /// <c>Tools ▸ Collin ▸ Stealth Planner ▸ 烘焙可见性表</c>。
    /// <para><b>分帧而非一次跑完</b>（DESIGN D-11）：判定底层是射线、只能主线程，一口气跑完会卡死
    /// 编辑器并切断所有 AI 会话的实时通道。窗口每帧只从 <see cref="VisibilityBakeSession.Pump"/>
    /// 取固定预算，随时可中断；<b>跨帧复用同一会话</b>，绝不每帧重枚举 footprint。</para>
    /// <para>提示一律走日志与面板文本，<b>不用 <c>DisplayDialog</c></b>：共享编辑器里模态窗会卡死指令通道。</para>
    /// </summary>
    internal sealed class VisibilityBakeWindow : EditorWindow
    {
        const string MenuPath = "Tools/Collin/Stealth Planner/烘焙可见性表";
        const string SampleDir = "Packages/com.collin.stealthplanner/Sample";
        const string SettingsPath = SampleDir + "/PlannerBakeSettings.asset";
        const string TablePath = SampleDir + "/VisibilityTable.asset";

        /// <summary>每帧查询预算：太小烘得慢、太大掉帧。实测后可调（DESIGN §7 待校准项）。</summary>
        const long QueriesPerFrame = 400;

        [SerializeField] PlannerBakeSettings _settings;
        [SerializeField] VisibilityTableAsset _target;
        [SerializeField] Vector3 _areaMin = new Vector3(-27f, 0f, -27f);
        [SerializeField] Vector3 _areaMax = new Vector3(27f, 0f, 27f);
        [Tooltip("向下打多长找地面（米）。太短会漏掉低处地面，太长会穿到地板下方")]
        [SerializeField] float _groundProbeDown = 3f;
        [Tooltip("站立体净高（米）：地面上这么多距离内有实体 ⇒ 该格判为不可站")]
        [SerializeField, Range(0.3f, 3f)] float _standClearance = 1.5f;

        VisibilityBakeSession _session;
        bool _running;
        double _startedAt;
        double _pumpFrames;
        string _status = "未烘焙";

        [MenuItem(MenuPath)]
        static void Open()
        {
            var w = GetWindow<VisibilityBakeWindow>();
            w.titleContent = new GUIContent("烘焙可见性表");
            w.minSize = new Vector2(380f, 360f);
        }

        /// <summary>
        /// <b>脚本模式</b>：一次跑完、写盘、把读数打进日志。菜单
        /// <c>Tools ▸ Collin ▸ Stealth Planner ▸ 烘焙（脚本模式）</c>。
        /// <para>为什么要有它：交互窗口的分帧泵挂在 <c>EditorApplication.update</c> 上，而本工程实测
        /// <b>窗口失焦时不推进泵</b>（<c>set_auto_tick</c> 只保活 UI）。于是命令行 / CI / 自动化取证
        /// 走不通窗口这条路。本入口<b>会阻塞主线程</b>直到烘完，只适合脚本与离线。</para>
        /// <para>读数（片数、格数、耗时、预筛省下的射线数）全部打进日志，供验收表直接引用。</para>
        /// </summary>
        [MenuItem(MenuPath + "（脚本模式）")]
        static void BakeFromScript()
        {
            // 与窗口 OnEnable 同一入口：脚本模式常在窗口从未打开过时被调用（新克隆、CI），
            // 只 Load 会让取证在干净工程里直接失败。
            var settings = EnsureAsset<PlannerBakeSettings>(SettingsPath);
            var target = EnsureAsset<VisibilityTableAsset>(TablePath);
            if (settings == null || target == null)
            {
                Debug.LogWarning("[StealthPlanner] 脚本模式缺设置或输出资产（创建失败，见上方日志）");
                return;
            }

            var session = VisibilityBakeSession.TryBegin(new Vector3(-27f, 0f, -27f),
                new Vector3(27f, 0f, 27f), settings, 3f, 1.5f, out var fail);
            if (session == null)
            {
                Debug.LogWarning("[StealthPlanner] 脚本模式未启动：" + fail);
                return;
            }
            var bakeStart = EditorApplication.timeSinceStartup;
            if (!session.RunToEnd())
            {
                Debug.LogWarning("[StealthPlanner] 脚本模式推进未收敛");
                return;
            }
            var bakeSeconds = EditorApplication.timeSinceStartup - bakeStart;

            var table = session.Table;
            target.SetBaked(table, settings.CellSize, settings.TimeStep, settings.PhaseWindow,
                table.Slices.Count, table.PermanentlySafeCellCount, table.PackedByteCount);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();

            Debug.Log("[StealthPlanner][ScriptBake] 片数=" + table.Slices.Count
                + " 活动格=" + session.WalkableCells.ToString("N0") + "/" + session.RectCells.ToString("N0")
                + " 永久安全格=" + table.PermanentlySafeCellCount.ToString("N0")
                + " 位图KB=" + (table.PackedByteCount / 1024f).ToString("0.0")
                + " 查询=" + session.Queries.ToString("N0")
                + " 预筛省射线=" + session.Prefiltered.ToString("N0")
                + " 实打射线=" + session.Forwarded.ToString("N0")
                + " 主循环耗时=" + bakeSeconds.ToString("0.00") + "s"
                + " footprint耗时=" + session.FootprintSeconds.ToString("0.00")
                + "s 跳过=" + session.Skipped.Count
                + (session.Skipped.Count > 0
                    ? "（" + string.Join("、", session.Skipped.ToArray()) + "）" : "")
                // ⚠️ 必须打印：整片零可见 = 该哨兵视野在表里不存在 ⇒ 热区谎报安全。
                // 这道防线若只存在内存里、日志与表都不出口，等于没有防线（本轮实测撞见）。
                + " 零可见=" + session.ZeroVisibilityGuards.Count
                + (session.ZeroVisibilityGuards.Count > 0
                    ? "（" + string.Join("、", session.ZeroVisibilityGuards.ToArray()) + "）" : ""));
        }

        void OnEnable()
        {
            // 必须「缺失即创建」而非只加载：表资产与配置资产都可能不在库里
            // （新克隆 / 表资产按派生产物处理不入库）。只加载会让「开始烘焙」永久灰死、
            // 且面板只显示一句缺资产的提示而无自助路径——那是死胡同 UI。
            if (_settings == null) _settings = EnsureAsset<PlannerBakeSettings>(SettingsPath);
            if (_target == null) _target = EnsureAsset<VisibilityTableAsset>(TablePath);
        }

        /// <summary>取包内资产；不存在则创建（含建 `Sample/` 目录）。失败返回 null 并只打日志。</summary>
        static T EnsureAsset<T>(string path) where T : ScriptableObject
        {
            var found = AssetDatabase.LoadAssetAtPath<T>(path);
            if (found != null) return found;

            var dir = (System.IO.Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                // 父目录必须已存在（包根一定在）；这里只补最后一级，不做递归以免误建路径
                var parent = (System.IO.Path.GetDirectoryName(dir) ?? string.Empty).Replace('\\', '/');
                var leaf = System.IO.Path.GetFileName(dir);
                if (AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder(parent, leaf);
                else
                {
                    Debug.LogWarning("[StealthPlanner] 无法创建 " + path + "：父目录不存在 " + parent);
                    return null;
                }
            }

            found = CreateInstance<T>();
            found.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(found, path);
            AssetDatabase.SaveAssets();
            Debug.Log("[StealthPlanner] 已创建包内资产 " + path);
            return found;
        }

        void OnDisable() => Detach();

        void OnGUI()
        {
            EditorGUILayout.LabelField("参数与目标资产", EditorStyles.boldLabel);
            _settings = (PlannerBakeSettings)EditorGUILayout.ObjectField(
                "烘焙设置", _settings, typeof(PlannerBakeSettings), false);
            _target = (VisibilityTableAsset)EditorGUILayout.ObjectField(
                "输出资产", _target, typeof(VisibilityTableAsset), false);

            using (new EditorGUI.DisabledScope(_running))
            {
                EditorGUILayout.BeginHorizontal();
                _areaMin = EditorGUILayout.Vector3Field("范围 min", _areaMin);
                _areaMax = EditorGUILayout.Vector3Field("范围 max", _areaMax);
                EditorGUILayout.EndHorizontal();
                _groundProbeDown = EditorGUILayout.FloatField("地面向下探测(m)", _groundProbeDown);
                _standClearance = EditorGUILayout.Slider("站立净高(m)", _standClearance, 0.3f, 3f);
            }

            var cell = _settings != null ? _settings.CellSize : 0.5f;
            var dt = _settings != null ? _settings.TimeStep : 0.5f;
            EditorGUILayout.HelpBox(
                "格长 " + cell.ToString("0.###") + "m · Δt " + dt.ToString("0.###") + "s ⇒ 矩形格上限 "
                + RectCellCount(cell).ToString("N0") + "（实际按可活动 footprint 过滤后更少）\n"
                + "查询数 ≈ 可解析哨兵数 × 各自⌈c_i/Δt⌉ × 活动格数，全部主线程射线、分帧推进。",
                MessageType.Info);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_running || _settings == null || _target == null))
            {
                if (GUILayout.Button("开始烘焙", GUILayout.Height(28f))) Start();
            }
            using (new EditorGUI.DisabledScope(!_running))
            {
                if (GUILayout.Button("中断", GUILayout.Height(20f))) Stop();
            }

            EditorGUILayout.Space();
            DrawProgress();
            EditorGUILayout.LabelField("状态", _status);
            DrawBakedSummary();
        }

        void DrawProgress()
        {
            if (_session == null || !_running) return;

            var total = _session.TotalQueries;
            var p = total > 0 ? Mathf.Clamp01(_session.Queries / (float)total) : 0f;

            // EditorGUILayout 没有 ProgressBar；它在 EditorGUI 上、要 Rect。
            var rect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(rect, p, "查询 " + _session.Queries.ToString("N0") + " / " + total.ToString("N0"));

            var secs = EditorApplication.timeSinceStartup - _startedAt;
            var eta = p > 0.02f ? secs / p - secs : -1f;
            EditorGUILayout.LabelField("已用 " + secs.ToString("0.0") + "s · 预计剩余 "
                + (eta >= 0f ? eta.ToString("0.0") + "s" : "—")
                + " · 帧预算 " + QueriesPerFrame + "/帧 × " + ((int)_pumpFrames).ToString("N0") + " 帧");
            Repaint();
        }

        void DrawBakedSummary()
        {
            if (_target == null || !_target.IsBaked) return;
            var table = _target.Table;
            var total = table != null ? table.CellCount : 0;
            EditorGUILayout.LabelField("已烘 " + _target.GuardCount + " 片 · 永久安全格 "
                + _target.SafeCellCount.ToString("N0") + " / " + total.ToString("N0")
                + " · 位图 " + (_target.PackedBytes / 1024f).ToString("0.0") + " KB");
        }

        int RectCellCount(float cell)
        {
            cell = Mathf.Max(0.01f, cell);
            var n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(_areaMax.x - _areaMin.x) / cell));
            var m = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(_areaMax.z - _areaMin.z) / cell));
            return n * m;
        }

        // ── 流程 ────────────────────────────────────────────────

        void Start()
        {
            if (_running) return;

            var s = VisibilityBakeSession.TryBegin(_areaMin, _areaMax, _settings,
                _groundProbeDown, _standClearance, out var fail);
            if (s == null)
            {
                _status = fail;
                Debug.LogWarning("[StealthPlanner] 烘焙未启动：" + fail);
                Repaint();
                return;
            }

            _session = s;
            if (_target != null) _target.ClearBaked();
            _running = true;
            _startedAt = EditorApplication.timeSinceStartup;
            _pumpFrames = 0;
            _status = "footprint " + s.WalkableCells.ToString("N0") + "/" + s.RectCells.ToString("N0")
                      + " 格（枚举 " + s.FootprintSeconds.ToString("0.00") + "s）· 哨兵 " + s.GuardCount
                      + " 名 · 烘焙中";
            EditorApplication.update += Tick;
            Repaint();
        }

        void Tick()
        {
            if (!_running || _session == null) return;
            _pumpFrames++;
            if (!_session.Pump(QueriesPerFrame))
            {
                Repaint();
                return;                                        // 未完成，下一帧继续
            }

            EditorApplication.update -= Tick;
            _running = false;
            Finish();
            Repaint();
        }

        void Finish()
        {
            var table = _session.Table;
            var secs = EditorApplication.timeSinceStartup - _startedAt;

            if (_target != null)
            {
                _target.SetBaked(table, _settings.CellSize, _settings.TimeStep, _settings.PhaseWindow,
                    table.Slices.Count, table.PermanentlySafeCellCount, table.PackedByteCount);
                EditorUtility.SetDirty(_target);
                AssetDatabase.SaveAssets();
            }

            _status = "完成：" + table.Slices.Count + " 片 / " + table.CellCount.ToString("N0")
                      + " 格 / " + (table.PackedByteCount / 1024f).ToString("0.0") + " KB / "
                      + secs.ToString("0.0") + "s / " + ((int)_pumpFrames).ToString("N0") + " 帧";

            Debug.Log("[StealthPlanner] 烘焙" + _status
                + " | 查询=" + _session.Queries.ToString("N0")
                + " 预筛省射线=" + _session.Prefiltered.ToString("N0")
                + " 实打射线=" + _session.Forwarded.ToString("N0"));

            WarnSkipped(_session);
            _session = null;
        }

        /// <summary>
        /// 被跳过的哨兵在表里等于「不存在」，求解会<b>高估</b>安全性 ⇒ 必须显式警告，
        /// 不能只写进日志（策划不会翻日志）。
        /// </summary>
        static void WarnSkipped(VisibilityBakeSession s)
        {
            if (s.Skipped.Count > 0)
            {
                Debug.LogWarning("[StealthPlanner] " + s.Skipped.Count
                    + " 名哨兵无法按时刻解析位姿，<未参与烘焙>（表内等于不存在，"
                    + "求解结果对其视野会偏乐观）：" + string.Join("、", s.Skipped.ToArray()));
            }
            if (s.GuardsWithoutDistance > 0)
            {
                Debug.LogWarning("[StealthPlanner] " + s.GuardsWithoutDistance
                    + " 名单位取不到视距参数，距离预筛对其不生效（结果仍正确，只是更慢）。");
            }
        }

        void Stop()
        {
            if (!_running) return;
            Detach();
            _status = "已中断（结果未写入资产）";
            Debug.Log("[StealthPlanner] 烘焙已中断，资产保持原状");
            Repaint();
        }

        void Detach()
        {
            EditorApplication.update -= Tick;
            _running = false;
            _session = null;
        }
    }
}
