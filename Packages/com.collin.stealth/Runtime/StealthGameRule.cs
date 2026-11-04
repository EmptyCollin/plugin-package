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
    /// 对局规则（胜负判定）。
    /// <para>补上「游戏无法结束」这一环：抵达 <see cref="StealthExitZone"/> 即通关；
    /// 任一哨兵察觉度到顶且持续一段时间即被抓。两者都做进<b>可暂停/可复原</b>的显式状态，
    /// 便于示例重开与后续辅助台复用。</para>
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

        StealthExitZone[] _exits;
        float _catchTimer;
        float _lastSeenAt = -999f;

        public StealthGameOutcome Outcome { get; private set; } = StealthGameOutcome.Playing;
        public float CatchProgress => _catchSeconds <= 0f ? 0f : Mathf.Clamp01(_catchTimer / _catchSeconds);
        public DetectionState Player => _player;

        void Awake()
        {
            if (_player == null)
            {
                var targets = DetectionRegistry.Targets;
                if (targets.Count > 0) _player = targets[0];
            }
            _exits = FindObjectsByType<StealthExitZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        void Update()
        {
            if (Outcome != StealthGameOutcome.Playing) return;

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
                    Outcome = StealthGameOutcome.Escaped;
                    return;
                }
            }

            // ② 被抓：察觉度顶格且正被注视
            var maxSusp = MaxSuspicion();
            if (maxSusp >= 0.999f && _player.LastSeen)
            {
                _catchTimer += dt;
                _lastSeenAt = Time.time;
                if (_catchTimer >= _catchSeconds) Outcome = StealthGameOutcome.Caught;
            }
            else if (Time.time - _lastSeenAt > _escapeGrace)
            {
                _catchTimer = Mathf.Max(0f, _catchTimer - dt * 2f);
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

        /// <summary>重开一局：清结果、清驻留与抓捕计时。</summary>
        public void ResetRound()
        {
            Outcome = StealthGameOutcome.Playing;
            _catchTimer = 0f;
            _lastSeenAt = -999f;
            if (_exits != null)
            {
                for (var i = 0; i < _exits.Length; i++)
                {
                    if (_exits[i] != null) _exits[i].ResetZone();
                }
            }
        }

        void OnValidate()
        {
            _catchSeconds = Mathf.Max(0.1f, _catchSeconds);
            _escapeGrace = Mathf.Max(0.1f, _escapeGrace);
        }
    }
}
