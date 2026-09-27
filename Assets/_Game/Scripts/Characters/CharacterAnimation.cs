using UnityEngine;

namespace Voron.Characters
{
    [DisallowMultipleComponent]
    public sealed class CharacterAnimation : MonoBehaviour
    {
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int Inspect = Animator.StringToHash("Inspect");
        private static readonly int Talk = Animator.StringToHash("Talk");
        private static readonly int Turn = Animator.StringToHash("Turn");
        private static readonly int Death = Animator.StringToHash("Death");
        private static readonly int TalkState = Animator.StringToHash("Talk");
        private static readonly int IdleState = Animator.StringToHash("Idle");
        [SerializeField] private Animator animator;
        [SerializeField] private bool randomizeIdlePhase = true;
        private bool dead;

        public bool IsDead => dead;

        public void Configure(Animator animator) => this.animator = animator;

        private void Start()
        {
            // Every exhibit shares a 6 s Idle loop; a random phase keeps them from breathing in unison.
            if (randomizeIdlePhase && animator != null && animator.isActiveAndEnabled &&
                animator.runtimeAnimatorController != null)
                animator.Play(IdleState, 0, Random.value);
        }

        public void SetSpeed(float speed)
        {
            if (!dead && animator != null && animator.runtimeAnimatorController != null)
                animator.SetFloat(Speed, Mathf.Clamp(speed, 0f, 2f));
        }

        public void PlayInspect() => Trigger(Inspect);
        public void PlayTalk() => Trigger(Talk);
        public void PlayTurn() => Trigger(Turn);

        public void PlayDeath()
        {
            if (dead)
                return;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.ResetTrigger(Inspect);
                animator.ResetTrigger(Talk);
                animator.ResetTrigger(Turn);
                animator.SetFloat(Speed, 0f);
            }
            Trigger(Death);
            dead = true;
        }

        public bool TryGetTalkProgress(out float normalizedTime, out float length)
        {
            normalizedTime = 0f;
            length = 0f;
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null ||
                !animator.isInitialized)
                return false;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash != TalkState)
                return false;

            normalizedTime = state.normalizedTime;
            length = state.length;
            return true;
        }

        private void Trigger(int parameter)
        {
            if (!dead && animator != null && animator.runtimeAnimatorController != null)
                animator.SetTrigger(parameter);
        }
    }
}
