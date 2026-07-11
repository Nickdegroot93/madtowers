Shader "MadTowers/Sandstone"
{
    // Sandstone cell: warm layered sediment stone with a LOAD-BEARING read-out. _Damage (the
    // ratcheted worst load, 0..1) grows a crack network CONTINUOUSLY - veins appear, deepen
    // and widen as weight is added, never heal - and _Load (current pressure, 0..1) makes
    // fine sand trickle from the open cracks. Together with the skin's >85% shiver the brick
    // reads unambiguously: "one more thing on top and it bursts". Framed like the other
    // bricks (rounded cell, bevel, near-black outline, grain) so it tiles next to them.
    // Theme-independent by design (the chapter art is hidden).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Seed ("Per-cell Seed", Float) = 0
        _Damage ("Damage (ratcheted load)", Range(0, 1)) = 0
        _Load ("Current Load", Range(0, 1)) = 0
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.086
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.066
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.102
        _SandLight ("Sand Light", Color) = (0.87, 0.74, 0.5, 1)
        _SandDark ("Sand Dark", Color) = (0.7, 0.56, 0.36, 1)
        _CrackColor ("Crack Colour", Color) = (0.32, 0.22, 0.13, 1)
        _OutlineColor ("Outline Colour", Color) = (0.16, 0.11, 0.07, 1)
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

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Seed;
                float _Damage;
                float _Load;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float4 _SandLight;
                float4 _SandDark;
                float4 _CrackColor;
                float4 _OutlineColor;
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
                float4 color       : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345) + _Seed);
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i2 = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i2);
                float b = hash21(i2 + float2(1, 0));
                float c = hash21(i2 + float2(0, 1));
                float d = hash21(i2 + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Cracked-earth network: Voronoi plate edges. Returns x = distance to the nearest
            // plate border (thin where cracks live), y = the nearest plate's reveal order, so
            // damage cracks the brick plate by plate - unmistakably fracture, never camouflage.
            float2 crackNet(float2 uv)
            {
                float2 p = uv * 3.6 + _Seed;
                float2 ip = floor(p);
                float2 fp = frac(p);
                float f1 = 8.0;
                float f2 = 8.0;
                float id = 1.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = float2(hash21(ip + g), hash21(ip + g + 17.7));
                    float2 r = g + o - fp;
                    float d = dot(r, r);
                    if (d < f1) { f2 = f1; f1 = d; id = hash21(ip + g + 41.3); }
                    else if (d < f2) { f2 = d; }
                }
                return float2(sqrt(f2) - sqrt(f1), id);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv - 0.5;

                // Rounded-cell frame (the shared brick silhouette).
                float2 q = abs(p) - (0.5 - _CornerRadius);
                float cell = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - _CornerRadius + 0.5;
                float d = cell - 0.5; // <0 inside
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d); // house AA edge (Boulder pattern)
                if (d > aa) discard;

                // Body: horizontal sediment strata + grain speckle.
                float strata = vnoise(float2(i.uv.x * 2.0, i.uv.y * 7.0) + _Seed);
                float grain = hash21(floor(i.uv * 46.0));
                half3 col = lerp(_SandDark.rgb, _SandLight.rgb, 0.35 + 0.5 * strata + 0.15 * grain);

                // Bevel light: top-lit lip, darker base (house recipe). Both terms reference
                // their EDGE (uv.y = 1 / uv.y = 0) so the lit strips are _BevelWidth wide.
                col *= 1.0 + smoothstep(-_BevelWidth, 0.0, -(1.0 - i.uv.y)) * 0.12
                           - smoothstep(-_BevelWidth, 0.0, -(i.uv.y)) * 0.10;

                // THE CRACKS: plates fracture one by one as damage grows (reveal order per
                // plate), and every open crack WIDENS with damage - continuous, never healing.
                float2 net = crackNet(i.uv);
                float revealed = step(net.y, _Damage * 1.25);
                float width = 0.02 + 0.09 * _Damage;
                float open = (1.0 - smoothstep(width * 0.4, width, net.x)) * revealed;
                float shade = (1.0 - smoothstep(width, width * 2.5, net.x)) * revealed;
                col = lerp(col, _CrackColor.rgb * 1.3, shade * 0.3 * (0.4 + 0.6 * _Damage));
                col = lerp(col, _CrackColor.rgb, open * (0.65 + 0.35 * _Damage));

                // Fine sand TRICKLE inside the open cracks while under load: grain streaks
                // sliding down the crack columns.
                float trickleMask = open * _Load;
                float streak = step(0.86, hash21(floor(float2(i.uv.x * 30.0, 0.0))))
                             * frac(i.uv.y * 3.0 + _Time.y * 1.6 + _Seed);
                col = lerp(col, _SandDark.rgb * 0.8, trickleMask * streak * 0.5);

                // Edge chipping at high damage: corners and rim erode darker.
                float rim = smoothstep(-0.10, 0.0, d);
                col = lerp(col, _CrackColor.rgb * 0.9,
                           rim * _Damage * _Damage * (0.4 + 0.4 * hash21(floor(i.uv * 14.0))));

                // Near-black outline (house framing).
                float outline = smoothstep(-_OutlineWidth, -_OutlineWidth * 0.4, d);
                col = lerp(col, _OutlineColor.rgb, outline);

                return half4(col, i.color.a * mask);
            }
            ENDHLSL
        }
    }
}
