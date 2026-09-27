using UnityEngine;

namespace Voron.Characters
{
    [DisallowMultipleComponent]
    public sealed class CharacterAppearance : MonoBehaviour
    {
        private const float MinBlinkInterval = 2.2f;
        private const float MaxBlinkInterval = 5.5f;
        private const float BlinkDuration = 0.11f;
        private const float DoubleBlinkChance = 0.2f;
        private const float DoubleBlinkGap = 0.14f;
        private static readonly int MainTexture = Shader.PropertyToID("_MainTex");
        [SerializeField] private CharacterData data;
        [SerializeField] private Renderer face;
        [SerializeField] private Renderer eyes;
        [SerializeField] private CharacterAnimation characterAnimation;
        private MaterialPropertyBlock properties;
        private Texture shownFace;
        private Texture shownEyes;
        private float blinkCountdown;
        private float blinkRemaining;
        private bool doubleBlinkNext;

        public bool IsMouthOpen { get; private set; }
        public bool IsBlinking => blinkRemaining > 0f;

        public void Configure(CharacterData data, Renderer face, Renderer eyes = null)
        {
            this.data = data;
            this.face = face;
            this.eyes = eyes;
            Apply();
        }

        private void OnEnable()
        {
            if (characterAnimation == null)
                characterAnimation = GetComponent<CharacterAnimation>();
            IsMouthOpen = false;
            blinkRemaining = 0f;
            doubleBlinkNext = false;
            blinkCountdown = Random.Range(MinBlinkInterval, MaxBlinkInterval);
            Apply();
        }

        private void Update()
        {
            AdvanceBlink(Time.deltaTime);
            IsMouthOpen = characterAnimation != null &&
                          characterAnimation.TryGetTalkProgress(out float normalizedTime, out float length) &&
                          FacePerformance.IsMouthOpen(normalizedTime, length);
            Refresh();
        }

        public void Blink()
        {
            blinkRemaining = BlinkDuration;
            doubleBlinkNext = false;
            Refresh();
        }

        public void Apply()
        {
            shownFace = null;
            shownEyes = null;
            Refresh();
        }

        private void AdvanceBlink(float deltaTime)
        {
            if (blinkRemaining > 0f)
            {
                blinkRemaining -= deltaTime;
                if (blinkRemaining > 0f)
                    return;

                blinkRemaining = 0f;
                if (doubleBlinkNext)
                {
                    doubleBlinkNext = false;
                    blinkCountdown = DoubleBlinkGap;
                }
                else
                {
                    blinkCountdown = Random.Range(MinBlinkInterval, MaxBlinkInterval);
                    doubleBlinkNext = Random.value < DoubleBlinkChance;
                }
                return;
            }

            if (characterAnimation != null && characterAnimation.IsDead)
                return;

            blinkCountdown -= deltaTime;
            if (blinkCountdown <= 0f)
                blinkRemaining = BlinkDuration;
        }

        private void Refresh()
        {
            if (data == null || data.FaceTexture == null)
                return;

            // Missing talk/blink maps fall back to the neutral face.
            Show(face, IsMouthOpen && data.TalkFaceTexture != null ? data.TalkFaceTexture : data.FaceTexture, ref shownFace);
            // The painted eyes are a strip of the same swappable face map.
            Show(eyes, IsBlinking && data.BlinkFaceTexture != null ? data.BlinkFaceTexture : data.FaceTexture, ref shownEyes);
        }

        private void Show(Renderer target, Texture texture, ref Texture shown)
        {
            if (target == null || texture == shown)
                return;
            if (properties == null)
                properties = new MaterialPropertyBlock();
            target.GetPropertyBlock(properties);
            properties.SetTexture(MainTexture, texture);
            target.SetPropertyBlock(properties);
            shown = texture;
        }
    }
}
