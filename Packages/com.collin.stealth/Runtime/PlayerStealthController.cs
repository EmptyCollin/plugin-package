using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 潜行玩家控制器（<b>拆箱即用</b>，DESIGN D-13）：与 CharacterController +
    /// <see cref="DetectionState"/> 一起打进 <c>StealthPlayer</c> 预制件，
    /// 拖进任意场景即可玩，<b>无需自建 InputAction 资产</b>。
    /// <para><b>操控语义（用户实机反馈后的定案，勿回退）</b>：</para>
    /// <list type="bullet">
    /// <item><b>W/A/S/D = 相机相对的四向移动</b>（A 向左前进、D 向右前进），
    /// 数学在 <see cref="ThirdPersonRig.MoveDirection"/>。
    /// 旧实现把「移动朝向」和「镜头朝向」共用同一个 <c>_yaw</c>，导致按 A/D 变成转镜头。</item>
    /// <item>角色模型朝向由移动方向决定，且与镜头角<b>各自独立</b>；
    /// 镜头角只由鼠标右键拖拽改。</item>
    /// <item><b>相机位置与朝向用同一个阻尼平滑</b>（都取自平滑后的 <c>_camYaw/_camPitch</c>）：
    /// 旧实现朝向瞬时赋值、位置却单独 Lerp，两者脱节 ⇒ 拖拽时画面抖动。</item>
    /// <item>相机更新放在 <c>LateUpdate</c>，避免与其他组件的 Update 顺序互相拉扯。</item>
    /// </list>
    /// <para>输入后端（实测）：本工程 <c>activeInputHandler: 1</c> = 仅新 Input System；
    /// 条件编译只写在<b>方法体内部</b>，写在方法外会让调用点在单后端工程里找不到方法。</para>
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerStealthController : MonoBehaviour
    {
        [Header("移动")]
        [SerializeField, Min(0.1f)] float _walkSpeed = 3.2f;
        [SerializeField, Min(0.1f)] float _sprintSpeed = 5.0f;
        [SerializeField, Min(0.1f)] float _crouchSpeed = 1.6f;
        [SerializeField, Min(1f)] float _gravity = 18f;
        [Tooltip("角色模型转向速度（阻尼系数，越大越跟手）")]
        [SerializeField, Min(1f)] float _turnStiffness = 14f;

        [Header("蹲伏")]
        [Tooltip("开=按 Ctrl 切换；关=按住才蹲")]
        [SerializeField] bool _toggleCrouch = true;
        [SerializeField, Min(0.5f)] float _standHeight = 1.8f;
        [SerializeField, Min(0.5f)] float _crouchHeight = 1.0f;
        [Tooltip("蹲伏时上报给 L4 的曝光度，越低越难被发现")]
        [SerializeField, Range(0f, 1f)] float _crouchExposure = 0.35f;

        [Header("场地边界（硬钳制，与可见围栏配套）")]
        [Tooltip("场地中心（水平面）。半宽任一维 ≤0 表示关闭边界，不限制移动")]
        [SerializeField] Vector3 _arenaCenter = Vector3.zero;
        [Tooltip("场地半宽 (x, z)。<b>默认 0 = 关闭</b>：预制件拖进任意场景不会凭空出现隐形墙，"
                 + "需要边界时由场景/生成器显式开启（示例场景为 27）")]
        [SerializeField] Vector2 _arenaHalfSize = Vector2.zero;
        [Tooltip("离墙内裕度（米）：留出角色半径，避免贴着墙面摩擦抖动")]
        [SerializeField, Range(0f, 2f)] float _arenaMargin = 0.55f;

        [Header("第三人称视角")]
        [Tooltip("留空自动取 Camera.main；无相机时本组件不管视角")]
        [SerializeField] Camera _followCamera;
        [Tooltip("按住鼠标右键拖拽转镜头")]
        [SerializeField] bool _allowOrbit = true;
        [SerializeField, Range(0.05f, 2f)] float _orbitSensitivity = 0.22f;
        [SerializeField] float _orbitPitchMin = -10f;
        [SerializeField] float _orbitPitchMax = 60f;
        [SerializeField, Min(0.5f)] float _cameraDistance = 5.2f;
        [Tooltip("观察枢轴相对脚底的高度比例")]
        [SerializeField, Range(0.2f, 2f)] float _cameraPivotRatio = 0.62f;
        [Tooltip("镜头跟随阻尼（位置与朝向共用，避免脱节抖动）")]
        [SerializeField, Min(1f)] float _cameraStiffness = 12f;

        static readonly RaycastHit[] s_camBuffer = new RaycastHit[4];

        CharacterController _cc;
        DetectionState _detection;
        Transform _capsuleVisual;
        Camera _camera;

        // 出生位姿（Awake 捕获，重开复位的真源）：允许检视覆盖，留默认即"作者摆在哪就从哪出生"。
        [Tooltip("出生位置覆盖。保留 (9999,9999,9999) 表示自动取场景里摆好的初始位置")]
        [SerializeField] Vector3 _spawnOverride = new Vector3(9999f, 9999f, 9999f);

        Vector3 _spawnPosition;
        Quaternion _spawnRotation;
        float _spawnPitch = 18f;
        bool _spawnCaptured;

        float _verticalVelocity;
        float _camYaw;
        float _camPitch = 18f;
        float _moveYaw;
        bool _crouching;
        bool _warnedNoInput;

        Vector3 _camPos;
        bool _camInitialized;

        public bool Crouching => _crouching;

        /// <summary>
        /// 冻结输入（对局结算时由 <see cref="StealthGameRule"/> 置位）。
        /// <para>出结果后若还能移动，玩家会在结算遮罩后面继续跑，
        /// 既看不清归因数据，也会让下一次重开的基线被污染。</para>
        /// </summary>
        public bool Frozen { get; set; }
        /// <summary>当前镜头水平角（辅助台与相机相关计算用）。</summary>
        public float CameraYaw => _camYaw;
        /// <summary>当前模型朝向角。</summary>
        public float MoveYaw => _moveYaw;
        public float CurrentSpeed => _crouching ? _crouchSpeed
            : (IsSprinting() ? _sprintSpeed : _walkSpeed);

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _detection = GetComponent<DetectionState>();
            _camera = _followCamera != null ? _followCamera : Camera.main;
            _capsuleVisual = transform.Find("CapsuleVisual");

            // 初始角取场景摆好的朝向（所见即所得）；镜头角与模型角同起点，
            // 避免第一帧镜头从别的方向甩过来。
            _camYaw = transform.eulerAngles.y;
            _moveYaw = _camYaw;
            _spawnPitch = _camPitch;
            CaptureSpawn();
            ApplyCapsuleShape();
        }

        /// <summary>
        /// 捕获出生位姿：<b>优先用检视里显式配的覆盖值</b>，否则取作者在当前场景摆好的
        /// 位置与朝向（所见即所得：编辑器里放哪，Play 就从哪开始）。
        /// <para>只在首次且未捕获过时写入，避免重开后再次 Awake 把"死亡点"当成新出生点。</para>
        /// </summary>
        void CaptureSpawn()
        {
            if (_spawnCaptured) return;
            var sentinel = new Vector3(9999f, 9999f, 9999f);
            _spawnPosition = _spawnOverride != sentinel ? _spawnOverride : transform.position;
            _spawnRotation = transform.rotation;
            _spawnCaptured = true;
        }

        void OnEnable()
        {
            if (_detection != null)
            {
                _detection.Exposure = _crouching ? _crouchExposure : 1f;
                _detection.Crouching = _crouching;
            }
            _camInitialized = false;
        }

        void Update()
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();

            // 结算冻结：忽略移动/蹲伏输入（镜头保持当前角度，画面稳定便于读数据）。
            // 蹲伏状态必须在解冻时重新同步，否则冻结前按住的 Ctrl 会残留。
            if (Frozen) return;

            if (_toggleCrouch)
            {
                if (ReadCrouchPressed()) SetCrouching(!_crouching);
            }
            else
            {
                SetCrouching(ReadCrouchHeld());
            }

            // ① 镜头角：只由鼠标改，不与移动耦合。
            if (_allowOrbit && IsRightDown())
            {
                var d = ReadMouseDelta();
                _camYaw = Mathf.Repeat(_camYaw + d.x * _orbitSensitivity, 360f);
                _camPitch = ThirdPersonRig.ClampPitch(
                    _camPitch - d.y * _orbitSensitivity, _orbitPitchMin, _orbitPitchMax);
            }

            // ② 移动：相机相对的平面方向（A=左移、D=右移、W=朝镜头前方）。
            var input = ReadMoveAxis();
            var worldDir = ThirdPersonRig.MoveDirection(_camYaw, input);

            // ③ 模型朝向跟随移动方向（仅在有输入时转，无输入保持）。
            if (worldDir.sqrMagnitude > 1e-6f)
            {
                var targetYaw = Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg;
                _moveYaw = ThirdPersonRig.SmoothAngle(_moveYaw, targetYaw, _turnStiffness, Time.deltaTime);
            }
            transform.rotation = Quaternion.Euler(0f, _moveYaw, 0f);

            // ④ 位移与重力。
            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity -= _gravity * Time.deltaTime;

            var velocity = worldDir * CurrentSpeed + Vector3.up * _verticalVelocity;
            _cc.Move(velocity * Time.deltaTime);

            // ⑤ 场地硬边界：可见围栏之外再兜一层。走 _cc.Move 而不是直接写 transform——
            // 后者会让控制器内部的碰撞状态与实际位置脱节，下一次 Move 可能把人挤过墙。
            var clamped = StealthArena.Clamp(transform.position, _arenaCenter, _arenaHalfSize, _arenaMargin);
            var back = clamped - transform.position;
            if (back.sqrMagnitude > 1e-8f) _cc.Move(back);
        }

        void LateUpdate()
        {
            UpdateCamera();
        }

        void UpdateCamera()
        {
            if (_camera == null) return;

            var dt = Time.deltaTime;
            var height = _crouching ? _crouchHeight : _standHeight;
            var pivot = transform.position + Vector3.up * height * _cameraPivotRatio;
            var rot = Quaternion.Euler(_camPitch, _camYaw, 0f);
            var desired = pivot - rot * Vector3.forward * _cameraDistance;

            // 防穿墙收距
            var dir = desired - pivot;
            var dist = dir.magnitude;
            if (dist > 0.01f)
            {
                var n = Physics.RaycastNonAlloc(pivot, dir / dist, s_camBuffer, dist);
                var nearest = dist;
                for (var i = 0; i < n; i++)
                {
                    var t = s_camBuffer[i].transform;
                    if (t == null) continue;
                    if (t == transform || t.IsChildOf(transform)) continue;
                    if (s_camBuffer[i].distance < nearest) nearest = s_camBuffer[i].distance;
                }
                if (nearest < dist - 0.2f) desired = pivot + dir / dist * Mathf.Max(0.6f, nearest - 0.2f);
            }

            if (!_camInitialized)
            {
                _camPos = desired;
                _camInitialized = true;
            }
            else
            {
                // 位置与朝向<b>共用同一阻尼</b>：朝向瞬时、位置缓动会脱节，拖拽时表现为画面抖动。
                var k = _cameraStiffness;
                _camPos.x = ThirdPersonRig.SmoothValue(_camPos.x, desired.x, k, dt);
                _camPos.y = ThirdPersonRig.SmoothValue(_camPos.y, desired.y, k, dt);
                _camPos.z = ThirdPersonRig.SmoothValue(_camPos.z, desired.z, k, dt);
            }

            _camera.transform.position = _camPos;
            _camera.transform.rotation = rot;
        }

        /// <summary>设蹲伏，同步胶囊形状与 L4 曝光度。</summary>
        public void SetCrouching(bool crouching)
        {
            _crouching = crouching;
            if (_detection != null)
            {
                _detection.Crouching = crouching;
                _detection.Exposure = crouching ? _crouchExposure : 1f;
            }
            ApplyCapsuleShape();
        }

        void ApplyCapsuleShape()
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();
            if (_cc == null) return;
            // 惰性解析：EditMode 不执行 Awake，测试与写工具直接调本方法时 _capsuleVisual 还是空，
            // 少这一步就会把摆位整段跳过（GuardBrain 同口径处理过这个坑）。
            if (_capsuleVisual == null) _capsuleVisual = transform.Find("CapsuleVisual");

            var h = Mathf.Max(0.5f, _crouching ? _crouchHeight : _standHeight);
            _cc.height = h;
            _cc.center = new Vector3(0f, h * 0.5f, 0f);

            if (_capsuleVisual != null)
            {
                // 纵向交给纯函数，x/z 保留作者摆好的粗细（所见即所得）。
                // 旧写法 localPosition.y = (h - 站高)/2 在站立时算出 0 ⇒ 胶囊中心落到脚底、
                // 下半截埋进地面（用户实机反馈「玩家一出场，胶囊体半个在地里」）。
                var pose = ThirdPersonRig.CapsuleVisualPose(h);
                var s = _capsuleVisual.localScale;
                var p = _capsuleVisual.localPosition;
                _capsuleVisual.localScale = new Vector3(s.x, pose.x, s.z);
                _capsuleVisual.localPosition = new Vector3(p.x, pose.y, p.z);
            }
        }

        /// <summary>
        /// 把玩家复位到<b>出生位姿</b>并清空本局运动状态（对局重开用）。
        /// <para>必须先停用 <c>CharacterController</c> 再写 transform：控制器启用时改 transform
        /// 会被物理层按上一帧的碰撞结果拉回（ teleport 常见坑），表现为「复位后又卡回原地」。</para>
        /// <para>相机阻尼基准同时作废（<c>_camInitialized=false</c>），否则重开瞬间镜头
        /// 会从死亡点一路缓动飞回出生点，观感像「没复位成功」。</para>
        /// </summary>
        public void ResetToSpawn()
        {
            if (_cc != null) _cc.enabled = false;
            transform.position = _spawnPosition;
            transform.rotation = _spawnRotation;
            if (_cc != null) _cc.enabled = true;

            _verticalVelocity = 0f;
            SetCrouching(false);

            _camYaw = _spawnRotation.eulerAngles.y;
            _moveYaw = _camYaw;
            _camPitch = _spawnPitch;
            _camInitialized = false;
        }

        /// <summary>是否已记录出生位姿（Awake 之后恒为真）。</summary>
        public bool HasSpawn => _spawnCaptured;

        /// <summary>当前记录的出生位置（只读，供调试与工具显示）。</summary>
        public Vector3 SpawnPosition => _spawnPosition;

        /// <summary>
        /// 把<b>当前</b>位姿记为出生点（覆盖已有记录）。
        /// <para>公开的原因：编辑模式测试与写工具不执行 <c>Awake</c>，若出生点只能由初始化时序
        /// 捕获，「重开回到出生点」这条就无法脱离 Play 态验证——本包的纪律正是几何与状态
        /// 必须能在编辑态断言。关卡作者同样可以在摆好位置后调用它定锚。</para>
        /// </summary>
        public void RecordSpawnPoint()
        {
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _spawnPitch = _camPitch;
            _spawnCaptured = true;
        }

        // ── 输入（条件编译只进方法体）───────────────────────────────

        enum InputBackend { NewSystem, Legacy, None }

        static InputBackend? s_backend;

        static InputBackend ResolveBackend()
        {
            if (s_backend.HasValue) return s_backend.Value;
#if ENABLE_INPUT_SYSTEM
            s_backend = InputBackend.NewSystem;
#elif ENABLE_LEGACY_INPUT_MANAGER
            s_backend = InputBackend.Legacy;
#else
            s_backend = InputBackend.None;
#endif
            return s_backend.Value;
        }

        bool IsSprinting()
        {
            switch (ResolveBackend())
            {
                case InputBackend.NewSystem:
#if ENABLE_INPUT_SYSTEM
                    var kb = UnityEngine.InputSystem.Keyboard.current;
                    return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
#else
                    return false;
#endif
                case InputBackend.Legacy:
#if ENABLE_LEGACY_INPUT_MANAGER
                    return UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        Vector2 ReadMoveAxis()
        {
            switch (ResolveBackend())
            {
                case InputBackend.NewSystem:
#if ENABLE_INPUT_SYSTEM
                    var k = UnityEngine.InputSystem.Keyboard.current;
                    if (k == null) return Vector2.zero;
                    var x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f)
                          - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
                    var y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f)
                          - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
                    return new Vector2(x, y);
#else
                    return Vector2.zero;
#endif
                case InputBackend.Legacy:
#if ENABLE_LEGACY_INPUT_MANAGER
                    return new Vector2(UnityEngine.Input.GetAxisRaw("Horizontal"),
                                       UnityEngine.Input.GetAxisRaw("Vertical"));
#else
                    return Vector2.zero;
#endif
                default:
                    WarnNoInputOnce();
                    return Vector2.zero;
            }
        }

        /// <summary>Ctrl 是否<b>本帧刚按下</b>（切换模式用）。</summary>
        static bool ReadCrouchPressed()
        {
            switch (ResolveBackend())
            {
                case InputBackend.NewSystem:
#if ENABLE_INPUT_SYSTEM
                    var kb = UnityEngine.InputSystem.Keyboard.current;
                    if (kb == null) return false;
                    return kb.leftCtrlKey.wasPressedThisFrame || kb.rightCtrlKey.wasPressedThisFrame;
#else
                    return false;
#endif
                case InputBackend.Legacy:
#if ENABLE_LEGACY_INPUT_MANAGER
                    return UnityEngine.Input.GetKeyDown(KeyCode.LeftControl)
                        || UnityEngine.Input.GetKeyDown(KeyCode.RightControl);
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        /// <summary>Ctrl 是否<b>正在按住</b>（按住模式用）。</summary>
        static bool ReadCrouchHeld()
        {
            switch (ResolveBackend())
            {
                case InputBackend.NewSystem:
#if ENABLE_INPUT_SYSTEM
                    var kb = UnityEngine.InputSystem.Keyboard.current;
                    if (kb == null) return false;
                    return kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
#else
                    return false;
#endif
                case InputBackend.Legacy:
#if ENABLE_LEGACY_INPUT_MANAGER
                    return UnityEngine.Input.GetKey(KeyCode.LeftControl)
                        || UnityEngine.Input.GetKey(KeyCode.RightControl);
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        static bool IsRightDown()
        {
            switch (ResolveBackend())
            {
                case InputBackend.NewSystem:
#if ENABLE_INPUT_SYSTEM
                    var m = UnityEngine.InputSystem.Mouse.current;
                    return m != null && m.rightButton.isPressed;
#else
                    return false;
#endif
                case InputBackend.Legacy:
#if ENABLE_LEGACY_INPUT_MANAGER
                    return UnityEngine.Input.GetMouseButton(1);
#else
                    return false;
#endif
                default:
                    return false;
            }
        }

        static Vector2 ReadMouseDelta()
        {
            switch (ResolveBackend())
            {
                case InputBackend.NewSystem:
#if ENABLE_INPUT_SYSTEM
                    var mo = UnityEngine.InputSystem.Mouse.current;
                    if (mo == null) return Vector2.zero;
                    // delta 已是「本帧像素位移」，直接交给灵敏度，不再叠经验系数。
                    var d = mo.delta.ReadValue();
                    return new Vector2(d.x, d.y);
#else
                    return Vector2.zero;
#endif
                case InputBackend.Legacy:
#if ENABLE_LEGACY_INPUT_MANAGER
                    // 旧 Axis 已按「每帧像素 / 约 1 单位」归一，放大到与新后端同量级。
                    return new Vector2(UnityEngine.Input.GetAxis("Mouse X"),
                                       UnityEngine.Input.GetAxis("Mouse Y")) * 6f;
#else
                    return Vector2.zero;
#endif
                default:
                    return Vector2.zero;
            }
        }

        void WarnNoInputOnce()
        {
            if (_warnedNoInput) return;
            _warnedNoInput = true;
            Debug.LogWarning("[PlayerStealthController] 新 Input System 与旧输入后端都不可用，"
                             + "请在 Project Settings ▸ Player ▸ Active Input Handling 选择 "
                             + "Input System 或 Both，否则玩家无法操作。");
        }

        void OnValidate()
        {
            _walkSpeed = Mathf.Max(0.1f, _walkSpeed);
            _sprintSpeed = Mathf.Max(_walkSpeed, _sprintSpeed);
            _crouchSpeed = Mathf.Max(0.1f, _crouchSpeed);
            _gravity = Mathf.Max(1f, _gravity);
            _turnStiffness = Mathf.Max(1f, _turnStiffness);
            _standHeight = Mathf.Max(0.5f, _standHeight);
            _crouchHeight = Mathf.Clamp(_crouchHeight, 0.5f, _standHeight);
            _cameraDistance = Mathf.Max(0.5f, _cameraDistance);
            _cameraStiffness = Mathf.Max(1f, _cameraStiffness);
            if (_orbitPitchMax < _orbitPitchMin)
            {
                var tmp = _orbitPitchMin;
                _orbitPitchMin = _orbitPitchMax;
                _orbitPitchMax = tmp;
            }
        }
    }
}
