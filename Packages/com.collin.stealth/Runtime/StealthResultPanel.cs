using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 胜利 / 失败<b>结算界面</b>（用户实机反馈「给胜利/失败做个简单UI」）。
    /// <para>为什么用 IMGUI 而不是 uGUI：uGUI 需要场景里存在 <c>Canvas + EventSystem</c>，
    /// 会破坏本包「玩家预制件拖进任意空场景即可玩」（DESIGN D-13）的承诺——
    /// 空场景没有 EventSystem 时按钮直接失灵，表现为「有 UI 但点不动」。
    /// IMGUI 零依赖、零场景配置，与 <see cref="StealthHud"/> 同一口径。</para>
    /// <para>「显示什么」全部来自 <see cref="StealthResultModel"/>（纯函数、可脱离 Play 态断言），
    /// 本组件只负责画与接输入。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StealthResultPanel : MonoBehaviour
    {
        [Tooltip("出结果后多少秒自动弹出（0 = 立刻）")]
        [SerializeField, Range(0f, 3f)] float _revealDelay = 0.25f;

        [Tooltip("卡片宽度（像素）")]
        [SerializeField, Range(260f, 720f)] float _cardWidth = 380f;

        GUIStyle _title;
        GUIStyle _sub;
        GUIStyle _rowLabel;
        GUIStyle _rowValue;
        GUIStyle _hint;
        Texture2D _solid;
        bool _styleReady;
        float _styleRefHeight;          // 样式建立时的 Screen.height，变了就重建（改分辨率/全屏切换）
        float _s = 1f;                  // 缩放系数：以 1080 高的 1.0 为基准
        bool _dismissed;
        float _endedAt = -999f;
        StealthGameOutcome _lastOutcome = StealthGameOutcome.Playing;

        void EnsureStyle()
        {
            // IMGUI 没有 uGUI 的 Canvas 缩放，字号一律按像素——4K 屏上 12px 小到看不见，
            // 1080 以下又会撑破卡片。统一乘一个缩放系数，并把基准高度记下来，
            // 分辨率一变（切全屏、拖到副屏）就重建样式。
            var refH = Mathf.Max(480f, Screen.height);
            if (_styleReady && Mathf.Abs(refH - _styleRefHeight) < 1f) return;

            if (_solid == null)
            {
                _solid = new Texture2D(1, 1);
                _solid.SetPixel(0, 0, Color.white);
                _solid.Apply();
            }
            _s = Mathf.Clamp(refH / 1080f, 0.75f, 2.2f);

            _title = new GUIStyle(GUI.skin.label)
            { fontSize = Px(30), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _sub = new GUIStyle(GUI.skin.label)
            { fontSize = Px(13), alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _sub.normal.textColor = new Color(0.78f, 0.78f, 0.82f);
            _rowLabel = new GUIStyle(GUI.skin.label) { fontSize = Px(14) };
            _rowLabel.normal.textColor = new Color(0.7f, 0.7f, 0.75f);
            _rowValue = new GUIStyle(GUI.skin.label)
            { fontSize = Px(15), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _hint = new GUIStyle(GUI.skin.label) { fontSize = Px(12), alignment = TextAnchor.MiddleCenter };
            _hint.normal.textColor = Color.gray;
            _styleReady = true;
            _styleRefHeight = refH;
        }

        /// <summary>把 1080p 基准像素换算成当前分辨率下的字号。</summary>
        int Px(float baseSize) => Mathf.Max(10, Mathf.RoundToInt(baseSize * _s));

        void OnDestroy()
        {
            if (_solid != null) Object.Destroy(_solid);
        }

        StealthGameRule _rule;

        void Update()
        {
            if (_rule == null) _rule = StealthGameRule.Find();
            if (_rule == null) return;

            var outcome = _rule.Outcome;

            // 新一局开始（结算面板被关闭后重开）：恢复可弹出状态
            if (outcome == StealthGameOutcome.Playing)
            {
                if (_lastOutcome != StealthGameOutcome.Playing)
                {
                    _dismissed = false;
                    _endedAt = -999f;
                }
                _lastOutcome = outcome;
                return;
            }

            // 结果刚产生：记录弹出时刻；R 键重开
            if (_lastOutcome != outcome)
            {
                _endedAt = Time.unscaledTime;
                _lastOutcome = outcome;
            }
            if (ReadRestartPressed()) _rule.ResetRound();
            if (ReadDismissPressed()) _dismissed = true;
        }

        static bool ReadDismissPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }

        void OnGUI()
        {
            if (_rule == null) _rule = StealthGameRule.Find();
            if (_rule == null || _dismissed) return;
            if (_rule.Outcome == StealthGameOutcome.Playing) return;
            if (Time.unscaledTime - _endedAt < _revealDelay) return;

            EnsureStyle();

            var data = StealthResultModel.Build(
                _rule.Outcome, _rule.ElapsedSeconds, _rule.PeakSuspicion,
                _rule.GuardsAlerted, _rule.ExitReached);
            if (data == null) return;

            // ① 全屏压暗（不动画质、不加相机，纯 GUI 层）
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _solid);
            GUI.color = Color.white;

            // ② 卡片（所有尺寸按 _s 缩放：只放大字号不改行高会让文字互相重叠）
            var w = Mathf.Min(_cardWidth * _s, Screen.width - 24f);
            var rowH = Px(24);
            var h = Px(150) + data.Rows.Count * rowH + Px(96);
            var card = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(card, GUIContent.none);

            // 顶部强调条：纯色贴图直接画（GUI.Box 会带皮肤九宫格，色带会变成灰边）
            GUI.color = data.Accent;
            GUI.DrawTexture(new Rect(card.x, card.y, card.width, Px(4)), _solid);
            GUI.color = Color.white;

            var y = card.y + Px(22);
            _title.normal.textColor = data.Accent;
            GUI.Label(new Rect(card.x, y, card.width, Px(38)), data.Title, _title);
            y += Px(40);
            GUI.Label(new Rect(card.x + Px(16), y, card.width - Px(32), Px(34)), data.Subtitle, _sub);
            y += Px(40);

            // ③ 数据行
            for (var i = 0; i < data.Rows.Count; i++)
            {
                var row = data.Rows[i];
                GUI.Label(new Rect(card.x + Px(26), y, card.width * 0.5f, Px(22)), row.Label, _rowLabel);
                GUI.Label(new Rect(card.x + card.width * 0.5f, y, card.width * 0.5f - Px(26), Px(22)),
                          row.Value, _rowValue);
                y += rowH;
            }
            y += Px(12);

            // ④ 按钮
            if (GUI.Button(new Rect(card.x + Px(26), y, card.width - Px(52), Px(34)), data.PrimaryHint))
            {
                _rule.ResetRound();
            }
            y += Px(40);
            if (GUI.Button(new Rect(card.x + Px(26), y, card.width - Px(52), Px(24)), "关闭本界面"))
            {
                _dismissed = true;
            }
            GUI.Label(new Rect(card.x, card.y + card.height - Px(24), card.width, Px(20)),
                      "R 重新开始 · Esc 关闭", _hint);
        }

        void OnDisable()
        {
            _dismissed = false;
            _endedAt = -999f;
            _lastOutcome = StealthGameOutcome.Playing;
        }

        /// <summary>条件编译只进方法体：写在外面会让调用点在单后端工程里找不到方法。</summary>
        static bool ReadRestartPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && (kb.rKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.GetKeyDown(KeyCode.R) || UnityEngine.Input.GetKeyDown(KeyCode.F);
#else
            return false;
#endif
        }

        void OnValidate()
        {
            _cardWidth = Mathf.Clamp(_cardWidth, 260f, 720f);
            _revealDelay = Mathf.Clamp(_revealDelay, 0f, 3f);
        }
    }
}
