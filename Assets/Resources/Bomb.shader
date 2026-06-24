Shader "MadTowers/Bomb"
{
    // Bomb cell: a fixed, theme-independent powder-keg / sea-mine casing - near-black riveted iron
    // (same rounded-brick silhouette + bevel + corner bolts recipe as Anchor, so it tiles next to the
    // normal bricks) cut through by a network of SEAMS that glow from within. The seam glow is the
    // whole personality:
    //   - at rest (_Fuse = 0) the seams sit at a faint warm ember (_IdleEmber) so the brick reads as
    //     "explosive" while it is still falling and being steered;
    //   - once it locks the behaviour ramps _Fuse 0 -> 1, heating the seams ember-orange -> white-hot,
    //     and pulses _Pulse as an accelerating heartbeat.
    // Theme-independent: the chapter art is hidden, only the quad alpha is used (ART.md s13).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _IronColor ("Iron Colour", Color) = (0.11, 0.11, 0.13, 1)
        _SeamColor ("Seam / Glow Colour", Color) = (1.0, 0.45, 0.12, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.12
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.1
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.16
        _RivetInset ("Rivet Inset (from centre)", Range(0, 0.45)) = 0.18
        _RivetRadius ("Rivet Radius", Range(0, 0.2)) = 0.07
        _SeamScale ("Seam Scale", Float) = 5.0
        _SeamWidth ("Seam Width", Range(0.01, 0.15)) = 0.06
        _IdleEmber ("Idle Ember (glow at rest)", Range(0, 1)) = 0.28
        _Fuse ("Fuse Heat (0..1, driven)", Range(0, 1)) = 0
        _Pulse ("Heartbeat (0..1, driven)", Range(0, 1)) = 0
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
                float4 _IronColor;
                float4 _SeamColor;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float _RivetInset;
                float _RivetRadius;
                float _SeamScale;
                float _SeamWidth;
                float _IdleEmber;
                float _Fuse;
                float _Pulse;
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
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, float2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;
            }

            // One corner bolt at centre c: returns (fill mask, shade) where shade is a top-left dome
            // highlight minus a dark contact ring just outside the stud (same as Anchor's rivets).
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
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float3 iron = _IronColor.rgb;

                float2 p = uv - 0.5;
                float halfBox = 0.5;
                float r = min(_CornerRadius, halfBox - 0.001);
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(p, bb, r);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                // Vertical gradient, lighter at the top (matches the normal brick recipe).
                float grad = 0.82 + 0.30 * uv.y;
                float3 body = iron * grad;

                // In-hue bevel: lighten the top-facing inner rim, darken the bottom-facing one.
                float e = 0.012;
                float dY = sdRoundBox(p + float2(0, e), bb, r) - sdRoundBox(p - float2(0, e), bb, r);
                float ny = dY / (2.0 * e);
                float band = saturate((d + _OutlineWidth + _BevelWidth) / max(_BevelWidth, 0.001));
                float f = 1.0;
                if (ny > 0.4) f = 1.0 + 0.26 * band;        // top rim, lighter (same hue)
                else if (ny < -0.4) f = 1.0 - 0.22 * band;  // bottom rim, darker (same hue)
                body *= f;

                // Brushed-iron: fine horizontal streaks (vary per row), keeps the flat fill alive.
                float streak = (hash21(float2(floor(uv.y * 150.0), 3.0)) - 0.5) * 0.05;
                body *= (1.0 + streak);

                // Outline: blend toward a darker iron near the edge (no hard line).
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                body = lerp(body, iron * 0.28 * grad, tOut);

                // Seam network: a domain-warped noise band carves channels across the casing. The seam
                // floor is darker iron; the glow rides inside it. (Boulder's crack recipe.)
                float warp = vnoise(uv * _SeamScale * 0.5 + 7.0);
                float cf = vnoise(uv * _SeamScale + warp * 1.5);
                float seam = 1.0 - smoothstep(0.0, _SeamWidth, abs(cf - 0.5));
                body *= (1.0 - 0.55 * seam);

                // Four corner bolts.
                float q = 0.5 - _RivetInset;
                float2 a0 = rivet(p, float2(-q, -q), _RivetRadius, aa);
                float2 a1 = rivet(p, float2( q, -q), _RivetRadius, aa);
                float2 a2 = rivet(p, float2(-q,  q), _RivetRadius, aa);
                float2 a3 = rivet(p, float2( q,  q), _RivetRadius, aa);
                float studMask = max(max(a0.x, a1.x), max(a2.x, a3.x));
                float studShade = a0.y + a1.y + a2.y + a3.y;
                body = lerp(body, iron * 1.25 * grad, studMask * 0.5);
                body *= (1.0 + studShade * 0.9);

                // The glow: seams light from within. Ember-orange at rest, shifting white-hot as the fuse
                // burns, breathing with the heartbeat. The bolts stay cold iron (studMask keeps them out).
                float heatLevel = _IdleEmber + _Fuse * (1.9 + 1.6 * _Fuse);     // ramps super-linearly
                float breathe = 0.55 + 0.45 * _Pulse;                           // heartbeat depth
                float3 hot = lerp(_SeamColor.rgb, float3(1.0, 0.95, 0.85), _Fuse * 0.8); // -> white-hot
                float glow = seam * heatLevel * breathe * (1.0 - studMask);
                body += hot * glow;

                return half4(body, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
