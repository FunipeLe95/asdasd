using UnityEngine;
using Voron.UI;

namespace Voron.Player
{
    public sealed class PlayerFlashlight : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Light _light;
        [SerializeField] private InventoryUI _inventoryUI;

        public void Configure(PlayerInputReader inputReader, Light flashlight, InventoryUI inventoryUI)
        {
            _input = inputReader;
            _light = flashlight;
            _inventoryUI = inventoryUI;
        }

        private void Update()
        {
            if (_input == null || _light == null || (_inventoryUI != null && _inventoryUI.IsOpen))
            {
                return;
            }

            if (_input.FlashlightPressed)
            {
                _light.enabled = !_light.enabled;
            }
        }
    }
}
