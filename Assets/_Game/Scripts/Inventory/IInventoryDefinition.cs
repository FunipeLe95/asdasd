namespace Voron.Inventory
{
    public interface IInventoryDefinition
    {
        string Id { get; }
        int StackLimit { get; }
    }
}
