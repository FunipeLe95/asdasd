using UnityEngine;

namespace Voron.Core
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PrototypeLighting : MonoBehaviour
    {
        [SerializeField] private LightingProfile profile;
        [SerializeField] private Light keyLight;

        public LightingProfile Profile => profile;

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        public void Configure(LightingProfile lightingProfile, Light directionalLight)
        {
            profile = lightingProfile;
            keyLight = directionalLight;
            Apply();
        }

        public void Apply(LightingProfile lightingProfile)
        {
            profile = lightingProfile;
            Apply();
        }

        public void Apply()
        {
            if (profile == null)
                return;

            RenderSettings.ambientMode = profile.AmbientMode;
            RenderSettings.ambientLight = profile.AmbientColor;
            RenderSettings.ambientIntensity = profile.AmbientIntensity;
            RenderSettings.fog = profile.FogEnabled;
            RenderSettings.fogMode = profile.FogMode;
            RenderSettings.fogColor = profile.FogColor;
            RenderSettings.fogDensity = profile.FogDensity;
            RenderSettings.reflectionIntensity = profile.ReflectionIntensity;

            if (keyLight == null)
                return;

            keyLight.color = profile.KeyLightColor;
            keyLight.intensity = profile.KeyLightIntensity;
            keyLight.shadowStrength = profile.ShadowStrength;
            keyLight.shadows = profile.Shadows;
        }
    }
}
