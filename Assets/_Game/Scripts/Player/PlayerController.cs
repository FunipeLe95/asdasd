using UnityEngine;
using Voron.UI;
using Voron.Visuals;

namespace Voron.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float walkSpeed = 3.2f;
        [SerializeField, Min(0f)] private float runSpeed = 5.2f;
        [SerializeField, Min(0f)] private float crouchSpeed = 1.8f;
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.08f;
        [SerializeField, Min(0f)] private float gamepadSensitivity = 150f;
        [SerializeField, Min(0f)] private float gravity = 22f;
        [SerializeField, Min(0.1f)] private float crouchHeight = 1.15f;
        [SerializeField, Min(0f)] private float crouchCameraOffset = 0.42f;
        [SerializeField] private bool invertLookY = false;
        [SerializeField] private bool ensurePS1CameraEffect = true;

        private CharacterController _characterController;
        private readonly RaycastHit[] _standClearanceHits = new RaycastHit[16];
        private readonly Collider[] _standOverlapHits = new Collider[16];
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private InventoryUI _inventoryUI;
        private InventoryUI _subscribedInventoryUI;
        private float _standingHeight;
        private float _cameraStandingY;
        private float _verticalVelocity;
        private float _pitch;
        private bool _cursorModeInitialized;
        private bool _cursorReleasedByUser;
        private bool _applicationHasFocus = true;
        private int _inventoryToggleFrame = -1;

        public bool IsMovementSuppressed => IsInventoryOpen || _cursorReleasedByUser || !_applicationHasFocus;
        public bool IsCursorReleased => _cursorReleasedByUser;

        private void Awake()
        {
            CacheControllerAndCameraState();
            EnsureCameraEffect();
        }

        private void OnEnable()
        {
            SubscribeToInventory();
            ApplyCursorMode();
        }

        private void Update()
        {
            if (_input == null || _cameraTransform == null || _characterController == null)
            {
                return;
            }

            HandleCursorInput();
            ApplyCursorMode();
            if (IsMovementSuppressed)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            UpdateCrouch(deltaTime);
            UpdateLook(deltaTime);
            UpdateMovement(deltaTime);
        }

        private void OnDisable()
        {
            if (_subscribedInventoryUI != null)
            {
                _subscribedInventoryUI.Toggled -= OnInventoryToggled;
                _subscribedInventoryUI = null;
            }

            if (Application.isPlaying)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            _cursorModeInitialized = false;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _applicationHasFocus = hasFocus;
            _cursorModeInitialized = false;

            if (!hasFocus && Application.isPlaying)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (hasFocus)
            {
                ApplyCursorMode();
            }
        }

        public void Configure(PlayerInputReader inputReader, Transform cameraTransform, InventoryUI inventoryUI)
        {
            _input = inputReader;
            _cameraTransform = cameraTransform;
            _characterController = GetComponent<CharacterController>();
            CacheControllerAndCameraState();
            EnsureCameraEffect();

            if (_inventoryUI != inventoryUI)
            {
                if (_subscribedInventoryUI != null)
                {
                    _subscribedInventoryUI.Toggled -= OnInventoryToggled;
                    _subscribedInventoryUI = null;
                }

                _inventoryUI = inventoryUI;
                SubscribeToInventory();
            }

            ApplyCursorMode();
        }

        private void CacheControllerAndCameraState()
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }

            if (_characterController != null)
            {
                _standingHeight = _characterController.height;
            }

            if (_cameraTransform != null)
            {
                _cameraStandingY = _cameraTransform.localPosition.y;
                _pitch = NormalizeAngle(_cameraTransform.localEulerAngles.x);
            }
        }

        private void EnsureCameraEffect()
        {
            if (!ensurePS1CameraEffect || _cameraTransform == null || _cameraTransform.GetComponent<Camera>() == null)
            {
                return;
            }

            PS1CameraEffect effect = _cameraTransform.GetComponent<PS1CameraEffect>();
            if (effect == null)
            {
                effect = _cameraTransform.gameObject.AddComponent<PS1CameraEffect>();
                Shader effectShader = Shader.Find("Hidden/Voron/PS1CameraEffect");
                if (effectShader != null)
                {
                    effect.Configure(effectShader);
                }
            }
        }

        private void SubscribeToInventory()
        {
            if (isActiveAndEnabled && _inventoryUI != null && _subscribedInventoryUI != _inventoryUI)
            {
                _inventoryUI.Toggled += OnInventoryToggled;
                _subscribedInventoryUI = _inventoryUI;
            }
        }

        private void OnInventoryToggled(bool isOpen)
        {
            _inventoryToggleFrame = Time.frameCount;
            _cursorReleasedByUser = false;
            ApplyCursorMode();
        }

        private void ApplyCursorMode()
        {
            if (!Application.isPlaying || _input == null || _cameraTransform == null || !_applicationHasFocus)
            {
                return;
            }

            bool inventoryOpen = IsInventoryOpen;
            bool shouldLock = !inventoryOpen && !_cursorReleasedByUser;
            CursorLockMode lockMode = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
            bool cursorVisible = _cursorReleasedByUser || (inventoryOpen && !_input.UsingGamepad);

            if (_cursorModeInitialized && Cursor.lockState == lockMode && Cursor.visible == cursorVisible)
            {
                return;
            }

            Cursor.lockState = lockMode;
            Cursor.visible = cursorVisible;
            _cursorModeInitialized = true;
        }

        private void UpdateCrouch(float deltaTime)
        {
            float crouchedHeight = Mathf.Min(crouchHeight, _standingHeight);
            float targetHeight = _input.CrouchHeld
                ? crouchedHeight
                : CanStandUp() ? _standingHeight : Mathf.Min(_characterController.height, crouchedHeight);
            float bottom = _characterController.center.y - _characterController.height * 0.5f;
            _characterController.height = Mathf.MoveTowards(_characterController.height, targetHeight, 5f * deltaTime);

            Vector3 center = _characterController.center;
            center.y = bottom + _characterController.height * 0.5f;
            _characterController.center = center;

            if (_cameraTransform != null)
            {
                float crouchAmount = Mathf.Clamp01((_standingHeight - _characterController.height) / Mathf.Max(0.01f, _standingHeight - crouchHeight));
                Vector3 cameraPosition = _cameraTransform.localPosition;
                cameraPosition.y = _cameraStandingY - crouchCameraOffset * crouchAmount;
                _cameraTransform.localPosition = cameraPosition;
            }
        }

        public bool CanStandUp()
        {
            if (_characterController == null)
            {
                _characterController = GetComponent<CharacterController>();
            }

            if (_characterController == null)
            {
                return false;
            }

            if (_standingHeight <= 0f)
            {
                _standingHeight = _characterController.height;
            }

            Vector3 scale = _characterController.transform.lossyScale;
            float scaleY = Mathf.Abs(scale.y);
            float worldRadius = _characterController.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            Vector3 worldCenter = _characterController.transform.TransformPoint(_characterController.center);
            float currentWorldHeight = _characterController.height * scaleY;
            float standingWorldHeight = _standingHeight * scaleY;
            float currentTopOffset = Mathf.Max(0f, currentWorldHeight * 0.5f - worldRadius);
            float standingTopOffset = Mathf.Max(0f, standingWorldHeight * 0.5f - worldRadius);
            Vector3 currentTop = worldCenter + Vector3.up * currentTopOffset;
            float centerRise = (standingWorldHeight - currentWorldHeight) * 0.5f;
            Vector3 standingTop = worldCenter + Vector3.up * (centerRise + standingTopOffset);
            Vector3 rise = standingTop - currentTop;
            float distance = rise.magnitude;

            if (distance <= 0.001f)
            {
                return true;
            }

            int castCount = Physics.SphereCastNonAlloc(
                currentTop,
                worldRadius,
                rise / distance,
                _standClearanceHits,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            if (castCount >= _standClearanceHits.Length)
            {
                return false;
            }

            for (int i = 0; i < castCount; i++)
            {
                Transform hitTransform = _standClearanceHits[i].collider != null ? _standClearanceHits[i].collider.transform : null;
                if (IsExternalCollider(hitTransform))
                {
                    return false;
                }
            }

            int overlapCount = Physics.OverlapSphereNonAlloc(
                standingTop,
                worldRadius,
                _standOverlapHits,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            if (overlapCount >= _standOverlapHits.Length)
            {
                return false;
            }

            for (int i = 0; i < overlapCount; i++)
            {
                Transform hitTransform = _standOverlapHits[i] != null ? _standOverlapHits[i].transform : null;
                if (IsExternalCollider(hitTransform))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsExternalCollider(Transform candidate)
        {
            return candidate != null && candidate != transform && !candidate.IsChildOf(transform);
        }

        private bool IsInventoryOpen => _inventoryUI != null && _inventoryUI.IsOpen;

        private void HandleCursorInput()
        {
            // The modal may consume Escape before or after this component's Update.
            if (IsInventoryOpen || _inventoryToggleFrame == Time.frameCount)
            {
                return;
            }

            if (_input.CancelPressed && !_input.UsingGamepad)
            {
                _cursorReleasedByUser = true;
                _cursorModeInitialized = false;
                return;
            }

            if (_cursorReleasedByUser && !IsInventoryOpen && _input.PointerPressed)
            {
                _cursorReleasedByUser = false;
                _cursorModeInitialized = false;
            }
        }

        private void UpdateLook(float deltaTime)
        {
            Vector2 look = _input.Look;
            float scale = _input.LookIsPointer ? mouseSensitivity : gamepadSensitivity * deltaTime;
            float pitchDirection = invertLookY ? 1f : -1f;

            transform.Rotate(Vector3.up, look.x * scale, Space.Self);
            _pitch = Mathf.Clamp(_pitch + look.y * scale * pitchDirection, -85f, 85f);
            _cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void UpdateMovement(float deltaTime)
        {
            Vector2 input = Vector2.ClampMagnitude(_input.Move, 1f);
            Vector3 movement = transform.right * input.x + transform.forward * input.y;

            float speed = _input.CrouchHeld
                ? crouchSpeed
                : _input.RunHeld ? runSpeed : walkSpeed;

            if (_characterController.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }

            _verticalVelocity -= gravity * deltaTime;
            _characterController.Move((movement * speed + Vector3.up * _verticalVelocity) * deltaTime);
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
