Shader "MadTowers/Tremor"
{
    // Tremor cell: a fixed, theme-independent slab of warm ochre fault-stone (same rounded-brick
    // silhouette + fbm lumps as Boulder, so it tiles next to the normal bricks) - but where Boulder is
    // dead-still grey basalt, Tremor is RESTLESS earth under stress: a network of fault cracks that glow
    // amber from within, with a pulse of light travelling along them (_Wave) so the brick reads as
    // charged seismic energy even at rest. On landing the behaviour drives _Quake 0->1: the cracks flash
    // and a shockwave ring rips outward across the face, marrying the look to the tower jolt it triggers.
    // Theme-independent: the chapter art is hidden, only the quad alpha is used (ART.md s13).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _RockColor ("Rock Colour", Color) = (0.50, 0.38, 0.21, 1)
        _GlowColor ("Fault Glow Colour", Color) = (1.0, 0.55, 0.16, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.35)) = 0.14
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.066
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.102
        _RockScale ("Rock Lump Scale", Float) = 4.0
        _CrackScale ("Fault Scale", Float) = 5.0
        _CrackWidth ("Fault Width", Range(0.01, 0.15)) = 0.06
        _IdleEmber ("Idle Ember (glow at rest)", Range(0, 1)) = 0.25
        _Wave ("Travelling Pulse (0..1, driven)", Range(0, 1)) = 0
        _Quake ("Quake Discharge (0..1, driven)", Range(0, 1)) = 0
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
                float4 _GlowColor;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float _RockScale;
                float _CrackScale;
                float _CrackWidth;
                float _IdleEmber;
                float _Wave;
                float _Quake;
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

                // Tectonic slabs: posterized plates of warm dry earth with hairline joints - the ground
                // is already broken into pieces that the quake will rattle.
                float n = fbm(uv * _RockScale + 1.7);
                float fq = frac(n * 4.0);
                float slabEdge = 1.0 - smoothstep(0.0, 0.20, min(fq, 1.0 - fq));
                float3 rock = _RockColor.rgb * (0.70 + 0.56 * (floor(n * 4.0) / 4.0));
                rock *= (1.0 - 0.30 * slabEdge);

                // Vertical gradient, lighter at the top; base heavier than normal bricks.
                float grad = 1.10 - 0.40 * pow(saturate(1.0 - uv.y), 1.15);
                rock *= grad;

                // Soft embossed bevel (worn earth edges), same structure as the piece generator:
                // AO ring, lit top rim, shadowed bottom, shaded sides.
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
                rock = lerp(rock, hiRock, 0.40 * band * topness);
                rock *= (1.0 - 0.22 * band * botness);
                rock *= (1.0 - 0.10 * band * sideness);

                // Outline: thick, near-black, hue kept (mildly desaturated darker earth).
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                float lumT = dot(_RockColor.rgb, float3(0.299, 0.587, 0.114));
                float3 outCol = lerp(_RockColor.rgb, float3(lumT, lumT, lumT), 0.30) * 0.22;
                rock = lerp(rock, outCol * grad, tOut);

                // Fault network: a domain-warped noise band. The channel floor is recessed (darker), and
                // the glow rides inside it.
                float warp = fbm(uv * _CrackScale * 0.5 + 4.0);
                float cf = vnoise(uv * _CrackScale + warp * 1.5);
                float fd = abs(cf - 0.5);
                float fault = 1.0 - smoothstep(0.0, _CrackWidth, fd);
                float halo = 1.0 - smoothstep(0.0, _CrackWidth * 2.6, fd);
                rock *= (1.0 - 0.62 * fault);

                // Travelling pulse: a bright band sweeps diagonally across the brick, lighting the part of
                // the fault network it passes over (energy looking for a way out).
                float axis = saturate((uv.x + uv.y) * 0.5);            // 0..1 corner-to-corner
                float waveBand = exp(-pow((axis - _Wave) * 6.0, 2.0)); // soft moving lobe
                float glow = _IdleEmber + waveBand * 0.85 + _Quake * 1.6; // quake flashes the whole network
                rock += _GlowColor.rgb * (fault * glow + halo * glow * 0.30);

                // Quake shockwave: a ring expands from the centre to the rim as _Quake goes 0->1, fading as
                // it grows. Reads as the seismic discharge on landing.
                if (_Quake > 0.0)
                {
                    float dist = length(p) * 2.0;                       // 0 centre .. ~1 edge
                    float ringR = _Quake;
                    float ring = 1.0 - smoothstep(0.0, 0.16, abs(dist - ringR));
                    rock += _GlowColor.rgb * ring * (1.0 - _Quake) * 1.4 * mask;
                }

                return half4(rock, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
