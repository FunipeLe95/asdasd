namespace Voron.Inventory
{
    public sealed class InventoryItem
    {
        public ItemData Data { get; }
        public int Quantity { get; }

        internal InventoryItem(ItemData data, int quantity)
        {
            Data = data;
            Quantity = quantity;
        }
    }
}
