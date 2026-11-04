using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>对局结果。</summary>
    public enum StealthGameOutcome
    {
        /// <summary>进行中。</summary>
        Playing = 0,
        /// <summary>抵达撤离区。</summary>
        Escaped = 1,
        /// <summary>被哨兵彻底察觉。</summary>
        Caught = 2,
    }

    /// <summary>
    /// 对局规则（胜负判定 + <b>完整重开</b>）。
    /// <para>补上「游戏无法结束」这一环：抵达 <see cref="StealthExitZone"/> 即通关；
    /// 任一哨兵察觉度到顶且持续一段时间即被抓。两者都做进<b>可暂停/可复原</b>的显式状态，
    /// 便于示例重开与后续辅助台复用。</para>
    /// <para><b>重开必须是全量复位</b>（用户实机反馈「按 R 重新开始并没有效果」）：
    /// 旧实现只清了本组件的计时，玩家仍站在被抓处、哨兵仍保持警觉档、巡逻时钟继续累加
    /// ⇒ 观感等于「什么都没发生」。现在把玩家、察觉状态、所有哨兵叠加层、巡逻时钟、
    /// 撤离区驻留五处一次性归零，并在结算瞬间冻结玩家输入。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StealthGameRule : MonoBehaviour
    {
        [Tooltip("玩家目标。留空自动取场景第一个 DetectionState")]
        [SerializeField] DetectionState _player;

        [Tooltip("察觉度达到该值并开始被注视，累计多少秒判定被抓")]
        [SerializeField, Min(0.1f)] float _catchSeconds = 1.2f;

        [Tooltip("被抓后允许逃脱的宽限：脱离视线超过该秒数则解除抓捕计时")]
        [SerializeField, Min(0.1f)] float _escapeGrace = 2.5f;

        [Tooltip("出结果时是否冻结玩家输入（结算界面需要稳定画面）")]
        [SerializeField] bool _freezeOnEnd = true;

        StealthExitZone[] _exits;
        PlayerStealthController _controller;
        readonly HashSet<int> _alertedGuards = new HashSet<int>();

        float _catchTimer;
        float _lastSeenAt = -999f;
        float _roundStartT;
        float _peakSuspicion;
        bool _roundStarted;

        public StealthGameOutcome Outcome { get; private set; } = StealthGameOutcome.Playing;
        public float CatchProgress => _catchSeconds <= 0f ? 0f : Mathf.Clamp01(_catchTimer / _catchSeconds);
        public DetectionState Player => _player;

        /// <summary>本局已进行秒数（巡逻时钟口径，与位姿求值同源）。</summary>
        public float ElapsedSeconds => Mathf.Max(0f, Now() - _roundStartT);
        /// <summary>本局察觉度峰值。</summary>
        public float PeakSuspicion => _peakSuspicion;
        /// <summary>本局进入过警觉档的哨兵数。</summary>
        public int GuardsAlerted => _alertedGuards.Count;
        /// <summary>是否有撤离区已达成。</summary>
        public bool ExitReached
        {
            get
            {
                if (_exits == null) return false;
                for (var i = 0; i < _exits.Length; i++)
                {
                    if (_exits[i] != null && _exits[i].Reached) return true;
                }
                return false;
            }
        }

        static float Now()
        {
            var clock = StealthClock.Instance;
            return clock != null ? clock.Current : Time.time;
        }

        void Awake() => Resolve();

        /// <summary>
        /// 惰性解析三方引用（玩家 / 撤离区 / 玩家控制器）。
        /// <para>之所以不只在 <c>Awake</c> 里做：编辑模式测试与写工具不保证走初始化时序，
        /// 而「重开」恰恰要求这三处<b>一个都不能漏</b>——漏掉玩家就表现为「按 R 没反应」
        /// （用户实机反馈的原问题）。Update 与 ResetRound 都先调它，拿到空引用时自愈。</para>
        /// </summary>
        void Resolve()
        {
            if (_player == null)
            {
                var targets = DetectionRegistry.Targets;
                if (targets.Count > 0) _player = targets[0];
            }
            if (_controller == null && _player != null)
            {
                _controller = _player.GetComponent<PlayerStealthController>();
            }
            if (_exits == null || _exits.Length == 0)
            {
                _exits = FindObjectsByType<StealthExitZone>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            }
            // 起算标志：不能用「_roundStartT <= 0」判断是否已开局——巡逻时钟归零后
            // _roundStartT 正常就是 0，那样每帧都会把起点推到当前时间，用时永远显示 0:00。
            if (!_roundStarted) StartRoundClock();
        }

        void StartRoundClock()
        {
            _roundStartT = Now();
            _roundStarted = true;
        }

        void Update()
        {
            if (Outcome != StealthGameOutcome.Playing) return;
            Resolve();

            var dt = Time.deltaTime;
            var clock = StealthClock.Instance;
            if (clock != null && clock.Mode == StealthClockMode.Manual) return;   // 预览态不推进对局

            if (_player == null) return;

            // ① 通关
            var foot = _player.transform.position;
            for (var i = 0; i < _exits.Length; i++)
            {
                var zone = _exits[i];
                if (zone == null) continue;
                if (zone.Tick(zone.Contains(foot), dt))
                {
                    Finish(StealthGameOutcome.Escaped);
                    return;
                }
            }

            // ② 被抓：察觉度顶格且正被注视
            var maxSusp = MaxSuspicion();
            if (maxSusp > _peakSuspicion) _peakSuspicion = maxSusp;
            TrackAlertedGuards();

            if (maxSusp >= 0.999f && _player.LastSeen)
            {
                _catchTimer += dt;
                _lastSeenAt = Time.time;
                if (_catchTimer >= _catchSeconds) Finish(StealthGameOutcome.Caught);
            }
            else if (Time.time - _lastSeenAt > _escapeGrace)
            {
                _catchTimer = Mathf.Max(0f, _catchTimer - dt * 2f);
            }
        }

        void Finish(StealthGameOutcome outcome)
        {
            Outcome = outcome;
            if (_freezeOnEnd && _controller != null) _controller.Frozen = true;
        }

        void TrackAlertedGuards()
        {
            var guards = GuardBrain.All;
            for (var i = 0; i < guards.Count; i++)
            {
                var g = guards[i];
                if (g != null && g.Stage == StealthDetectionStage.Alerted) _alertedGuards.Add(g.ActorId);
            }
        }

        float MaxSuspicion()
        {
            var guards = GuardBrain.All;
            var max = 0f;
            for (var i = 0; i < guards.Count; i++)
            {
                if (guards[i] != null && guards[i].Suspicion > max) max = guards[i].Suspicion;
            }
            return Mathf.Max(max, _player != null ? _player.Suspicion : 0f);
        }

        /// <summary>
        /// 重开一局：<b>全量复位</b>。
        /// <para>顺序有讲究——先把本组件门控解开，再清哨兵叠加层、归零巡逻时钟，最后复位玩家：
        /// 巡逻时钟归零会让所有 <see cref="PatrolAgent"/> 的位姿回到 t=0 基线，
        /// 若叠加层还有残留偏移，会看到 NPC 从"上一局的追击点"瞬移回去。</para>
        /// <para><c>Outcome</c> 必须<b>最先</b>改回 Playing：结算界面的「再来一局」按钮在绘制回调里
        /// 调用本方法，若结果仍停在已结束态，同一帧的 <see cref="Update"/> 会直接早退，
        /// 残留状态就再也清不掉了。</para>
        /// </summary>
        public void ResetRound()
        {
            // ① 先解本组件自身的门控，保证后续步骤与同帧 Update 都能正常走
            Outcome = StealthGameOutcome.Playing;
            _catchTimer = 0f;
            _lastSeenAt = -999f;
            _peakSuspicion = 0f;
            _alertedGuards.Clear();

            // 编辑态/预制件场景下 Awake 可能没跑过（或当时还没有玩家），这里重新解析一遍，
            // 否则 _player / _controller / _exits 是空引用，"复位"会静默漏掉玩家——
            // 正是用户反馈的「按 R 没效果」的另一条可能路径。
            _controller = null;
            _exits = null;
            Resolve();

            if (_controller != null) _controller.Frozen = false;

            // ② 撤离区驻留
            if (_exits != null)
            {
                for (var i = 0; i < _exits.Length; i++)
                {
                    if (_exits[i] != null) _exits[i].ResetZone();
                }
            }

            // ③ 玩家察觉状态
            if (_player != null) _player.Reset();

            // ④ 所有哨兵：清档位/偏移/可疑点/导航路径
            var guards = GuardBrain.All;
            for (var i = 0; i < guards.Count; i++)
            {
                if (guards[i] != null) guards[i].ResetOverlay();
            }

            // ⑤ 巡逻时钟归零 ⇒ 基线位姿回到 t=0（位姿是时间的纯函数，这一步就够）
            var clock = StealthClock.Instance;
            if (clock != null) clock.SetAutoTime(0f);
            StartRoundClock();   // 走统一入口，保证 _roundStartT 与 _roundStarted 同步

            // ⑥ 玩家回出生点
            if (_controller != null) _controller.ResetToSpawn();
        }

        /// <summary>场景里是否有可重开的对局规则（供结算界面判断按钮是否可用）。</summary>
        public static StealthGameRule Find()
        {
            return FindFirstObjectByType<StealthGameRule>();
        }

        void OnValidate()
        {
            _catchSeconds = Mathf.Max(0.1f, _catchSeconds);
            _escapeGrace = Mathf.Max(0.1f, _escapeGrace);
        }
    }
}
