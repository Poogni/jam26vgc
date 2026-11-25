// Seamless walls for the medina.
// - World-space triplanar projection: textures line up across every face, cell and building, so there are
//   no seams where generated faces meet, and nothing stretches.
// - Three wall textures blended by smooth world-space noise: lots of variation, but no hard edges.
// - Diffuse lighting from URP's light data: sun + shadows, point lights like the torch, ambient, fog.
Shader "Retro/Medina Wall"
{
    Properties
    {
        _TexA ("Wall Texture A", 2D) = "white" {}
        _TexB ("Wall Texture B", 2D) = "white" {}
        _TexC ("Wall Texture C", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1,1,1,1)

        _TextureSize ("Texture Size (meters per repeat)", Float) = 4
        _BlendScale ("Variation Scale (meters)", Float) = 3.5
        _BlendSharpness ("Variation Sharpness", Range(1, 8)) = 3
        _TriplanarSharpness ("Corner Blend Sharpness", Range(1, 16)) = 6
        _ToneVariation ("Brightness Variation", Range(0, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _TexA_ST;
            float4 _TexB_ST;
            float4 _TexC_ST;
            half4 _BaseColor;
            float _TextureSize;
            float _BlendScale;
            float _BlendSharpness;
            float _TriplanarSharpness;
            float _ToneVariation;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5 // Forward+ (clustered lights) needs SM 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_TexA); SAMPLER(sampler_TexA);
            TEXTURE2D(_TexB);
            TEXTURE2D(_TexC);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // ---- smooth 3D value noise (0..1)
            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ValueNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0,0,0)), Hash(i + float3(1,0,0)), f.x),
                                 lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                                 lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            // Triplanar sample of one texture; each texture gets its own offset so repeats never line up.
            half3 Triplanar(TEXTURE2D_PARAM(tex, samp), float3 p, float3 w, float2 offset)
            {
                half3 x = SAMPLE_TEXTURE2D(tex, samp, p.zy + offset).rgb;
                half3 y = SAMPLE_TEXTURE2D(tex, samp, p.xz + offset).rgb;
                half3 z = SAMPLE_TEXTURE2D(tex, samp, p.xy + offset).rgb;
                return x * w.x + y * w.y + z * w.z;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 pos = input.positionWS;

                // Projection weights (sharp so corners don't smear).
                float3 w = pow(abs(n), _TriplanarSharpness);
                w /= max(w.x + w.y + w.z, 1e-4);
                float3 p = pos / max(_TextureSize, 0.01);

                // Which wall texture where: two noise octaves, softly banded into A / B / C.
                float3 q = pos / max(_BlendScale, 0.01);
                float t = ValueNoise(q) * 0.7 + ValueNoise(q * 2.3 + 17.0) * 0.3;
                t = saturate((t - 0.5) * 1.6 + 0.5);
                float wa = saturate(1.0 - abs(t - 0.0) * _BlendSharpness);
                float wb = saturate(1.0 - abs(t - 0.5) * _BlendSharpness);
                float wc = saturate(1.0 - abs(t - 1.0) * _BlendSharpness);
                float sum = max(wa + wb + wc, 1e-4);
                wa /= sum; wb /= sum; wc /= sum;

                // No branches around the samples: mip selection needs every pixel to sample.
                half3 albedo = wa * Triplanar(TEXTURE2D_ARGS(_TexA, sampler_TexA), p, w, float2(0.00, 0.00))
                             + wb * Triplanar(TEXTURE2D_ARGS(_TexB, sampler_TexA), p, w, float2(0.37, 0.61))
                             + wc * Triplanar(TEXTURE2D_ARGS(_TexC, sampler_TexA), p, w, float2(0.71, 0.23));

                // Gentle large-scale brightness drift (sun-bleached vs. damp patches).
                float tone = ValueNoise(q * 0.5 + 41.0) * 2.0 - 1.0;
                albedo *= (1.0 + tone * _ToneVariation) * _BaseColor.rgb;

                // Lighting: written out by hand (URP's UniversalFragmentBlinnPhong discards these pixels in this
                // project's Forward+ setup). Diffuse only: plaster has no shine.
                InputData inputData = (InputData)0;          // the light-loop macros read inputData
                inputData.positionWS = pos;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = n;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half4 shadowMask = half4(1, 1, 1, 1);

                // Screen-space ambient occlusion (URP's SSAO feature): full on ambient, a little on direct light.
                half indirectAO = 1.0, directAO = 1.0;
            #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
                indirectAO = ao.indirectAmbientOcclusion;
                directAO = ao.directAmbientOcclusion;
            #endif

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(pos), pos, shadowMask);
                half3 lighting = SampleSH(n) * indirectAO; // ambient
                lighting += mainLight.color * saturate(dot(n, mainLight.direction))
                          * mainLight.shadowAttenuation * mainLight.distanceAttenuation * directAO;

            #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, pos, shadowMask);
                    lighting += l.color * saturate(dot(n, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                LIGHT_LOOP_END
            #endif

                half3 color = MixFog(albedo * lighting, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowVert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                o.positionCS = ApplyShadowClamping(positionCS);
                return o;
            }

            half4 ShadowFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            // Normals for SSAO (and anything else that needs the depth-normals texture). Same encoding as URP Lit.
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings DNVert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 DNFrag(Varyings input) : SV_Target
            {
            #if defined(_GBUFFER_NORMALS_OCT)
                float3 normalWS = normalize(input.normalWS);
                float2 oct = PackNormalOctQuadEncode(normalWS);
                half3 packed = PackFloat2To888(saturate(oct * 0.5 + 0.5));
                return half4(packed, 0.0);
            #else
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half DepthFrag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Simple Lit"
}
