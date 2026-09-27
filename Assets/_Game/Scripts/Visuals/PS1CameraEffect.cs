using UnityEngine;

namespace Voron.Visuals
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class PS1CameraEffect : MonoBehaviour
    {
        [SerializeField] private Shader shader;
        [SerializeField, Range(80, 1280)] private int pixelWidth = 320;
        [SerializeField, Range(2, 256)] private int colorSteps = 32;
        [SerializeField, Range(0f, 1f)] private float ditherStrength = 0.28f;
        [SerializeField, Range(0f, 0.05f)] private float noiseStrength = 0.008f;
        [SerializeField] private bool effectEnabled = true;

        private Material _material;
        private int _frame;

        public bool EffectEnabled
        {
            get => effectEnabled;
            set => effectEnabled = value;
        }

        public void Configure(Shader effectShader, int width = 320, int levels = 32, float dither = 0.28f, float noise = 0.008f)
        {
            shader = effectShader;
            pixelWidth = Mathf.Clamp(width, 80, 1280);
            colorSteps = Mathf.Clamp(levels, 2, 256);
            ditherStrength = Mathf.Clamp01(dither);
            noiseStrength = Mathf.Clamp(noise, 0f, 0.05f);
            ReleaseMaterial();
        }

        private void OnEnable()
        {
            EnsureMaterial();
        }

        private void OnDisable()
        {
            ReleaseMaterial();
        }

        private void OnDestroy()
        {
            ReleaseMaterial();
        }

        private void OnValidate()
        {
            pixelWidth = Mathf.Clamp(pixelWidth, 80, 1280);
            colorSteps = Mathf.Clamp(colorSteps, 2, 256);
            ditherStrength = Mathf.Clamp01(ditherStrength);
            noiseStrength = Mathf.Clamp(noiseStrength, 0f, 0.05f);
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            if (!effectEnabled || !EnsureMaterial())
            {
                Graphics.Blit(source, destination);
                return;
            }

            int width = Mathf.Clamp(pixelWidth, 80, 1280);
            int height = Mathf.Max(1, Mathf.RoundToInt(width * source.height / (float)Mathf.Max(1, source.width)));
            RenderTexture lowResolution = null;

            try
            {
                lowResolution = RenderTexture.GetTemporary(width, height, 0, source.format, RenderTextureReadWrite.Default);
                lowResolution.filterMode = FilterMode.Point;
                lowResolution.wrapMode = TextureWrapMode.Clamp;

                _material.SetVector("_LowResolution", new Vector4(width, height, 1f / width, 1f / height));
                _material.SetFloat("_ColorSteps", colorSteps);
                _material.SetFloat("_DitherStrength", ditherStrength);
                _material.SetFloat("_NoiseStrength", noiseStrength);
                _material.SetFloat("_Frame", _frame++ % 4096);

                Graphics.Blit(source, lowResolution, _material);
                Graphics.Blit(lowResolution, destination);
            }
            finally
            {
                if (lowResolution != null)
                {
                    RenderTexture.ReleaseTemporary(lowResolution);
                }
            }
        }

        private bool EnsureMaterial()
        {
            if (_material != null)
            {
                return true;
            }

            if (shader == null)
            {
                shader = Shader.Find("Hidden/Voron/PS1CameraEffect");
            }

            if (shader == null || !shader.isSupported)
            {
                return false;
            }

            _material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            return true;
        }

        private void ReleaseMaterial()
        {
            if (_material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_material);
            }
            else
            {
                DestroyImmediate(_material);
            }

            _material = null;
        }
    }
}
