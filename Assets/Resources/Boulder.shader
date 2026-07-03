Shader "MadTowers/Boulder"
{
    // Boulder cell: a chunk of FACETED GRANITE that reads heavy at a glance. The surface is posterized
    // noise - big flat rock facets at distinct brightness steps - with dark crevice contours running
    // between the facets and a lit lower lip on each crevice (the same carved-emboss language as the
    // normal bricks' cracks). Mica flecks glint in the grain. Worn round corners, a heavier/darker base
    // gradient and NO idle motion: dead-still + matte = mass. The personality is the landing slam
    // (BoulderBlockSkin). Theme-independent (the chapter art is hidden).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _RockColor ("Rock Colour", Color) = (0.40, 0.38, 0.35, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.35)) = 0.16
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.066
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.102
        _FacetScale ("Facet Scale", Float) = 2.6
        _FacetSteps ("Facet Steps", Float) = 5
        _Speckle ("Speckle Strength", Range(0, 1)) = 0.55
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
                float _FacetScale;
                float _FacetSteps;
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

            // Facet field: (stepped brightness, crevice mask) at a given uv.
            float2 facets(float2 uv, float steps)
            {
                float n = fbm(uv * _FacetScale + 3.1);
                float f = frac(n * steps);
                float q = floor(n * steps) / steps;
                float crev = 1.0 - smoothstep(0.0, 0.16, min(f, 1.0 - f));
                return float2(q, crev);
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

                // Facet plates: flat steps of brightness with dark crevice contours between them.
                float steps = max(3.0, _FacetSteps);
                float2 fc = facets(uv, steps);
                float2 fcUp = facets(uv + float2(0.0, 0.028), steps);   // sample above: lit lip below crevices

                float3 rock = _RockColor.rgb * (0.72 + 0.62 * fc.x);

                // Heavier/darker base than normal bricks.
                float grad = 1.10 - 0.42 * pow(saturate(1.0 - uv.y), 1.15);
                rock *= grad;

                // Carved crevices: dark cut + light catching the plate edge below it.
                rock *= (1.0 - 0.42 * fc.y);
                rock *= (1.0 + 0.20 * fcUp.y * (1.0 - fc.y));

                // Mica flecks + occasional dark grain.
                float h = hash21(floor(uv * 70.0));
                rock *= (1.0 + step(0.93, h) * _Speckle * 0.7 - step(0.965, 1.0 - h) * 0.28);

                // Soft embossed bevel (worn edges) + AO ring.
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
                rock *= (1.0 - 0.09 * band);
                float3 hiRock = lerp(_RockColor.rgb * 1.5, 1.0 - (1.0 - _RockColor.rgb) * 0.42, 0.45) * grad;
                rock = lerp(rock, hiRock, 0.38 * band * topness);
                rock *= (1.0 - 0.22 * band * botness);
                rock *= (1.0 - 0.10 * band * sideness);

                // Outline: thick, near-black, granite hue kept.
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                float lumT = dot(_RockColor.rgb, float3(0.299, 0.587, 0.114));
                float3 outCol = lerp(_RockColor.rgb, float3(lumT, lumT, lumT), 0.30) * 0.22;
                rock = lerp(rock, outCol * grad, tOut);

                return half4(rock, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
