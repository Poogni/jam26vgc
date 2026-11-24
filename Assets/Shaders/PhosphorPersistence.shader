// CRT phosphor persistence: blends the new frame with the previous (already persisted) frame so fast
// motion, like a 90° turn, leaves a short ghost trail. A still image is unaffected.
// Used by RetroViewport via Graphics.Blit at the low source resolution.
Shader "Hidden/Retro/PhosphorPersistence"
{
    Properties
    {
        _MainTex ("Current Frame", 2D) = "black" {}
        _History ("Previous Frame", 2D) = "black" {}
        _Persistence ("Persistence", Range(0, 0.98)) = 0.55
        _BrightBias ("Bright Trail Bias", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _History;
            float _Persistence; // already frame-rate corrected by the script
            float _BrightBias;

            float4 frag(v2f_img i) : SV_Target
            {
                float3 cur = tex2D(_MainTex, i.uv).rgb;
                float3 prev = tex2D(_History, i.uv).rgb;

                // Plain ghosting: everything smears a little while it moves.
                float3 ghost = lerp(cur, prev, _Persistence);
                // Phosphor-like: only things brighter than the new frame keep glowing, then decay.
                float3 glow = max(cur, prev * _Persistence);

                return float4(lerp(ghost, glow, _BrightBias), 1.0);
            }
            ENDCG
        }
    }
}
