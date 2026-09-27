using UnityEngine;

namespace Voron.Characters
{
    [DisallowMultipleComponent]
    public sealed class CharacterWalkPreview : MonoBehaviour
    {
        [SerializeField] private CharacterAnimation animationDriver;
        [SerializeField, Range(0f, 2f)] private float speed = 1f;

        public void Configure(CharacterAnimation animationDriver, float speed = 1f)
        {
            this.animationDriver = animationDriver;
            this.speed = Mathf.Clamp(speed, 0f, 2f);
        }

        private void Start()
        {
            if (animationDriver != null)
                animationDriver.SetSpeed(speed);
        }

        private void OnDisable()
        {
            if (animationDriver != null)
                animationDriver.SetSpeed(0f);
        }
    }
}
