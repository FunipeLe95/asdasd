using UnityEngine;
using Voron.Inventory;

namespace Voron.Interaction
{
    public sealed class ItemPickup : MonoBehaviour, IInteractable
    {
        [SerializeField] private ItemData item;
        [SerializeField, Min(1)] private int quantity = 1;

        private bool _interacting;

        public string Prompt => item != null ? $"PICK UP {item.DisplayName}" : "PICK UP ITEM";

        public void Configure(ItemData itemData, int itemQuantity = 1)
        {
            item = itemData;
            quantity = Mathf.Max(1, itemQuantity);
            _interacting = false;
        }

        public bool CanInteract(InteractionContext context)
        {
            return !_interacting && item != null && context != null && context.Inventory != null;
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context))
            {
                return;
            }

            _interacting = true;
            if (!context.Inventory.TryAdd(item, quantity))
            {
                _interacting = false;
                context.ShowMessage?.Invoke("Inventory full.");
                return;
            }

            string itemName = string.IsNullOrWhiteSpace(item.DisplayName) ? "Item" : item.DisplayName;
            context.ShowMessage?.Invoke($"Added {itemName}.");
            Destroy(gameObject);
        }
    }
}
