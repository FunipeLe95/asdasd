Shader "Hidden/Voron/PS1CameraEffect"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _LowResolution ("Low Resolution", Vector) = (320, 180, 0.003125, 0.005556)
        _ColorSteps ("Color Steps", Float) = 32
        _DitherStrength ("Dither Strength", Range(0, 1)) = 0.28
        _NoiseStrength ("Noise Strength", Range(0, 0.05)) = 0.008
        _Frame ("Frame", Float) = 0
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _LowResolution;
            float _ColorSteps;
            float _DitherStrength;
            float _NoiseStrength;
            float _Frame;

            float Bayer4x4(float2 pixel)
            {
                float2 p = fmod(floor(pixel), 4.0);
                float value = 0.0;

                if (p.y < 1.0)
                {
                    value = p.x < 1.0 ? 0.0 : p.x < 2.0 ? 8.0 : p.x < 3.0 ? 2.0 : 10.0;
                }
                else if (p.y < 2.0)
                {
                    value = p.x < 1.0 ? 12.0 : p.x < 2.0 ? 4.0 : p.x < 3.0 ? 14.0 : 6.0;
                }
                else if (p.y < 3.0)
                {
                    value = p.x < 1.0 ? 3.0 : p.x < 2.0 ? 11.0 : p.x < 3.0 ? 1.0 : 9.0;
                }
                else
                {
                    value = p.x < 1.0 ? 15.0 : p.x < 2.0 ? 7.0 : p.x < 3.0 ? 13.0 : 5.0;
                }

                return value / 16.0;
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv);
                float2 pixel = floor(input.uv * _LowResolution.xy);
                float threshold = (Bayer4x4(pixel) - 0.5) * _DitherStrength;
                float levels = max(_ColorSteps, 2.0);
                float3 quantized = floor(saturate(color.rgb) * levels + 0.5 + threshold) / levels;

                float noise = frac(sin(dot(pixel + _Frame, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
                color.rgb = saturate(quantized + noise * _NoiseStrength);
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
