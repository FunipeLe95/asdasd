using System;
using Voron.Inventory;

namespace Voron.Interaction
{
    public sealed class InteractionContext
    {
        public InventorySystem Inventory { get; }
        public Action<string> ShowMessage { get; }

        public InteractionContext(InventorySystem inventory, Action<string> showMessage)
        {
            Inventory = inventory;
            ShowMessage = showMessage;
        }
    }
}
