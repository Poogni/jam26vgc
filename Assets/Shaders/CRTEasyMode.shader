// CRT filter for the low-res 3D viewport (a UI RawImage showing the game render texture).
// Port of "crt-easymode" by EasyMode (GPL) from the libretro slang shaders, plus barrel curvature,
// rounded screen corners and an optional vignette.
//
// _MainTex is the low-res source; its texel size is the "SourceSize". The fragment's screen position
// drives the phosphor mask, so the mask is always aligned to real display pixels.
// RetroViewport sets the global _CRT_UVRect (the RawImage's uvRect crop).
Shader "Retro/CRT EasyMode"
{
    Properties
    {
        [PerRendererData] _MainTex ("Source", 2D) = "white" {}

        [Header(Curvature)]
        _Curvature ("Curvature (X, Y)", Vector) = (0.05, 0.07, 0, 0)
        _CornerRadius ("Corner Radius (px)", Range(0, 200)) = 30
        _EdgeSoftness ("Edge Softness (px)", Range(0.5, 20)) = 2
        _Vignette ("Vignette", Range(0, 1)) = 0.15

        [Header(Sharpness)]
        _SharpnessH ("Sharpness Horizontal", Range(0, 1)) = 0.5
        _SharpnessV ("Sharpness Vertical", Range(0, 1)) = 1.0

        [Header(Phosphor Mask)]
        _MaskStrength ("Mask Strength", Range(0, 1)) = 0.3
        _MaskDotWidth ("Mask Dot Width", Range(1, 100)) = 1
        _MaskDotHeight ("Mask Dot Height", Range(1, 100)) = 1
        _MaskStagger ("Mask Stagger", Range(0, 100)) = 0
        _MaskSize ("Mask Size", Range(1, 100)) = 1

        [Header(Scanlines)]
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 1.0
        _ScanlineBeamWidthMin ("Scanline Beam Width Min", Range(0.5, 5)) = 1.5
        _ScanlineBeamWidthMax ("Scanline Beam Width Max", Range(0.5, 5)) = 1.5
        _ScanlineBrightMin ("Scanline Brightness Min", Range(0, 1)) = 0.35
        _ScanlineBrightMax ("Scanline Brightness Max", Range(0, 1)) = 0.65
        _ScanlineCutoff ("Scanline Cutoff (source lines)", Range(1, 1000)) = 400
        _ScanlineMinPixels ("Scanline Anti-Moire (px per line for full strength)", Range(1, 8)) = 3

        [Header(Color)]
        _GammaInput ("Gamma Input", Range(0.1, 5)) = 2.0
        _GammaOutput ("Gamma Output", Range(0.1, 5)) = 1.8
        _BrightBoost ("Brightness Boost", Range(1, 2)) = 1.2
        [Toggle] _Dilation ("Dilation", Float) = 1

        // Standard UI stencil block, so the image still works inside masks.
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One Zero
        ColorMask [_ColorMask]

        Pass
        {
            Name "CRT"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            #define PI 3.141592653589

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // (1/w, 1/h, w, h)
            float4 _CRT_UVRect;        // set globally by RetroViewport: (x, y, w, h)

            float4 _Curvature;
            float _CornerRadius, _EdgeSoftness, _Vignette;
            float _SharpnessH, _SharpnessV;
            float _MaskStrength, _MaskDotWidth, _MaskDotHeight, _MaskStagger, _MaskSize;
            float _ScanlineStrength, _ScanlineBeamWidthMin, _ScanlineBeamWidthMax;
            float _ScanlineBrightMin, _ScanlineBrightMax, _ScanlineCutoff, _ScanlineMinPixels;
            float _GammaInput, _GammaOutput, _BrightBoost, _Dilation;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // The algorithm works on gamma-encoded values (like an emulator's framebuffer).
            // In a Linear project the texture sample is linear, so encode it first.
            float4 Sample(float2 uv)
            {
                float4 c = tex2Dlod(_MainTex, float4(uv, 0, 0)); // no mips; also safe inside branches
            #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = LinearToGammaSpace(c.rgb);
            #endif
                // dilate()
                float4 x = lerp(float4(1, 1, 1, 1), c, _Dilation);
                return c * x;
            }

            float CurveDistance(float x, float sharp)
            {
                // Half-circle s-curve for sharper (more pixelated) interpolation.
                float xStep = step(0.5, x);
                float curve = 0.5 - sqrt(0.25 - (x - xStep) * (x - xStep)) * sign(0.5 - x);
                return lerp(x, curve, sharp);
            }

            float3 FilterLanczos(float4 coeffs, float2 co, float2 dx)
            {
                float4 c0 = Sample(co - dx);
                float4 c1 = Sample(co);
                float4 c2 = Sample(co + dx);
                float4 c3 = Sample(co + 2.0 * dx);
                float4 col = c0 * coeffs.x + c1 * coeffs.y + c2 * coeffs.z + c3 * coeffs.w;
                return clamp(col, min(c1, c2), max(c1, c2)).rgb;
            }

            // Barrel distortion on a 0..1 screen coordinate.
            float2 Warp(float2 d)
            {
                float2 cc = d * 2.0 - 1.0;
                cc *= 1.0 + (cc.yx * cc.yx) * _Curvature.xy;
                return cc * 0.5 + 0.5;
            }

            // 1 inside the curved tube, fading to 0 over the rounded edge.
            float ScreenMask(float2 w, float2 outputSize)
            {
                float2 p = w * outputSize;
                float2 halfSize = outputSize * 0.5;
                float r = min(_CornerRadius, min(halfSize.x, halfSize.y));
                float2 q = abs(p - halfSize) - (halfSize - r);
                float dist = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
                return saturate(-dist / _EdgeSoftness);
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 uvRect = _CRT_UVRect.z > 0 ? _CRT_UVRect : float4(0, 0, 1, 1);

                // 0..1 across the visible screen, then curved.
                float2 d = (i.uv - uvRect.xy) / uvRect.zw;
                float2 w = Warp(d);
                float2 outputSize = 1.0 / max(fwidth(d), 1e-6);   // on-screen size of the viewport in pixels
                // Screen pixels per source scanline here (curvature makes it vary across the tube).
                float pxPerLine = 1.0 / max(fwidth(w.y) * uvRect.w * _MainTex_TexelSize.w, 1e-6);

                float frame = ScreenMask(w, outputSize);
                if (frame <= 0.0) return float4(0, 0, 0, 1);

                float2 srcUV = uvRect.xy + saturate(w) * uvRect.zw;
                float4 sourceSize = float4(_MainTex_TexelSize.zw, _MainTex_TexelSize.xy);

                float2 dx = float2(sourceSize.z, 0.0);
                float2 dy = float2(0.0, sourceSize.w);
                float2 pixCo = srcUV * sourceSize.xy - 0.5;
                float2 texCo = (floor(pixCo) + 0.5) * sourceSize.zw;
                float2 dist = frac(pixCo);

                // Horizontal Lanczos2 with sharpness curve.
                float curveX = CurveDistance(dist.x, _SharpnessH * _SharpnessH);
                float4 coeffs = PI * float4(1.0 + curveX, curveX, 1.0 - curveX, 2.0 - curveX);
                coeffs = max(abs(coeffs), 1e-5);
                coeffs = 2.0 * sin(coeffs) * sin(coeffs * 0.5) / (coeffs * coeffs);
                coeffs /= dot(coeffs, float4(1, 1, 1, 1));

                float3 col  = FilterLanczos(coeffs, texCo, dx);
                float3 col2 = FilterLanczos(coeffs, texCo + dy, dx);

                col = lerp(col, col2, CurveDistance(dist.y, _SharpnessV));
                col = pow(max(col, 0.0), _GammaInput / (_Dilation + 1.0));

                // Scanlines: beam gets wider / darker gaps shrink on bright pixels.
                float luma = dot(float3(0.2126, 0.7152, 0.0722), col);
                float bright = (max(col.r, max(col.g, col.b)) + luma) * 0.5;
                float scanBright = clamp(bright, _ScanlineBrightMin, _ScanlineBrightMax);
                float scanBeam = clamp(bright * _ScanlineBeamWidthMax, _ScanlineBeamWidthMin, _ScanlineBeamWidthMax);
                float scanWeight = 1.0 - pow(cos(srcUV.y * 2.0 * PI * sourceSize.y) * 0.5 + 0.5, scanBeam) * _ScanlineStrength;
                if (sourceSize.y >= _ScanlineCutoff) scanWeight = 1.0;
                // Anti-moire: with too few screen pixels per line, curved scanlines alias into ripples,
                // so fade them out (full at _ScanlineMinPixels, gone at half of that).
                float scanFade = saturate((pxPerLine - _ScanlineMinPixels * 0.5) / (_ScanlineMinPixels * 0.5));
                scanWeight = lerp(1.0, scanWeight, scanFade);

                // RGB phosphor mask, aligned to real screen pixels (not curved: it's the glass, not the image).
                float mask = 1.0 - _MaskStrength;
                float2 modFac = floor(i.pos.xy / float2(_MaskSize, _MaskDotHeight * _MaskSize));
                int dotNo = (int)fmod((modFac.x + fmod(modFac.y, 2.0) * _MaskStagger) / _MaskDotWidth, 3.0);
                float3 maskWeight = dotNo == 0 ? float3(1.0, mask, mask)
                                  : dotNo == 1 ? float3(mask, 1.0, mask)
                                               : float3(mask, mask, 1.0);

                col2 = col;
                col *= scanWeight;
                col = lerp(col, col2, scanBright);
                col *= maskWeight;
                col = pow(max(col, 0.0), 1.0 / _GammaOutput);
                col *= _BrightBoost;

                // Vignette + curved bezel.
                float2 vw = saturate(w);
                float vig = pow(saturate(16.0 * vw.x * vw.y * (1.0 - vw.x) * (1.0 - vw.y)), _Vignette * 0.5);
                col *= vig * frame;

            #ifndef UNITY_COLORSPACE_GAMMA
                col = GammaToLinearSpace(saturate(col));
            #endif
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
