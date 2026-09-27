using UnityEngine;
using UnityEngine.InputSystem;

namespace Voron.Player
{
    public sealed class PlayerInputReader : MonoBehaviour
    {
        private InputActionMap _gameplayMap;
        private InputActionMap _interfaceMap;
        private InputAction _move;
        private InputAction _look;
        private InputAction _run;
        private InputAction _crouch;
        private InputAction _interact;
        private InputAction _inventory;
        private InputAction _flashlight;
        private InputAction _navigate;
        private InputAction _submit;
        private InputAction _cancel;
        private InputAction _pointerPosition;
        private InputAction _pointerClick;

        private bool _interactPressed;
        private bool _inventoryPressed;
        private bool _flashlightPressed;
        private bool _submitPressed;
        private bool _cancelPressed;
        private bool _pointerPressed;

        public Vector2 Move => ReadVector2(_move);
        public Vector2 Look => ReadVector2(_look);
        public bool LookIsPointer { get; private set; } = true;
        public bool RunHeld => IsPressed(_run);
        public bool CrouchHeld => IsPressed(_crouch);
        public bool InteractPressed => _interactPressed;
        public bool InventoryPressed => _inventoryPressed;
        public bool CancelPressed => _cancelPressed;
        public bool FlashlightPressed => _flashlightPressed;
        public Vector2 Navigate => ReadVector2(_navigate);
        public bool SubmitPressed => _submitPressed;
        public bool UsingGamepad { get; private set; }
        public Vector2 PointerPosition => ReadVector2(_pointerPosition);
        public bool PointerPressed => _pointerPressed;

        private void OnEnable()
        {
            CreateActionMaps();
            _gameplayMap.Enable();
            _interfaceMap.Enable();
        }

        private void LateUpdate()
        {
            ResetPressedActions();
        }

        private void OnDisable()
        {
            DisposeActionMaps();
            ResetPressedActions();
        }

        private void OnDestroy()
        {
            DisposeActionMaps();
        }

        private void CreateActionMaps()
        {
            if (_gameplayMap != null)
            {
                return;
            }

            _gameplayMap = new InputActionMap("Gameplay");
            _move = _gameplayMap.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _move.AddBinding("<Gamepad>/leftStick");

            _look = _gameplayMap.AddAction("Look", InputActionType.Value, expectedControlLayout: "Vector2");
            _look.AddBinding("<Mouse>/delta");
            _look.AddBinding("<Gamepad>/rightStick");
            _move.performed += OnMovementPerformed;
            _look.performed += OnLookPerformed;

            _run = _gameplayMap.AddAction("Run", InputActionType.Button);
            _run.AddBinding("<Keyboard>/leftShift");
            _run.AddBinding("<Gamepad>/leftStickPress");

            _crouch = _gameplayMap.AddAction("Crouch", InputActionType.Button);
            _crouch.AddBinding("<Keyboard>/leftCtrl");
            _crouch.AddBinding("<Gamepad>/rightStickPress");

            _interact = _gameplayMap.AddAction("Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonSouth");
            _interact.performed += OnInteractPerformed;

            _inventory = _gameplayMap.AddAction("Inventory", InputActionType.Button);
            _inventory.AddBinding("<Keyboard>/tab");
            _inventory.AddBinding("<Keyboard>/i");
            _inventory.AddBinding("<Gamepad>/buttonNorth");
            _inventory.performed += OnInventoryPerformed;

            _flashlight = _gameplayMap.AddAction("Flashlight", InputActionType.Button);
            _flashlight.AddBinding("<Keyboard>/f");
            _flashlight.AddBinding("<Gamepad>/buttonWest");
            _flashlight.performed += OnFlashlightPerformed;

            _interfaceMap = new InputActionMap("Interface");
            _navigate = _interfaceMap.AddAction("Navigate", InputActionType.Value, expectedControlLayout: "Vector2");
            _navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _navigate.AddBinding("<Gamepad>/dpad");
            _navigate.AddBinding("<Gamepad>/leftStick");
            _navigate.performed += OnNavigationPerformed;

            _submit = _interfaceMap.AddAction("Submit", InputActionType.Button);
            _submit.AddBinding("<Keyboard>/enter");
            _submit.AddBinding("<Keyboard>/space");
            _submit.AddBinding("<Gamepad>/buttonSouth");
            _submit.performed += OnSubmitPerformed;

            _cancel = _interfaceMap.AddAction("Cancel", InputActionType.Button);
            _cancel.AddBinding("<Keyboard>/escape");
            _cancel.AddBinding("<Gamepad>/buttonEast");
            _cancel.performed += OnCancelPerformed;

            _pointerPosition = _interfaceMap.AddAction("PointerPosition", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            _pointerPosition.AddBinding("<Mouse>/position");
            _pointerPosition.performed += OnPointerMoved;

            _pointerClick = _interfaceMap.AddAction("PointerClick", InputActionType.Button);
            _pointerClick.AddBinding("<Mouse>/leftButton");
            _pointerClick.performed += OnPointerClickPerformed;
        }

        private void DisposeActionMaps()
        {
            if (_gameplayMap != null)
            {
                _gameplayMap.Disable();
                _gameplayMap.Dispose();
                _gameplayMap = null;
            }

            if (_interfaceMap != null)
            {
                _interfaceMap.Disable();
                _interfaceMap.Dispose();
                _interfaceMap = null;
            }

            _move = null;
            _look = null;
            _run = null;
            _crouch = null;
            _interact = null;
            _inventory = null;
            _flashlight = null;
            _navigate = null;
            _submit = null;
            _cancel = null;
            _pointerPosition = null;
            _pointerClick = null;
        }

        private void OnMovementPerformed(InputAction.CallbackContext context)
        {
            SetLastInput(context);
        }

        private void OnLookPerformed(InputAction.CallbackContext context)
        {
            SetLastInput(context);
            LookIsPointer = context.control != null && context.control.device is Mouse;
        }

        private void OnNavigationPerformed(InputAction.CallbackContext context)
        {
            SetLastInput(context);
        }

        private void OnInteractPerformed(InputAction.CallbackContext context)
        {
            SetLastInput(context);
            _interactPressed = true;
        }

        private void OnInventoryPerformed(InputAction.CallbackContext context)
        {
            SetLastInput(context);
            _inventoryPressed = true;
        }

        private void OnFlashlightPerformed(InputAction.CallbackContext context)
        {
            SetLastInput(context);
            _flashlightPressed = true;
        }

        private void OnSubmitPerformed(InputAction.CallbackContext context)
        {
            SetLastInput(context);
            _submitPressed = true;
        }

        private void OnCancelPerformed(InputAction.CallbackContext context)
        {
            SetLastInput(context);
            _cancelPressed = true;
        }

        private void OnPointerMoved(InputAction.CallbackContext context)
        {
            SetLastInput(context);
        }

        private void OnPointerClickPerformed(InputAction.CallbackContext context)
        {
            SetLastInput(context);
            _pointerPressed = true;
        }

        private void SetLastInput(InputAction.CallbackContext context)
        {
            if (context.control == null)
            {
                return;
            }

            InputDevice device = context.control.device;
            if (device is Gamepad)
            {
                UsingGamepad = true;
            }
            else if (device is Keyboard || device is Mouse)
            {
                UsingGamepad = false;
            }
        }

        private static Vector2 ReadVector2(InputAction action)
        {
            return action != null && action.enabled ? action.ReadValue<Vector2>() : Vector2.zero;
        }

        private static bool IsPressed(InputAction action)
        {
            return action != null && action.enabled && action.IsPressed();
        }

        private void ResetPressedActions()
        {
            _interactPressed = false;
            _inventoryPressed = false;
            _flashlightPressed = false;
            _submitPressed = false;
            _cancelPressed = false;
            _pointerPressed = false;
        }
    }
}
