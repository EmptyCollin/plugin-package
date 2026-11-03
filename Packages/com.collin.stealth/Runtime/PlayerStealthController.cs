using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 潜行玩家控制器（<b>拆箱即用</b>，DESIGN D-13）：与 CharacterController +
    /// <see cref="DetectionState"/> 一起打进 <c>StealthPlayer</c> 预制件，
    /// 拖进任意场景即可 WASD 移动、Ctrl 蹲伏，<b>无需自建 InputAction 资产</b>。
    /// <para>输入后端（实测）：本工程 <c>ProjectSettings.asset</c> 的
    /// <c>activeInputHandler: 1</c> = 仅新 Input System，旧 <c>Input.*</c> 运行时不可用。
    /// 故优先轮询 <c>Keyboard.current</c>，并按 <c>ENABLE_LEGACY_INPUT_MANAGER</c> 回退旧后端；
    /// 两路都不可用时输出<b>一次性明确 warning</b>，绝不静默失灵。</para>
    /// <para>条件编译只写在<b>方法体内部</b>：写在方法外会让调用点在单后端工程里找不到方法。</para>
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerStealthController : MonoBehaviour
    {
        [Header("移动")]
        [SerializeField, Min(0.1f)] float _walkSpeed = 3.2f;
        [SerializeField, Min(0.1f)] float _crouchSpeed = 1.6f;
        [SerializeField, Min(1f)] float _gravity = 18f;
        [Tooltip("朝向跟随输入方向的转速（度/秒）")]
        [SerializeField, Min(1f)] float _turnSpeed = 720f;

        [Header("蹲伏")]
        [Tooltip("按下 Ctrl 即切换（而非按住才蹲）")]
        [SerializeField] bool _toggleCrouch = true;
        [SerializeField, Min(0.5f)] float _standHeight = 1.8f;
        [SerializeField, Min(0.5f)] float _crouchHeight = 1.0f;
        [Tooltip("蹲伏时上报给 L4 的曝光度，越低越难被发现")]
        [SerializeField, Range(0f, 1f)] float _crouchExposure = 0.35f;

        [Header("第三人称视角（可选）")]
        [Tooltip("留空自动取 Camera.main；无相机时本组件不管视角")]
        [SerializeField] Camera _followCamera;
        [Tooltip("右键拖拽环绕视角（不需要鼠标锁定）")]
        [SerializeField] bool _allowOrbit = true;
        [SerializeField, Range(0.2f, 10f)] float _orbitSensitivity = 2f;
        [SerializeField] float _orbitPitchMin = -15f;
        [SerializeField] float _orbitPitchMax = 65f;
        [SerializeField, Min(0.5f)] float _cameraDistance = 5f;
        [Tooltip("观察枢轴相对脚底的高度（按站立/蹲伏自动缩放）")]
        [SerializeField, Range(0.2f, 2f)] float _cameraPivotRatio = 0.6f;

        static readonly RaycastHit[] s_camBuffer = new RaycastHit[4];

        CharacterController _cc;
        DetectionState _detection;
        Transform _capsuleVisual;
        Camera _camera;

        float _verticalVelocity;
        float _yaw = 180f;
        float _pitch = 18f;
        bool _crouching;
        bool _warnedNoInput;

        public bool Crouching => _crouching;
        public float WalkSpeed => _walkSpeed;
        public float CurrentSpeed => _crouching ? _crouchSpeed : _walkSpeed;
        public float Yaw => _yaw;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _detection = GetComponent<DetectionState>();
            _camera = _followCamera != null ? _followCamera : Camera.main;
            var child = transform.Find("CapsuleVisual");
            _capsuleVisual = child;

            // 初始朝向取场景里摆好的 transform（所见即所得：在 Scene 里把玩家转向哪里，
            // 出生就朝哪里）。写死 180° 会让背对关卡的出生点看不见任何内容。
            _yaw = transform.eulerAngles.y;
            ApplyCapsuleShape();
        }

        void OnEnable()
        {
            if (_detection != null)
            {
                _detection.Exposure = _crouching ? _crouchExposure : 1f;
                _detection.Crouching = _crouching;
            }
        }

        void Update()
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();

            var move = ReadMoveAxis();
            if (_toggleCrouch)
            {
                // 切换模式：按下即翻转（按住不放不会反复横跳）。
                if (ReadCrouchPressed()) SetCrouching(!_crouching);
            }
            else
            {
                // 按住模式：按住才蹲，松开即站起。
                SetCrouching(ReadCrouchHeld());
            }

            var planar = new Vector3(move.x, 0f, move.y);
            if (planar.sqrMagnitude > 1f) planar.Normalize();

            if (planar.sqrMagnitude > 1e-4f)
            {
                var targetYaw = Mathf.Atan2(planar.x, planar.z) * Mathf.Rad2Deg;
                _yaw = Mathf.Repeat(Mathf.MoveTowardsAngle(_yaw, targetYaw, _turnSpeed * Time.deltaTime), 360f);
            }
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            if (_allowOrbit && IsRightDown())
            {
                var d = ReadMouseDelta();
                _yaw = Mathf.Repeat(_yaw - d.x * _orbitSensitivity, 360f);
                _pitch = Mathf.Clamp(_pitch - d.y * _orbitSensitivity, _orbitPitchMin, _orbitPitchMax);
            }

            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity -= _gravity * Time.deltaTime;

            var velocity = planar * CurrentSpeed + Vector3.up * _verticalVelocity;
            _cc.Move(velocity * Time.deltaTime);

            FollowCamera();
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
            if (_cc == null) return;
            var h = Mathf.Max(0.5f, _crouching ? _crouchHeight : _standHeight);
            _cc.height = h;
            _cc.center = new Vector3(0f, h * 0.5f, 0f);

            if (_capsuleVisual != null)
            {
                var ratio = h / Mathf.Max(0.5f, _standHeight);
                _capsuleVisual.localScale = new Vector3(1f, ratio, 1f);
                _capsuleVisual.localPosition = new Vector3(0f, (h - _standHeight) * 0.5f, 0f);
            }
        }

        void FollowCamera()
        {
            if (_camera == null) return;

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            var height = _crouching ? _crouchHeight : _standHeight;
            var pivot = transform.position + Vector3.up * height * _cameraPivotRatio;
            var desired = pivot - rot * Vector3.forward * _cameraDistance;

            // 防穿墙：枢轴→期望点之间若被遮挡，收到命中点前。
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

            _camera.transform.position = Vector3.Lerp(_camera.transform.position, desired, Time.deltaTime * 10f);
            _camera.transform.rotation = rot;
        }

        // ── 输入：三档后端自动选择（条件编译只进方法体）───────────

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
                    var d = mo.delta.ReadValue();
                    // 像素 delta 缩到与旧 Axis 同量级，避免两套后端手感差异。
                    return new Vector2(d.x * 0.15f, d.y * 0.15f);
#else
                    return Vector2.zero;
#endif
                case InputBackend.Legacy:
#if ENABLE_LEGACY_INPUT_MANAGER
                    return new Vector2(UnityEngine.Input.GetAxis("Mouse X"),
                                       UnityEngine.Input.GetAxis("Mouse Y"));
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
            _crouchSpeed = Mathf.Max(0.1f, _crouchSpeed);
            _gravity = Mathf.Max(1f, _gravity);
            _turnSpeed = Mathf.Max(1f, _turnSpeed);
            _standHeight = Mathf.Max(0.5f, _standHeight);
            _crouchHeight = Mathf.Clamp(_crouchHeight, 0.5f, _standHeight);
            _cameraDistance = Mathf.Max(0.5f, _cameraDistance);
        }
    }
}
