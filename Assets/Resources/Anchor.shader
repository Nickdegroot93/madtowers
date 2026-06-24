Shader "MadTowers/Anchor"
{
    // Anchor STONE/IRON cell, drawn with the same rounded-brick recipe as the game's normal bricks
    // (Tools/generate_piece_sprites.py / Lava.shader) so it sits right next to them, but as a fixed,
    // theme-independent slab of riveted gunmetal: an in-hue bevel, a faintly recessed inner panel,
    // brushed-metal streaks, four corner rivets, and a slow specular sheen sweeping across the whole
    // piece (world-space, so it reads as one plate not four cells). _LockFlash is pulsed by
    // AnchorBlockSkin when the brick locks/freezes - the rivets and rim glint, marrying the juice to
    // the anchor's actual moment. Theme-independent: the chapter art is hidden, only the quad alpha is used.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _SteelColor ("Steel Colour", Color) = (0.17, 0.21, 0.27, 1)
        _GlintColor ("Glint / Sheen Colour", Color) = (0.75, 0.86, 1.0, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.12
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.1
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.16
        _PanelInset ("Inner Panel Inset", Range(0, 0.3)) = 0.14
        _RivetInset ("Rivet Inset (from centre)", Range(0, 0.45)) = 0.18
        _RivetRadius ("Rivet Radius", Range(0, 0.2)) = 0.075
        _SheenSpeed ("Sheen Speed", Float) = 0.5
        _SheenStrength ("Sheen Strength", Range(0, 1)) = 0.5
        _LockFlash ("Lock Flash (0..1, driven)", Range(0, 1)) = 0
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
                float4 _SteelColor;
                float4 _GlintColor;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float _PanelInset;
                float _RivetInset;
                float _RivetRadius;
                float _SheenSpeed;
                float _SheenStrength;
                float _LockFlash;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 worldXY     : TEXCOORD1;
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

            // One rivet at centre c: returns (fill mask, shade) where shade is a top-left dome highlight
            // minus a dark contact ring just outside the stud.
            float2 rivet(float2 p, float2 c, float r, float aa)
            {
                float2 d = p - c;
                float sd = length(d) - r;
                float m = 1.0 - smoothstep(0.0, aa, sd);
                float2 nd = d / max(length(d), 1e-4);
                float dome = saturate(dot(nd, normalize(float2(-1.0, 1.0))));
                float shade = (dome - 0.45) * m;
                float ring = smoothstep(0.0, aa, sd) * (1.0 - smoothstep(aa, r * 0.6, sd));
                return float2(m, shade - ring * 0.5);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.worldXY = TransformObjectToWorld(IN.positionOS.xyz).xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float3 steel = _SteelColor.rgb;

                float2 p = uv - 0.5;
                float halfBox = 0.5;
                float r = min(_CornerRadius, halfBox - 0.001);
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(p, bb, r);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                // Vertical gradient, lighter at the top (matches the normal brick recipe).
                float grad = 0.85 + 0.30 * uv.y;
                float3 body = steel * grad;

                // In-hue bevel: lighten the top-facing inner rim, darken the bottom-facing one.
                float e = 0.012;
                float dY = sdRoundBox(p + float2(0, e), bb, r) - sdRoundBox(p - float2(0, e), bb, r);
                float ny = dY / (2.0 * e);
                float band = saturate((d + _OutlineWidth + _BevelWidth) / max(_BevelWidth, 0.001));
                float f = 1.0;
                if (ny > 0.4) f = 1.0 + 0.28 * band;        // top rim, lighter (same hue)
                else if (ny < -0.4) f = 1.0 - 0.22 * band;  // bottom rim, darker (same hue)
                body *= f;

                // Faintly recessed inner panel: darken the centre a touch and draw a thin inner edge line.
                float innerHalf = max(0.05, halfBox - _PanelInset);
                float di = sdRoundBox(p, float2(innerHalf, innerHalf), r * 0.7);
                float center = 1.0 - smoothstep(0.0, aa, di);
                body *= (1.0 - 0.10 * center);
                float innerEdge = (1.0 - smoothstep(0.0, 0.02, abs(di))) * 0.18;
                body *= (1.0 - innerEdge);

                // Brushed-metal: fine horizontal streaks (vary per row).
                float streak = (hash21(float2(floor(uv.y * 150.0), 3.0)) - 0.5) * 0.06;
                body *= (1.0 + streak);

                // Outline: blend toward a darker steel near the edge (no hard line).
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                body = lerp(body, steel * 0.30 * grad, tOut);

                // Four corner rivets.
                float q = 0.5 - _RivetInset;
                float2 a0 = rivet(p, float2(-q, -q), _RivetRadius, aa);
                float2 a1 = rivet(p, float2( q, -q), _RivetRadius, aa);
                float2 a2 = rivet(p, float2(-q,  q), _RivetRadius, aa);
                float2 a3 = rivet(p, float2( q,  q), _RivetRadius, aa);
                float studMask = max(max(a0.x, a1.x), max(a2.x, a3.x));
                float studShade = a0.y + a1.y + a2.y + a3.y;
                body = lerp(body, steel * 1.30 * grad, studMask * 0.5); // studs a touch brighter steel
                body *= (1.0 + studShade * 0.9);

                // Slow specular sheen sweeping across the whole piece (world-space diagonal band).
                float axis = dot(IN.worldXY, normalize(float2(0.5, 1.0)));
                float sweep = frac(axis * 0.5 - _Time.y * _SheenSpeed * 0.1);
                float sheen = smoothstep(0.46, 0.5, sweep) * (1.0 - smoothstep(0.5, 0.54, sweep));
                body += _GlintColor.rgb * sheen * _SheenStrength * mask;

                // Lock flash: rivets and rim glint when the brick freezes.
                float rim = saturate(1.0 + d / max(_OutlineWidth + _BevelWidth, 0.001));
                body += _GlintColor.rgb * _LockFlash * (studMask * 1.4 + rim * 0.6);

                return half4(body, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
