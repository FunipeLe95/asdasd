using UnityEngine;
using UnityEngine.Rendering;

namespace Voron.Core
{
    [CreateAssetMenu(fileName = "LightingProfile", menuName = "Voron/Lighting Profile")]
    public sealed class LightingProfile : ScriptableObject
    {
        [SerializeField] private AmbientMode ambientMode = AmbientMode.Flat;
        [SerializeField] private Color ambientColor = new Color(0.2f, 0.22f, 0.26f, 1f);
        [SerializeField, Min(0f)] private float ambientIntensity = 1f;
        [SerializeField] private bool fogEnabled;
        [SerializeField] private FogMode fogMode = FogMode.ExponentialSquared;
        [SerializeField] private Color fogColor = new Color(0.16f, 0.18f, 0.22f, 1f);
        [SerializeField, Min(0f)] private float fogDensity = 0.01f;
        [SerializeField] private Color keyLightColor = Color.white;
        [SerializeField, Min(0f)] private float keyLightIntensity = 1f;
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.7f;
        [SerializeField] private LightShadows shadows = LightShadows.Soft;
        [SerializeField, Min(0f)] private float reflectionIntensity = 0.1f;

        public AmbientMode AmbientMode => ambientMode;
        public Color AmbientColor => ambientColor;
        public float AmbientIntensity => ambientIntensity;
        public bool FogEnabled => fogEnabled;
        public FogMode FogMode => fogMode;
        public Color FogColor => fogColor;
        public float FogDensity => fogDensity;
        public Color KeyLightColor => keyLightColor;
        public float KeyLightIntensity => keyLightIntensity;
        public float ShadowStrength => shadowStrength;
        public LightShadows Shadows => shadows;
        public float ReflectionIntensity => reflectionIntensity;

        public void Configure(
            AmbientMode profileAmbientMode,
            Color profileAmbientColor,
            float profileAmbientIntensity,
            bool profileFogEnabled,
            FogMode profileFogMode,
            Color profileFogColor,
            float profileFogDensity,
            Color profileKeyLightColor,
            float profileKeyLightIntensity,
            float profileShadowStrength,
            LightShadows profileShadows,
            float profileReflectionIntensity)
        {
            ambientMode = profileAmbientMode;
            ambientColor = profileAmbientColor;
            ambientIntensity = Mathf.Max(0f, profileAmbientIntensity);
            fogEnabled = profileFogEnabled;
            fogMode = profileFogMode;
            fogColor = profileFogColor;
            fogDensity = Mathf.Max(0f, profileFogDensity);
            keyLightColor = profileKeyLightColor;
            keyLightIntensity = Mathf.Max(0f, profileKeyLightIntensity);
            shadowStrength = Mathf.Clamp01(profileShadowStrength);
            shadows = profileShadows;
            reflectionIntensity = Mathf.Max(0f, profileReflectionIntensity);
        }
    }
}
