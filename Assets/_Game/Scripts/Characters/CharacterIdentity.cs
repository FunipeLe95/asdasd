using UnityEngine;

namespace Voron.Characters
{
    [DisallowMultipleComponent]
    public sealed class CharacterIdentity : MonoBehaviour
    {
        [SerializeField] private CharacterData data;
        public CharacterData Data => data;
        public void Configure(CharacterData data) => this.data = data;
    }
}
