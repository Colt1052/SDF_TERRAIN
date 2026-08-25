// TerrainVertexColor.shader
// URP 2D shader that renders geological layer colors by depth, with noise-based
// boundary perturbation so layer transitions look organic instead of perfectly circular.
Shader "SDFTerrain/VertexColor"
{
    Properties
    {
        _PlanetRadius("Planet Radius", Float) = 30.0
        _NoiseAmplitude("Noise Amplitude", Float) = 2.0
        _NoiseFrequency("Noise Frequency", Float) = 0.15
        _DirtColor("Dirt Color", Color) = (0.55, 0.35, 0.20, 1)
        _StoneColor("Stone Color", Color) = (0.55, 0.55, 0.55, 1)
        _DeepStoneColor("Deep Stone Color", Color) = (0.35, 0.35, 0.38, 1)
        _MantleColor("Mantle Color", Color) = (0.70, 0.30, 0.10, 1)
        [HDR]_TintColor("Tint Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 posOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float _PlanetRadius;
                float _NoiseAmplitude;
                float _NoiseFrequency;
                half4 _DirtColor;
                half4 _StoneColor;
                half4 _DeepStoneColor;
                half4 _MantleColor;
                half4 _TintColor;
            CBUFFER_END

            float hash(float2 p)
            {
                float h = dot(p, float2(12.9898, 78.233));
                return frac(sin(h) * 43758.5453123);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float v00 = hash(i);
                float v10 = hash(i + float2(1.0, 0.0));
                float v01 = hash(i + float2(0.0, 1.0));
                float v11 = hash(i + float2(1.0, 1.0));
                return lerp(lerp(v00, v10, u.x), lerp(v01, v11, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                float freq = 1.0;
                v += a * noise(p * freq);
                a *= 0.5; freq *= 2.0;
                v += a * noise(p * freq);
                a *= 0.5; freq *= 2.0;
                v += a * noise(p * freq);
                return v;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.posOS = input.positionOS.xy;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float r = length(input.posOS);
                float depth = max(0.0, _PlanetRadius - r);

                float n = fbm(input.posOS * _NoiseFrequency);
                float perturbedDepth = depth + _NoiseAmplitude * (n - 0.5);

                half4 layerColor;
                if (perturbedDepth < 3.0)
                    layerColor = _DirtColor;
                else if (perturbedDepth < 15.0)
                    layerColor = _StoneColor;
                else if (perturbedDepth < 30.0)
                    layerColor = _DeepStoneColor;
                else
                    layerColor = _MantleColor;

                return half4(layerColor.rgb * _TintColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
