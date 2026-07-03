Shader "MadTowers/Lava"
{
    // Magma cell: a charcoal CRUST riven by a molten crack network that glows from within.
    // Every cell is the same cooling-lava material (no more black/red checkerboard); the per-cell
    // _StoneColor from MagmaBlockSkin only nudges how HOT the cell runs (red cells = wider, brighter
    // veins), and _Seed desyncs the vein layout between cells. The veins breathe with _PulseSpeed and
    // their cores are bloom-emissive (white-yellow core -> deep orange halo) so the brick reads as
    // barely-contained molten rock about to melt - which is exactly what it does on landing.
    // Framed by the shared brick recipe (gradient, embossed bevel, near-black outline, grain) so it
    // tiles next to the normal bricks. Theme-independent (the chapter art is hidden).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _StoneColor ("Heat Tint (per cell, driven)", Color) = (1, 1, 1, 1)
        _Seed ("Per-cell Seed", Float) = 0
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.086
        _Inset ("Cell Inset", Range(0, 0.1)) = 0.0
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.066
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.102
        _CrustColor ("Crust Colour", Color) = (0.115, 0.095, 0.085, 1)
        _CoreColor ("Molten Core Colour", Color) = (1.0, 0.86, 0.45, 1)
        _GlowColor ("Molten Halo Colour", Color) = (1.0, 0.38, 0.10, 1)
        _Emission ("Molten Glow Strength", Range(0, 3)) = 1.25
        _PulseSpeed ("Glow Pulse Speed", Float) = 1.5
        _VeinScale ("Vein Scale", Float) = 3.4
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _StoneColor;
                float _Seed;
                float _CornerRadius;
                float _Inset;
                float _OutlineWidth;
                float _BevelWidth;
                float4 _CrustColor;
                float4 _CoreColor;
                float4 _GlowColor;
                float _Emission;
                float _PulseSpeed;
                float _VeinScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0, amp = 0.5;
                for (int i = 0; i < 4; i++) { v += amp * vnoise(p); p *= 2.02; amp *= 0.5; }
                return v;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, float2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                float2 p = uv - 0.5;
                float halfBox = max(0.05, 0.5 - _Inset);
                float r = min(_CornerRadius, halfBox - 0.001);
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(p, bb, r);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                // How hot this cell runs: the skin's red cells push more molten glow.
                float heat = 0.8 + 0.5 * saturate((_StoneColor.r - max(_StoneColor.g, _StoneColor.b)) * 3.0);

                // Crust body: charcoal with slow fbm mottling (cooling plates).
                float grad = 1.13 - 0.36 * pow(saturate(1.0 - uv.y), 1.15);
                float2 suv = uv + _Seed * 13.7;
                float plates = fbm(suv * 4.2 + 2.7);
                float3 tint = _CrustColor.rgb;
                float3 body = tint * grad * (0.80 + 0.45 * plates);

                // Molten vein network: domain-warped noise band. Wide soft halo heats the crust around
                // each vein; the narrow core is white-hot and bloom-emissive.
                float warp = fbm(suv * _VeinScale * 0.5 + 7.0);
                float cf = vnoise(suv * _VeinScale + warp * 1.7);
                float vd = abs(cf - 0.5);
                float halo = 1.0 - smoothstep(0.0, 0.16, vd);
                float vein = 1.0 - smoothstep(0.0, 0.055, vd);
                float core = 1.0 - smoothstep(0.0, 0.022, vd);

                // The veins breathe - hotter cells pulse deeper.
                float pulse = 0.80 + 0.20 * sin(_Time.y * _PulseSpeed + _Seed * 6.2831);

                // Embossed bevel + AO ring (shared frame). The crust is dark, so the top rim stays in-hue.
                float e = 0.012;
                float dY = sdRoundBox(p + float2(0, e), bb, r) - sdRoundBox(p - float2(0, e), bb, r);
                float dX = sdRoundBox(p + float2(e, 0), bb, r) - sdRoundBox(p - float2(e, 0), bb, r);
                float ny = dY / (2.0 * e);
                float nx = dX / (2.0 * e);
                float band = pow(saturate((d + _OutlineWidth + _BevelWidth) / max(_BevelWidth, 0.001)), 1.6);
                band *= saturate((-d - _OutlineWidth * 0.55) / max(_OutlineWidth * 0.45, 0.001));
                float topness  = saturate((ny - 0.25) / 0.5);
                float botness  = saturate((-ny - 0.25) / 0.5);
                float sideness = saturate((abs(nx) - 0.25) / 0.5) * (1.0 - topness) * (1.0 - botness);
                body *= (1.0 - 0.09 * band);
                float3 hiCol = lerp(tint * 1.6, 1.0 - (1.0 - tint) * 0.42, 0.35) * grad;
                body = lerp(body, hiCol, 0.55 * band * topness);
                body *= (1.0 - 0.26 * band * botness);
                body *= (1.0 - 0.12 * band * sideness);

                // Grain keeps the crust alive.
                float g = (hash21(floor(uv * 48.0)) - 0.5) * 0.05;
                body *= (1.0 + g);

                // Lay the molten light INTO the crust (after shading, so it glows from inside cracks).
                float glowAmt = _Emission * heat * pulse;
                body += _GlowColor.rgb * halo * 0.35 * glowAmt;                 // warm bleed around veins
                body = lerp(body, _GlowColor.rgb * (0.8 + 0.6 * glowAmt), vein); // the vein itself
                body = lerp(body, _CoreColor.rgb * (1.0 + 0.8 * glowAmt), core); // white-hot core (blooms)

                // A few tiny ember specks drifting in the crust.
                float spk = step(0.985, hash21(floor(suv * 26.0)));
                body += _GlowColor.rgb * spk * glowAmt * 0.6;

                // Outline: thick, near-black; molten veins faintly show through where they cross it.
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                float3 outCol = tint * 0.30;
                outCol += _GlowColor.rgb * vein * 0.25 * glowAmt;
                body = lerp(body, outCol * grad, tOut);

                return half4(body, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
