using UnityEngine;
using System;
using Voron.Inventory;
using Voron.Player;
using Voron.UI;

namespace Voron.Interaction
{
    public sealed class InteractionSystem : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float interactionDistance = 2.4f;
        [SerializeField] private LayerMask raycastLayers = Physics.DefaultRaycastLayers;

        [SerializeField] private Camera _camera;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private InventorySystem _inventory;
        [SerializeField] private InventoryUI _inventoryUI;
        [SerializeField] private PrototypeHUD _hud;
        private InteractionContext _context;
        private MonoBehaviour _targetComponent;
        private IInteractable _target;
        private string _lastPrompt;

        public IInteractable CurrentTarget => _targetComponent != null ? _target : null;
        public InteractionContext Context => _context;

        private void Awake()
        {
            RebuildContext();
        }

        private void OnEnable()
        {
            RebuildContext();
        }

        private void Update()
        {
            if (_input != null && _input.InteractPressed)
            {
                TryInteractCurrent();
                return;
            }

            ScanForInteractable();
        }

        public void Configure(
            Camera playerCamera,
            PlayerInputReader inputReader,
            InventorySystem inventory,
            InventoryUI inventoryUI,
            PrototypeHUD hud)
        {
            _camera = playerCamera;
            _input = inputReader;
            _inventory = inventory;
            _inventoryUI = inventoryUI;
            _hud = hud;
            RebuildContext();
            ClearTarget();
        }

        private void RebuildContext()
        {
            Action<string> showMessage = null;
            if (_hud != null)
            {
                showMessage = _hud.ShowMessage;
            }

            _context = new InteractionContext(_inventory, showMessage);
        }

        public void ScanForInteractable()
        {
            if (_camera == null || IsInventoryModalActive)
            {
                ClearTarget();
                return;
            }

            Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, interactionDistance, raycastLayers, QueryTriggerInteraction.Ignore))
            {
                ClearTarget();
                return;
            }

            MonoBehaviour[] components = hit.collider.GetComponentsInParent<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour component = components[i];
                if (component == null || !component.isActiveAndEnabled || !(component is IInteractable interactable))
                {
                    continue;
                }

                if (!interactable.CanInteract(_context))
                {
                    continue;
                }

                SetTarget(component, interactable);
                return;
            }

            ClearTarget();
        }

        public bool TryInteractCurrent()
        {
            if (IsInventoryModalActive)
            {
                ClearTarget();
                return false;
            }

            if (_context == null)
            {
                RebuildContext();
            }

            ScanForInteractable();
            IInteractable target = CurrentTarget;
            MonoBehaviour targetComponent = _targetComponent;
            if (target == null || targetComponent == null || _context == null || !target.CanInteract(_context))
            {
                ClearTarget();
                return false;
            }

            target.Interact(_context);

            if (targetComponent == null || !target.CanInteract(_context))
            {
                ClearTarget();
            }

            return true;
        }

        private bool IsInventoryModalActive =>
            (_inventoryUI != null && _inventoryUI.IsOpen) || (_input != null && _input.InventoryPressed);

        private void SetTarget(MonoBehaviour component, IInteractable interactable)
        {
            _targetComponent = component;
            _target = interactable;

            if (_hud == null)
            {
                return;
            }

            string action = _input != null && _input.UsingGamepad ? "A" : "E";
            string prompt = $"[{action}] {interactable.Prompt}";
            if (_lastPrompt != prompt)
            {
                _lastPrompt = prompt;
                _hud.SetPrompt(prompt);
            }
        }

        private void ClearTarget()
        {
            _targetComponent = null;
            _target = null;

            if (_lastPrompt != null)
            {
                _lastPrompt = null;
                if (_hud != null)
                {
                    _hud.SetPrompt(string.Empty);
                }
            }
        }
    }
}
