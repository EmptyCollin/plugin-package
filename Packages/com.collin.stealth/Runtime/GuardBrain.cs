using Collin.Core.Stealth;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 哨兵决策（M3）：读自己视野与目标察觉度，驱动四档状态机（巡逻→疑惑→警觉→搜索→巡逻），
    /// 并作为 <see cref="IPatrolModifier"/> 把反应<b>叠加</b>在巡逻基线之上。
    /// <para>两条地基纪律：</para>
    /// <list type="number">
    /// <item>不写 transform、不改路线——只输出偏移，由 <see cref="PatrolAgent"/> 统一合成。
    /// 所以辅助台回溯预览拿到的永远是干净的巡逻基线（DESIGN R-1）。</item>
    /// <item>不自己开 LateUpdate——由 PatrolAgent 每帧先 Tick 决策、再合成位姿，
    /// 顺序确定；编辑态预览统一传 <c>dt = 0</c>，本组件只刷新感知读数、状态与叠加完全不动。</item>
    /// </list>
    /// <para>Unity 事件顺序保证：玩家 <c>DetectionState.Update</c>（Update 阶段）
    /// 早于本组件被 <see cref="PatrolAgent.LateUpdate"/> 驱动，故感知读到的目标位置
    /// 已含本帧玩家移动。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VisionVolume))]
    public sealed class GuardBrain : MonoBehaviour, IPatrolModifier
    {
        [Tooltip("阈值来源。留空自动取本物体视野体的 VisionProfile（调 Profile 滑条即改行为，所见即所得）")]
        [SerializeField] VisionProfile _tuningOverride;

        [Tooltip("追击/走向可疑点的移动速度（米/秒）")]
        [SerializeField, Min(0.5f)] float _moveSpeed = 3.5f;

        [Tooltip("察觉度归零后回巡逻基线的收速（米/秒）")]
        [SerializeField, Min(0.5f)] float _returnSpeed = 3f;

        [Tooltip("疑惑档在可疑点驻留的秒数，之后转搜索")]
        [SerializeField, Min(0f)] float _holdSuspicious = 2.5f;

        [Tooltip("搜索档绕可疑点环绕的秒数，之后回巡逻")]
        [SerializeField, Min(0f)] float _holdSearching = 6f;

        [Tooltip("搜索环绕半径（米）")]
        [SerializeField, Min(0.5f)] float _searchRadius = 4f;

        [Tooltip("转入 Alerted 时把该半径内的同伴拉到疑惑（米）；0 = 关闭同伴通报")]
        [SerializeField, Min(0f)] float _alertRadius = 15f;

        [Tooltip("可选：察觉事件通道资产。留空不广播（同伴通报仍生效，示例不建资产也能玩）")]
        [SerializeField] StealthDetectedChannel _detectedChannel;

        VisionVolume _volume;
        PatrolAgent _agent;

        StealthDetectionStage _stage = StealthDetectionStage.Patrol;
        Vector3 _lastSeenPoint;
        int _lastSeenTargetId = -1;
        float _stageTime;
        float _searchAngle;
        float _suspicion;

        Vector3 _offset;
        Vector3? _lookAt;

        static readonly System.Collections.Generic.List<GuardBrain> s_all =
            new System.Collections.Generic.List<GuardBrain>();

        /// <summary>
        /// 场景内全部哨兵决策（HUD 与复原调用枚举用）。
        /// <para>EditMode 不执行 OnEnable（本包实测坑），仅靠生命周期登记会让
        /// 编辑态读到空列表、复原/通报静默失效。这里做成<b>剪枝 + 空则重建</b>：
        /// 列表为内部缓冲，取用后勿长期持有。</para>
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<GuardBrain> All
        {
            get
            {
                s_all.RemoveAll(x => x == null);
                if (s_all.Count == 0)
                {
                    s_all.AddRange(UnityEngine.Object.FindObjectsByType<GuardBrain>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                }
                return s_all;
            }
        }

        public StealthDetectionStage Stage => _stage;
        public Vector3 LastSeenPoint => _lastSeenPoint;
        public float Suspicion => _suspicion;
        public int ActorId => StealthIds.Of(gameObject);

        /// <summary>是否处于干预状态（非巡逻档，或回位偏移尚未收敛完）。</summary>
        public bool Active => _stage != StealthDetectionStage.Patrol || _offset.sqrMagnitude > 1e-6f;

        /// <summary>
        /// 每帧推进（由 <see cref="PatrolAgent"/> 统一调用）。
        /// <paramref name="dt"/> &lt;= 0 为<b>纯预览</b>：只刷新感知读数，不改档位、不动叠加。
        /// </summary>
        public void Tick(float dt, Vector3 baselineWorldPosition, float timeIndex)
        {
            // ① 感知：取「我看得见、且累积察觉度最高」的目标。
            // 视野/巡逻体惰性取：EditMode 不执行 Awake/OnEnable（本包实测坑），
            // 组件被测试或工具直接驱动时不能依赖生命周期已跑过。
            if (_volume == null) _volume = GetComponent<VisionVolume>();
            if (_agent == null) _agent = GetComponent<PatrolAgent>();
            if (_volume == null) return;
            var myId = ActorId;
            DetectionState best = null;
            var bestSus = 0f;
            var targets = DetectionRegistry.Targets;
            for (var i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null) continue;
                // 判定点用 BodyPoint（身体中部）而非脚底：脚底点在贴脸时几乎落在视线正下方，
                // 会被俯仰闸门判为看不见 ⇒ 表现为「NPC 与玩家重叠后就丧失目标」。
                var aim = t.BodyPoint;
                if (!_volume.CanSee(aim, QueryOptions.Full, myId, t.transform)) continue;
                if (t.Suspicion >= bestSus)
                {
                    bestSus = t.Suspicion;
                    best = t;
                }
            }
            _suspicion = bestSus;

            if (best != null)
            {
                _lastSeenPoint = best.BodyPoint;
                _lastSeenTargetId = best.TargetId;
            }

            if (dt <= 0f) return;   // 预览止步：读数已刷新，状态机与叠加不动。

            // ② 状态机。阈值 ≤ 0 表示该档未启用（default 阈值组 = 全关，纯巡逻演示可用）。
            var tuning = ResolveTuning();
            var spotted = best != null;

            var prev = _stage;
            if (tuning.alertThreshold > 0f && _suspicion >= tuning.alertThreshold)
            {
                _stage = StealthDetectionStage.Alerted;
                _stageTime = 0f;
            }
            else if (tuning.suspicionThreshold > 0f && _suspicion >= tuning.suspicionThreshold)
            {
                if (_stage == StealthDetectionStage.Alerted && !spotted)
                {
                    // 刚丢失目标：警觉 → 直接进搜索（贴脸发现后逃跑的经典流转）。
                    _stage = StealthDetectionStage.Searching;
                    _stageTime = 0f;
                    _searchAngle = 0f;
                }
                else if (_stage != StealthDetectionStage.Alerted)
                {
                    if (_stage != StealthDetectionStage.Suspicious)
                    {
                        _stage = StealthDetectionStage.Suspicious;
                        _stageTime = 0f;
                    }
                    else
                    {
                        _stageTime += dt;
                        if (_stageTime >= _holdSuspicious && !spotted)
                        {
                            _stage = StealthDetectionStage.Searching;
                            _stageTime = 0f;
                            _searchAngle = 0f;
                        }
                    }
                }
                else
                {
                    _stageTime = 0f;   // 仍被看见（低察觉），维持警觉驻留
                }
            }
            else
            {
                // 未越阈值：按驻留计时链回落。_stageTime 只在单一分支里累加（避免双增）。
                _stageTime += dt;
                if (_stage == StealthDetectionStage.Alerted && _stageTime >= _holdSuspicious)
                {
                    _stage = StealthDetectionStage.Searching;
                    _stageTime = 0f;
                    _searchAngle = 0f;
                }
                else if (_stage == StealthDetectionStage.Suspicious && _stageTime >= _holdSuspicious)
                {
                    _stage = StealthDetectionStage.Searching;
                    _stageTime = 0f;
                    _searchAngle = 0f;
                }
                else if (_stage == StealthDetectionStage.Searching && _stageTime >= _holdSearching)
                {
                    _stage = StealthDetectionStage.Patrol;
                    _stageTime = 0f;
                }
            }

            // 转入 Alerted 的瞬间：广播 + 同伴通报。
            if (_stage == StealthDetectionStage.Alerted && prev != StealthDetectionStage.Alerted)
            {
                RaiseDetected(timeIndex);
                NotifyNeighbors();
            }

            // ③ 叠加收敛。期望偏移以<b>基线</b>为锚计算（以当前 transform 为锚会与基线循环依赖）。
            if (_stage == StealthDetectionStage.Patrol)
            {
                _offset = Vector3.MoveTowards(_offset, Vector3.zero, _returnSpeed * dt);
                if (_offset.sqrMagnitude > 1e-6f)
                {
                    _lookAt = baselineWorldPosition + _offset * 2f;   // 回位途中面朝回家方向
                }
                else
                {
                    _lookAt = null;
                }
                return;
            }

            var dest = _lastSeenPoint;
            if (_stage == StealthDetectionStage.Searching)
            {
                _searchAngle += 80f * dt;
                var a = _searchAngle * Mathf.Deg2Rad;
                dest = _lastSeenPoint + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _searchRadius;
            }

            // 可选寻路接缝：注册了 IPathBridge（如 stealth.Navigation 子程序集）时，
            // 把叠加目标吸附到导航表面；默认不注册 = 纯直线叠加（DESIGN §3.6）。
            // 注意只作用于叠加目标——巡逻基线永远是纯函数结果。
            var targetPoint = dest;
            if (Collin.Core.Services.TryGet<IPathBridge>(out var bridge) && bridge != null
                && bridge.TrySnap(baselineWorldPosition, dest, out var snapped))
            {
                targetPoint = snapped;
            }

            var desired = targetPoint - baselineWorldPosition;
            desired.y = 0f;   // 不接管垂直（导航吸附的纵向处理是 bridge 的职责）
            _offset = Vector3.MoveTowards(_offset, desired, _moveSpeed * dt);
            _lookAt = targetPoint;
        }

        public void GetOverlay(out Vector3 worldOffset, out Vector3? worldLookAt)
        {
            worldOffset = _offset;
            worldLookAt = _lookAt;
        }

        /// <summary>复原初始状态：清档位、清偏移、清记忆。</summary>
        public void ResetOverlay()
        {
            _stage = StealthDetectionStage.Patrol;
            _stageTime = 0f;
            _searchAngle = 0f;
            _suspicion = 0f;
            _lastSeenTargetId = -1;
            _offset = Vector3.zero;
            _lookAt = null;
        }

        void RaiseDetected(float timeIndex)
        {
            if (_detectedChannel == null) return;
            _detectedChannel.Raise(new StealthDetectedPayload
            {
                actorId = ActorId,
                targetId = _lastSeenTargetId,
                timeIndex = timeIndex,
                stage = (StealthAlertStage)_stage,
                suspicion = _suspicion
            });
        }

        /// <summary>Alerted 通报：半径内同伴升疑惑并同步可疑点（只升级、不降级同伴）。</summary>
        void NotifyNeighbors()
        {
            if (_alertRadius <= 0f) return;
            var r2 = _alertRadius * _alertRadius;
            var list = All;
            for (var i = 0; i < list.Count; i++)
            {
                var other = list[i];
                if (other == null || other == this) continue;
                if (other._stage >= StealthDetectionStage.Alerted) continue;
                if ((other.transform.position - _lastSeenPoint).sqrMagnitude > r2) continue;

                other._lastSeenPoint = _lastSeenPoint;
                other._lastSeenTargetId = _lastSeenTargetId;
                other._stage = StealthDetectionStage.Suspicious;
                other._stageTime = 0f;
            }
        }

        AwarenessTuning ResolveTuning()
        {
            if (_tuningOverride != null) return _tuningOverride.ToTuning();
            if (_volume != null && _volume.Profile != null) return _volume.Profile.ToTuning();
            return default;
        }

        void Awake()
        {
            _volume = GetComponent<VisionVolume>();
            _agent = GetComponent<PatrolAgent>();
        }

        void OnEnable()
        {
            if (_volume == null) _volume = GetComponent<VisionVolume>();
            if (_agent == null) _agent = GetComponent<PatrolAgent>();
            if (_agent != null) _agent.AddModifier(this);
            if (!s_all.Contains(this)) s_all.Add(this);
        }

        void OnDisable()
        {
            if (_agent != null) _agent.RemoveModifier(this);
            s_all.Remove(this);
        }
    }
}
