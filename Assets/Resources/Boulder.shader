Shader "MadTowers/Boulder"
{
    // Boulder cell: a fixed, theme-independent chunk of dark cracked basalt, drawn with the same
    // rounded-brick silhouette as the game's normal bricks (so it tiles next to them) but with a
    // ROUGH natural surface - the deliberate opposite of the Anchor's smooth manufactured iron:
    // fbm lumps/facets (hand-hewn, not a clean bevel), a meandering crack network, mineral speckle,
    // worn round corners and a heavier/darker base. No sheen, no idle motion - dead-still reads as
    // heavy; the personality is the landing slam (BoulderBlockSkin). Theme-independent: chapter art hidden.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _RockColor ("Rock Colour", Color) = (0.23, 0.215, 0.205, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.35)) = 0.16
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.1
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.12
        _RockScale ("Rock Lump Scale", Float) = 4.0
        _CrackScale ("Crack Scale", Float) = 6.0
        _CrackWidth ("Crack Width", Range(0.005, 0.12)) = 0.04
        _Speckle ("Speckle Strength", Range(0, 1)) = 0.5
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
                float4 _RockColor;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float _RockScale;
                float _CrackScale;
                float _CrackWidth;
                float _Speckle;
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

            float fbm(float2 p)
            {
                float s = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    s += a * vnoise(p);
                    p *= 2.0;
                    a *= 0.5;
                }
                return s;
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
                float halfBox = 0.5;
                float r = min(_CornerRadius, halfBox - 0.001);
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(p, bb, r);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                // Lumpy rock facets - uneven brightness, not a clean gradient.
                float n = fbm(uv * _RockScale + 3.1);
                float3 rock = _RockColor.rgb * (0.74 + 0.52 * n);

                // Vertical gradient, lighter at the top; the base sits heavier/darker.
                float grad = 0.70 + 0.38 * uv.y;
                rock *= grad;

                // Soft in-hue bevel (gentler than metal - rock edges are worn, not crisp).
                float e = 0.012;
                float dY = sdRoundBox(p + float2(0, e), bb, r) - sdRoundBox(p - float2(0, e), bb, r);
                float ny = dY / (2.0 * e);
                float band = saturate((d + _OutlineWidth + _BevelWidth) / max(_BevelWidth, 0.001));
                if (ny > 0.4) rock *= 1.0 + 0.16 * band;
                else if (ny < -0.4) rock *= 1.0 - 0.16 * band;

                // Crack network: a domain-warped noise band darkens where it crosses 0.5.
                float warp = fbm(uv * _CrackScale * 0.5 + 7.0);
                float cf = vnoise(uv * _CrackScale + warp * 1.5);
                float crack = 1.0 - smoothstep(0.0, _CrackWidth, abs(cf - 0.5));
                rock *= (1.0 - 0.75 * crack);

                // Mineral speckle: bright mica flecks, occasional dark grain.
                float h = hash21(floor(uv * 70.0));
                rock *= (1.0 + step(0.92, h) * _Speckle * 0.6 - step(0.96, 1.0 - h) * 0.25);

                // Outline: blend toward a darker rock near the edge (no hard line).
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                rock = lerp(rock, _RockColor.rgb * 0.24 * grad, tOut);

                return half4(rock, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
