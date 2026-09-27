using System;
using UnityEngine;

namespace Voron.Characters
{
    [CreateAssetMenu(menuName = "Voron/Character", fileName = "Character")]
    public sealed class CharacterData : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private string role;
        [SerializeField, TextArea(2, 5)] private string greeting;
        [SerializeField] private Texture2D faceTexture;
        [SerializeField] private Texture2D talkFaceTexture;
        [SerializeField] private Texture2D blinkFaceTexture;
        [SerializeField] private Color bodyTint = Color.white;

        public string Id => id;
        public string DisplayName => displayName;
        public string Role => role;
        public string Greeting => greeting;
        public Texture2D FaceTexture => faceTexture;
        public Texture2D TalkFaceTexture => talkFaceTexture;
        public Texture2D BlinkFaceTexture => blinkFaceTexture;
        public Color BodyTint => bodyTint;

        public void Configure(string id, string displayName, string role, string greeting,
            Texture2D faceTexture, Color bodyTint, Texture2D talkFaceTexture = null, Texture2D blinkFaceTexture = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A character requires a stable non-empty ID.", nameof(id));
            this.id = id.Trim();
            this.displayName = displayName;
            this.role = role;
            this.greeting = greeting;
            this.faceTexture = faceTexture;
            this.talkFaceTexture = talkFaceTexture;
            this.blinkFaceTexture = blinkFaceTexture;
            this.bodyTint = bodyTint;
        }
    }
}
