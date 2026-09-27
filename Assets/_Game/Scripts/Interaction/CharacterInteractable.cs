using UnityEngine;
using Voron.Characters;

namespace Voron.Interaction
{
    public sealed class CharacterInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private CharacterData character;
        [SerializeField] private CharacterAnimation characterAnimation;

        public string Prompt => character != null ? $"TALK TO {character.DisplayName}" : "TALK";

        private void Awake()
        {
            if (characterAnimation == null)
            {
                characterAnimation = GetComponentInParent<CharacterAnimation>();
            }
        }

        public void Configure(CharacterData characterData, CharacterAnimation characterAnimation)
        {
            character = characterData;
            this.characterAnimation = characterAnimation;
        }

        public bool CanInteract(InteractionContext context)
        {
            return character != null;
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context))
            {
                return;
            }

            characterAnimation?.PlayTalk();
            string greeting = string.IsNullOrWhiteSpace(character.Greeting)
                ? $"{character.DisplayName} has nothing to say."
                : character.Greeting;
            context?.ShowMessage?.Invoke(greeting);
        }
    }
}
