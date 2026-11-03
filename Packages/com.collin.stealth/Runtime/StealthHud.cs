using System.Text;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 极简运行期 HUD（IMGUI，零依赖、零场景配置）：显示玩家察觉度、各哨兵档位与操作提示。
    /// <para>属于示例演示层而非玩法逻辑（DESIGN §3.4：「察觉度 UI 由示例场景/B 的演示层承担，
    /// 不进 Runtime 逻辑」）。这里放在 Runtime 是因为它必须跟着预制件走、供用户开箱验证；
    /// 它<b>只读</b>玩法状态，不写任何东西。</para>
    /// </summary>
    public sealed class StealthHud : MonoBehaviour
    {
        [Tooltip("跟随显示的玩家目标。留空自动找场景内第一个 DetectionState")]
        [SerializeField] DetectionState _target;

        [Tooltip("左上角显示哨兵档位列表")]
        [SerializeField] bool _showGuards = true;

        [Tooltip("察觉度条宽度（像素）")]
        [SerializeField, Range(60f, 600f)] float _barWidth = 220f;

        GUIStyle _label;
        GUIStyle _title;
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
            _styleReady = true;
        }

        void OnGUI()
        {
            EnsureStyle();

            var w = Mathf.Min(_barWidth, Screen.width - 20f);

            GUILayout.BeginArea(new Rect(16f, 16f, w + 16f, 260f));

            if (_target != null)
            {
                var s = _target.Suspicion;
                var label = _target.LastSeen
                    ? (_target.Crouching ? "被注视（蹲伏中）" : "被注视")
                    : (_target.Crouching ? "安全（蹲伏中）" : "未被发现");

                GUI.backgroundColor = StageColor(_target.Stage);
                GUILayout.Label(label, _label);
                var rect = GUILayoutUtility.GetLastRect();
                GUI.Box(new Rect(rect.x, rect.y + 20f, w, 14f), string.Empty);
                GUI.color = StageColor(_target.Stage);
                GUI.Box(new Rect(rect.x, rect.y + 20f, w * Mathf.Clamp01(s), 14f), string.Empty);
                GUI.color = Color.white;
                GUILayout.Label("察觉度 " + (s * 100f).ToString("0") + "%", _title);
            }
            else
            {
                GUILayout.Label("场景中未找到 DetectionState", _title);
            }

            GUILayout.Space(8f);

            if (_showGuards)
            {
                var guards = GuardBrain.All;
                for (var i = 0; i < guards.Count; i++)
                {
                    var g = guards[i];
                    if (g == null) continue;
                    var text = g.name + "  " + StageName(g.Stage);
                    if (g.Active) text += "  可疑点 " + Point(g.LastSeenPoint);
                    GUI.color = StageColor(g.Stage);
                    GUILayout.Label(text, _title);
                    GUI.color = Color.white;
                }
                if (guards.Count == 0) GUILayout.Label("场景中未找到哨兵（GuardBrain）", _title);
            }

            GUILayout.Space(10f);
            GUILayout.Label("WASD 移动 ·  Ctrl 蹲伏 ·  右键拖拽转视角", _title);
            GUILayout.EndArea();
        }

        static string Point(Vector3 p)
        {
            return "(" + p.x.ToString("0") + "," + p.z.ToString("0") + ")";
        }

        static string StageName(StealthDetectionStage s)
        {
            switch (s)
            {
                case StealthDetectionStage.Suspicious: return "疑惑";
                case StealthDetectionStage.Alerted: return "警觉";
                case StealthDetectionStage.Searching: return "搜索";
                default: return "巡逻";
            }
        }

        static Color StageColor(StealthDetectionStage s)
        {
            switch (s)
            {
                case StealthDetectionStage.Alerted: return new Color(0.95f, 0.25f, 0.2f);
                case StealthDetectionStage.Searching: return new Color(0.95f, 0.55f, 0.15f);
                case StealthDetectionStage.Suspicious: return new Color(0.95f, 0.85f, 0.25f);
                default: return new Color(0.3f, 0.85f, 0.4f);
            }
        }

        void OnValidate()
        {
            _barWidth = Mathf.Clamp(_barWidth, 60f, 600f);
        }
    }
}
