using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 极简运行期 HUD（IMGUI，零依赖、零场景配置）：察觉度、各哨兵档位、
    /// <b>撤离目标与胜负状态</b>、操作提示。
    /// <para>属于示例演示层而非玩法逻辑（DESIGN §3.4）。放 Runtime 是因为它要跟着预制件走、
    /// 供开箱验证；<b>只读</b>玩法状态，不写任何东西。</para>
    /// <para>配色与档位名统一取自 <see cref="StealthStageVisual"/>：与地面投影同源，
    /// 避免「HUD 说红、地上画黄」这种自相矛盾的可视化。</para>
    /// </summary>
    public sealed class StealthHud : MonoBehaviour
    {
        [Tooltip("跟随显示的玩家目标。留空自动找场景内第一个 DetectionState")]
        [SerializeField] DetectionState _target;

        [Tooltip("显示哨兵档位列表")]
        [SerializeField] bool _showGuards = true;

        [Tooltip("察觉度条宽度（像素）")]
        [SerializeField, Range(60f, 600f)] float _barWidth = 260f;

        GUIStyle _label;
        GUIStyle _title;
        GUIStyle _big;
        bool _styleReady;

        void Start()
        {
            if (_target == null)
            {
                var targets = DetectionRegistry.Targets;
                if (targets.Count > 0) _target = targets[0];
            }
        }

        void EnsureStyle()
        {
            if (_styleReady) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _title.normal.textColor = Color.gray;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _styleReady = true;
        }

        void OnGUI()
        {
            EnsureStyle();

            var w = Mathf.Min(_barWidth, Screen.width - 20f);
            GUILayout.BeginArea(new Rect(16f, 16f, w + 16f, 300f));

            var rule = FindRule();
            var outcome = rule != null ? rule.Outcome : StealthGameOutcome.Playing;

            // ① 察觉度
            if (_target != null)
            {
                var s = _target.Suspicion;
                var label = _target.LastSeen
                    ? (_target.Crouching ? "被注视（蹲伏中）" : "被注视")
                    : (_target.Crouching ? "安全（蹲伏中）" : "未被发现");

                GUI.backgroundColor = StealthStageVisual.ColorOf(_target.Stage);
                GUILayout.Label(label, _label);
                var rect = GUILayoutUtility.GetLastRect();
                GUI.Box(new Rect(rect.x, rect.y + 20f, w, 14f), string.Empty);
                GUI.color = StealthStageVisual.ColorOf(_target.Stage);
                GUI.Box(new Rect(rect.x, rect.y + 20f, w * Mathf.Clamp01(s), 14f), string.Empty);
                GUI.color = Color.white;
                GUILayout.Label("察觉度 " + (s * 100f).ToString("0") + "%", _title);

                if (rule != null && rule.CatchProgress > 0.01f)
                {
                    GUILayout.Label("被抓倒计时 " + rule.CatchProgress.ToString("0%"), _title);
                }
            }
            else
            {
                GUILayout.Label("场景中未找到 DetectionState", _title);
            }

            GUILayout.Space(6f);

            // ② 撤离目标指示
            var playerPos = _target != null ? _target.transform.position : Vector3.zero;
            var zone = FindNearestExit(playerPos);
            if (zone != null && outcome == StealthGameOutcome.Playing)
            {
                var from = playerPos;
                var flat = new Vector3(zone.Center.x - from.x, 0f, zone.Center.z - from.z);
                var dist = flat.magnitude;
                GUI.color = new Color(0.45f, 0.85f, 1f);
                GUILayout.Label("撤离点 " + dist.ToString("0.0") + " m  " + Compass(flat), _label);
                GUI.color = Color.white;
                if (zone.DwellProgress > 0.01f)
                {
                    GUILayout.Label("撤离中 " + zone.DwellProgress.ToString("0%"), _title);
                }
            }
            else if (zone == null && outcome == StealthGameOutcome.Playing)
            {
                GUILayout.Label("场景内没有 StealthExitZone（无通关点）", _title);
            }

            GUILayout.Space(6f);

            // ③ 哨兵档位
            if (_showGuards)
            {
                var guards = GuardBrain.All;
                for (var i = 0; i < guards.Count; i++)
                {
                    var g = guards[i];
                    if (g == null) continue;
                    var text = g.name + "  " + StealthStageVisual.NameOf(g.Stage);
                    if (g.Active) text += "  可疑点 " + Point(g.LastSeenPoint);
                    GUI.color = StealthStageVisual.ColorOf(g.Stage);
                    GUILayout.Label(text, _title);
                    GUI.color = Color.white;
                }
                if (guards.Count == 0) GUILayout.Label("场景中未找到哨兵（GuardBrain）", _title);
            }

            GUILayout.Space(8f);
            GUILayout.Label("WASD 相机相对移动 · Shift 跑 · Ctrl 蹲伏 · 右键拖拽转视角", _title);
            GUILayout.EndArea();

            // 胜负由 StealthResultPanel 统一呈现（含用时/察觉峰值归因数据）。
            // HUD 不再画横幅：两处同帧都响应 R 会重复触发复位，且横幅与结算卡片同时出现自相矛盾。
            if (rule == null)
            {
                var box = new Rect(0f, Screen.height * 0.36f, Screen.width, 60f);
                GUI.Label(box, "场景内没有 StealthGameRule（无胜负判定）", _big);
            }
        }

        static StealthGameRule FindRule()
        {
            return UnityEngine.Object.FindFirstObjectByType<StealthGameRule>();
        }

        /// <summary>取离玩家最近的未完成撤离区（无未完成者时退回第一个，保证 HUD 始终有目标）。</summary>
        static StealthExitZone FindNearestExit(Vector3 from)
        {
            var all = UnityEngine.Object.FindObjectsByType<StealthExitZone>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (all.Length == 0) return null;

            StealthExitZone best = null;
            var bestSq = float.MaxValue;
            for (var i = 0; i < all.Length; i++)
            {
                var z = all[i];
                if (z == null || z.Reached) continue;
                var c = z.Center;
                var dx = c.x - from.x;
                var dz = c.z - from.z;
                var d = dx * dx + dz * dz;
                if (d < bestSq) { bestSq = d; best = z; }
            }
            return best ?? all[0];
        }

        static string Compass(Vector3 flat)
        {
            if (flat.sqrMagnitude < 1e-4f) return "原地";
            var ang = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            if (ang < 0f) ang += 360f;
            if (ang < 22.5f || ang >= 337.5f) return "正北(+Z)";
            if (ang < 67.5f) return "东北";
            if (ang < 112.5f) return "正东(+X)";
            if (ang < 157.5f) return "东南";
            if (ang < 202.5f) return "正南(-Z)";
            if (ang < 247.5f) return "西南";
            if (ang < 292.5f) return "正西(-X)";
            return "西北";
        }

        static string Point(Vector3 p)
        {
            return "(" + p.x.ToString("0") + "," + p.z.ToString("0") + ")";
        }

        void OnValidate()
        {
            _barWidth = Mathf.Clamp(_barWidth, 60f, 600f);
        }
    }
}
