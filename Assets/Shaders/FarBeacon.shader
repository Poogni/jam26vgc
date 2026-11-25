// Soft vertical light beam for beacons that must be spotted from far away.
// Unlit, no fog (it should cut through the haze), depth-tested so nearby walls still hide its base, no depth write.
// Meant for a quad (UV x across the beam, UV y up it) that FarBeacon turns to face the camera.
Shader "Retro/Far Beacon"
{
    Properties
    {
        _Color ("Beam Color", Color) = (0.35, 0.85, 1, 0.85)
        _CoreColor ("Core Color", Color) = (1, 1, 1, 1)
        _CoreWidth ("Core Width", Range(0.01, 1)) = 0.25
        _FadeTop ("Fade Top (share of height)", Range(0.01, 1)) = 0.45
        _FadeBottom ("Fade Bottom (share of height)", Range(0, 0.5)) = 0.03
        _Pulse ("Pulse", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _CoreColor;
                half _CoreWidth;
                half _FadeTop;
                half _FadeBottom;
                half _Pulse;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half across = abs(i.uv.x * 2.0 - 1.0);           // 0 in the middle, 1 at the edges
                half glow = saturate(1.0 - across);
                glow *= glow;                                     // soft falloff to the sides
                half core = saturate(1.0 - across / _CoreWidth);

                half top = saturate((1.0 - i.uv.y) / _FadeTop);   // dissolves into the sky
                half bottom = _FadeBottom > 0 ? saturate(i.uv.y / _FadeBottom) : 1.0;
                half pulse = 1.0 - _Pulse * (0.5 + 0.5 * sin(_Time.y * 2.5));

                half3 col = lerp(_Color.rgb, _CoreColor.rgb, core * core);
                half a = saturate(_Color.a * glow + core * _CoreColor.a * 0.6) * top * bottom * pulse;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
