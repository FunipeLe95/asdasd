namespace Voron.Interaction
{
    public interface IInteractable
    {
        string Prompt { get; }
        bool CanInteract(InteractionContext context);
        void Interact(InteractionContext context);
    }
}
