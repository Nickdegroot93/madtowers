Shader "MadTowers/VoidZone"
{
    // A Void Zone: a rectangular tear in the sky that devours placed bricks (VoidZoneModifier
    // owns the rules). The look is a black-hole take on the Vortex language: a deep dark eye
    // stretched to the zone's aspect, slow spiral arms of void-indigo swirling into it, a thin
    // accretion rim that pulses, and starlight specks orbiting inward. Falling pieces pass in
    // front of it untouched - the menace must read as a PLACE, not a wall, so edges feather
    // out and the centre stays truly black. Theme-independent fixed look (the Magma rule).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Aspect ("Zone Aspect (w/h)", Float) = 1
        _Seed ("Per-zone Seed", Float) = 0
        _EyeColor ("Eye Colour", Color) = (0.0, 0.0, 0.005, 1)
        _SwirlColor ("Swirl Colour", Color) = (0.16, 0.08, 0.28, 1)
        _RimColor ("Accretion Rim Colour", Color) = (0.55, 0.3, 0.95, 1)
        _SwirlSpeed ("Swirl Speed", Float) = 0.5
        _Hunger ("Hunger (feeding pulse)", Range(0, 1)) = 0
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
                float _Aspect;
                float _Seed;
                float4 _EyeColor;
                float4 _SwirlColor;
                float4 _RimColor;
                float _SwirlSpeed;
                float _Hunger;
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

            // Single-octave value noise - enough for a barely-there nebula whisper.
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

            half4 frag(Varyings i) : SV_Target
            {
                // The look: a PITCH-BLACK rectangle - space itself missing, zero see-through -
                // with a faint universe inside (sparse dim stars, a whisper of nebula) and ONE
                // thin coloured border tracing the near-square boundary. The border is alive:
                // hot arcs of energy circulate around the perimeter like a spinning wheel, and
                // sectors glitch-flicker at random ticks. Nothing concentric: the danger area
                // IS the rect and every element follows it, so 2x2 and 2x3 both read honestly.
                float2 p = (i.uv - 0.5) * float2(_Aspect, 1.0);
                float2 halfSize = float2(_Aspect * 0.5, 0.5);

                // Box SDF, corners only just softened (0.04) so they read square, not rounded.
                const float cornerR = 0.04;
                float2 q = abs(p) - (halfSize - cornerR);
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - cornerR;

                // The ORGANIC edge: low-amplitude noise displaces the boundary, so the hole is
                // ~95% square - splashed paint, not a die-cut sticker. The amplitude (~0.025
                // local, well under the modifier's overlapInset) keeps the visual honest about
                // the danger area, and the ultra-slow drift keeps the edge alive without
                // reading as an animation.
                float wob = vnoise(p * 6.0 + float2(_Seed * 3.1, _Time.y * 0.12)) - 0.5;
                d += wob * 0.05;
                if (d > 0.0) discard;

                float t = _Time.y * _SwirlSpeed;

                half3 col = _EyeColor.rgb;

                // A whisper of nebula: huge slow noise, barely above black.
                float neb = vnoise(p * 1.6 + float2(t * 0.12, _Seed));
                col += _SwirlColor.rgb * neb * 0.05;

                // Sparse dim stars drifting - the universe on the other side of the hole.
                float2 starP = (p + float2(t * 0.08, 0)) * 11.0;
                float2 starCell = floor(starP);
                float2 starLocal = frac(starP) - 0.5;
                float twinkle = 0.6 + 0.4 * sin(_Time.y * 1.7 + hash21(starCell) * 40.0);
                float mote = step(0.975, hash21(starCell)) *
                             (1.0 - smoothstep(0.03, 0.12, length(starLocal)));
                col += mote * 0.22 * twinkle;

                // THE border: a THIN line hugging the (wobbled) boundary...
                float border = smoothstep(-0.034, -0.018, d);
                // ...with two hot arcs CIRCULATING around the perimeter (the spinning-wheel
                // energy - the border stays put, the light travels along it)...
                float ang = atan2(p.y, p.x);
                float chase = pow(0.5 + 0.5 * sin(ang * 2.0 - _Time.y * (2.2 + 6.0 * _Hunger)), 5.0);
                // ...and OCCASIONAL per-sector glitch flickers - subtle bad reception on
                // reality, not a rave.
                float sector = floor(ang * 7.0 + 3.5);
                float tick = floor(_Time.y * 7.0);
                float glitch = step(0.95, hash21(float2(sector, tick))) * 0.5;
                float energy = 0.28 + 0.75 * chase + glitch;
                col += _RimColor.rgb * border * energy * (1.0 + 1.2 * _Hunger);

                // Fully opaque body; only a hair (~1px) of anti-aliasing at the boundary.
                float a = saturate(-d / 0.008) * i.color.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
