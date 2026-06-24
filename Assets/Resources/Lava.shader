Shader "MadTowers/Lava"
{
    // Magma STONE cell, shaded with the SAME recipe as the game's normal bricks
    // (Tools/generate_piece_sprites.py) so it sits right next to them: a vertical gradient, an
    // IN-HUE bevel (the colour is multiplied lighter at the top rim / darker at the bottom - never a
    // white reflection), a darker-base outline, and faint grain. The per-cell tint (_StoneColor) is
    // black stone or red, set by MagmaBlockSkin (a 1x4 reads black-red-black-red). RED cells are a
    // bright, glowing fire colour with bloom-emissive heat (so they read as molten lava, not blood);
    // BLACK cells are clean dark stone with no glow. Theme-independent (the chapter art is hidden).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _StoneColor ("Stone Tint", Color) = (1, 1, 1, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.12
        _Inset ("Cell Inset", Range(0, 0.1)) = 0.0
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.1
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.14
        _Emission ("Fire Glow (hot cells)", Range(0, 2)) = 0.8
        _GlowColor ("Fire Colour", Color) = (1.0, 0.42, 0.12, 1)
        _PulseSpeed ("Glow Pulse Speed", Float) = 1.5
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
                float _CornerRadius;
                float _Inset;
                float _OutlineWidth;
                float _BevelWidth;
                float _Emission;
                float4 _GlowColor;
                float _PulseSpeed;
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
                float3 tint = _StoneColor.rgb;

                float2 p = uv - 0.5;
                float halfBox = max(0.05, 0.5 - _Inset);
                float r = min(_CornerRadius, halfBox - 0.001);
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(p, bb, r);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                // Vertical gradient, lighter at the top (matches generate_piece_sprites: 1.16 -> 0.82).
                float grad = 0.82 + 0.34 * uv.y;
                float3 body = tint * grad;

                // In-hue bevel: lighten the top-facing inner rim, darken the bottom-facing one. Use the
                // SDF's vertical gradient as the surface facing, only within a band near the edge.
                float e = 0.012;
                float dY = sdRoundBox(p + float2(0, e), bb, r) - sdRoundBox(p - float2(0, e), bb, r);
                float ny = dY / (2.0 * e);
                float band = saturate((d + _OutlineWidth + _BevelWidth) / max(_BevelWidth, 0.001));
                float f = 1.0;
                if (ny > 0.4) f = 1.0 + 0.20 * band;        // top rim, lighter (same hue)
                else if (ny < -0.4) f = 1.0 - 0.16 * band;  // bottom rim, darker (same hue)
                body *= f;

                // Outline: blend toward a darker version of the brick colour near the edge (no hard line).
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                body = lerp(body, tint * 0.32 * grad, tOut);

                // Faint grain so the flat fill isn't dead.
                float g = (hash21(floor(uv * 48.0)) - 0.5) * 0.05;
                body *= (1.0 + g);

                // Red cells glow like molten lava (bloom-emissive); black cells get nothing.
                float heat = saturate((tint.r - max(tint.g, tint.b)) * 3.0);
                float pulse = 0.85 + 0.15 * sin(_Time.y * _PulseSpeed);
                body += _GlowColor.rgb * heat * _Emission * pulse;

                return half4(body, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
