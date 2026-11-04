using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 察觉目标状态（挂玩家或任意被侦测物体）。<b>实例状态</b>，不进任何共享资产。
    /// <para>帧循环只在 Play 态跑；编辑态由 <see cref="StealthWorld"/> 显式驱动
    /// （同 M1 时钟的处理，见 stealth ACCEPTANCE F-5）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DetectionState : MonoBehaviour
    {
        [Tooltip("被侦测目标 id（0 = 自动用物体实体 id；显式填写便于外部按固定 id 查询）")]
        [SerializeField] int _targetId;

        [Tooltip("当前曝光度 0~1：1=完全暴露在光下，0=全黑。L4 用它缩放察觉速率")]
        [Range(0f, 1f)]
        [SerializeField] float _exposure = 1f;

        [Tooltip("是否处于蹲伏/掩体后（L4）")]
        [SerializeField] bool _crouching;

        [Tooltip("察觉判定点相对脚底的高度（米）。用身体中部而非脚底：\n"
                 + "脚底点会在哨兵贴近时落到视线正下方，且掩体遮挡判定也会打在脚踝上。")]
        [SerializeField, Range(0f, 2f)] float _bodyHeight = 0.95f;

        /// <summary>当前累积察觉度 [0,1]。</summary>
        [SerializeField] float _suspicion;

        /// <summary>当前档位（由察觉度导出，序列化仅便于 Inspector 观察）。</summary>
        [SerializeField] StealthDetectionStage _stage = StealthDetectionStage.Patrol;

        /// <summary>目标 id。未显式填写时用物体实体 id。</summary>
        public int TargetId => _targetId != 0 ? _targetId : StealthIds.Of(gameObject);

        /// <summary>当前察觉度。</summary>
        public float Suspicion => _suspicion;

        /// <summary>当前档位。</summary>
        public StealthDetectionStage Stage => _stage;

        /// <summary>曝光度。</summary>
        public float Exposure
        {
            get => _exposure;
            set => _exposure = Mathf.Clamp01(value);
        }

        /// <summary>是否蹲伏。</summary>
        public bool Crouching
        {
            get => _crouching;
            set => _crouching = value;
        }

        /// <summary>
        /// 察觉判定点（世界）：脚底 + 身体高度，蹲伏时下移。
        /// 哨兵感知与自身感知<b>必须共用这一点</b>，否则贴脸时两侧判定不一致会来回翻转。
        /// </summary>
        public Vector3 BodyPoint
        {
            get
            {
                var h = _crouching ? _bodyHeight * 0.55f : _bodyHeight;
                return transform.position + Vector3.up * h;
            }
        }

        /// <summary>外部（状态机/工具）直接设察觉度，档位同步导出。</summary>
        public void SetSuspicion(float value, in AwarenessTuning tuning)
        {
            _suspicion = Mathf.Clamp01(value);
            _stage = AwarenessModel.StageOf(_suspicion, tuning);
        }

        /// <summary>推进一帧（Play 态内部使用；编辑态由适配层显式调用）。</summary>
        public void Tick(bool seen, float gainPerSecond, float lightSensitivity,
                        in AwarenessTuning tuning, StealthMask masks, float dt)
        {
            var scale = AwarenessModel.ExposureScale(lightSensitivity, _exposure, _crouching, masks);
            _suspicion = AwarenessModel.Integrate(_suspicion, dt, gainPerSecond, scale, seen, tuning);
            _stage = AwarenessModel.StageOf(_suspicion, tuning);
        }

        /// <summary>归零（「复原初始状态」，辅助台 T 周期定义的锚）。</summary>
        public void Reset()
        {
            _suspicion = 0f;
            _stage = StealthDetectionStage.Patrol;
            LastSeen = false;
            LastSeerGain = 0f;
        }

        /// <summary>上一判定周期是否被任一视野看到（辅助台「此刻是否暴露」的可视化用）。</summary>
        public bool LastSeen { get; private set; }

        /// <summary>看到自己的视野里最大的上升速率（辅助台可据此算「多少秒被发现」）。</summary>
        public float LastSeerGain { get; private set; }

        /// <summary>
        /// 评估并推进一次。<b>dt &lt;= 0 时只更新可见性判定、不改察觉度</b>——
        /// 编辑态回溯（scrub）属于「预览未来/过去」，绝不能污染真实累积状态。
        /// </summary>
        public void EvaluateOnce(float dt)
        {
            var volumes = VisionRegistry.Volumes;

            var seen = false;
            var bestGain = 0f;
            var bestLight = 0f;
            var bestTuning = default(AwarenessTuning);
            var fallbackTuning = default(AwarenessTuning);
            var hasSeer = false;
            var hasFallback = false;

            var self = BodyPoint;
            var masks = CurrentMasks();

            for (var i = 0; i < volumes.Count; i++)
            {
                var v = volumes[i];
                if (v == null || !v.Enabled) continue;
                if (v.gameObject == gameObject) continue;   // 自己不能看见自己（玩家挂视野时）

                // 回落速率与阈值取自「场上任意启用视野」：看不到玩家时也得知道该按多快回落，
                // 否则会用 default(阈值组)（回落速率 0）⇒ 察觉度永远不降，潜行无法脱身。
                if (!hasFallback && v.Profile != null)
                {
                    fallbackTuning = v.Profile.ToTuning();
                    hasFallback = true;
                }

                var seerId = StealthIds.Of(v.gameObject);
                if (!v.CanSee(self, QueryOptions.Full, seerId, transform)) continue;

                seen = true;
                if (!hasSeer || v.GainPerSecond > bestGain)
                {
                    hasSeer = true;
                    bestGain = v.GainPerSecond;
                    bestLight = v.LightSensitivity;
                    bestTuning = v.Profile != null ? v.Profile.ToTuning() : default;
                }
            }

            LastSeen = seen;
            LastSeerGain = bestGain;

            if (dt <= 0f) return;   // 纯预览：只更新可见性，不推进累积。

            // 被看见 → 用该侦察者的阈值组；没被看见 → 用场上任一启用视野的回落参数，
            // 否则 default 的 decayPerSecond=0 会让察觉度永久钉住（脱身无效的静默 bug）。
            var tuning = seen ? bestTuning : fallbackTuning;

            _suspicion = AwarenessModel.Integrate(
                _suspicion, dt, bestGain,
                AwarenessModel.ExposureScale(bestLight, _exposure, _crouching, masks),
                seen, tuning);
            _stage = AwarenessModel.StageOf(_suspicion, tuning);
        }

        /// <summary>
        /// 汇总当前生效的 L4 掩码。安全岛数据来自 core 的 ISafeIslandProvider
        /// （由设计辅助台注册；未注册时按「没有安全岛」降级，AGENTS §5 的唯一合法取用方式）。
        /// </summary>
        StealthMask CurrentMasks()
        {
            var masks = StealthMask.None;
            if (_crouching) masks |= StealthMask.CoverOrCrouch;

            if (Collin.Core.Services.TryGet<Collin.Core.Stealth.ISafeIslandProvider>(out var islands)
                && islands != null && islands.Count > 0 && islands.Contains(BodyPoint))
            {
                masks |= StealthMask.SafeIsland;
            }

            return masks;
        }

        void Update()
        {
            var dt = Time.deltaTime;

            // 回溯（手动）模式下不推进累积：此时场景里的位姿是预览值，不是真实历史。
            var clock = StealthClock.Instance;
            if (clock != null && clock.Mode == StealthClockMode.Manual)
            {
                EvaluateOnce(0f);
                return;
            }

            if (dt > 0f) EvaluateOnce(dt);
        }

        void OnEnable() => DetectionRegistry.MarkDirty();
        void OnDisable() => DetectionRegistry.MarkDirty();
    }
}
