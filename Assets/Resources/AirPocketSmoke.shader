Shader "MadTowers/AirPocketSmoke"
{
    // Airtight mode's sealed-pocket hazard: ONE volume of dark pressure-smoke swelling out of
    // the pocket's centre until it fills the whole sealed region. The quad is just a window
    // onto a single WORLD-SPACE field (density, noise and the ember edge are all evaluated
    // in world coordinates); the pocket's SHAPE comes from _MaskTex - a tiny bilinear texture
    // with one texel per sealed cell, sampled in world space. The smoke's boundary is the
    // mask's soft edge displaced by noise, so a full pocket still reads as a torn, churning
    // cloud that bulges a fraction of a cell past the bricks - never as a clean rectangle.
    // _Fill 0..1 drives the blob's radius from the centre (_Center/_Extent, set per pocket by
    // AirPocketFx); the boundary churns faster as the fuse runs down, and a pulsing ember rim
    // rides the smoke's edge. Theme-independent by design - identical in every chapter (same
    // rule as Magma's fixed look).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _MaskTex ("Pocket Cell Mask", 2D) = "white" {}
        _MaskOrigin ("Mask Origin (world xy of texel 0,0)", Vector) = (0, 0, 0, 0)
        _MaskInvSize ("Mask Inverse World Size", Vector) = (1, 1, 0, 0)
        _Fill ("Fill Level", Range(0, 1)) = 0
        _Center ("Region Centre (world xy)", Vector) = (0, 0, 0, 0)
        _Extent ("Region Extent (world units)", Float) = 1
        _Seed ("Per-pocket Seed", Float) = 0
        _SmokeColor ("Smoke Colour", Color) = (0.05, 0.03, 0.045, 0.92)
        _DeepColor ("Deep Smoke Colour", Color) = (0.01, 0.005, 0.012, 0.97)
        _EmberColor ("Ember Rim Colour", Color) = (1.0, 0.32, 0.14, 1)
        _RimStrength ("Rim Strength", Range(0, 3)) = 1.2
        _NoiseScale ("Noise Scale (per world unit)", Float) = 2.6
        _ScrollSpeed ("Churn Speed", Float) = 0.4
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
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MaskOrigin;
                float4 _MaskInvSize;
                float _Fill;
                float4 _Center;
                float _Extent;
                float _Seed;
                float4 _SmokeColor;
                float4 _DeepColor;
                float4 _EmberColor;
                float _RimStrength;
                float _NoiseScale;
                float _ScrollSpeed;
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
                float2 worldXY     : TEXCOORD0;
                float4 color       : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(world);
                o.worldXY = world.xy;
                o.color = v.color;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345) + _Seed);
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            // Two-octave value noise: enough body for churning smoke, cheap enough for mobile.
            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float smoke(float2 p, float t)
            {
                float n = vnoise(p + float2(t * 0.5, t * 0.3));
                n = 0.6 * n + 0.4 * vnoise(p * 2.2 - float2(t * 0.35, t * 0.6));
                return n;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Urgency ramps the churn as the pocket nears full.
                float t = _Time.y * _ScrollSpeed * (1.0 + _Fill * 1.5);
                float2 fromCenter = i.worldXY - _Center.xy;
                float d = length(fromCenter) / max(_Extent, 0.001);

                // A slow swirl: the noise field rotates around the centre, so the cloud
                // visibly churns instead of just scintillating in place.
                float ang = 0.35 * sin(_Time.y * 0.5 + _Seed);
                float2 sw = float2(
                    fromCenter.x * cos(ang) - fromCenter.y * sin(ang),
                    fromCenter.x * sin(ang) + fromCenter.y * cos(ang));
                float n = smoke((_Center.xy + sw) * _NoiseScale, t);

                // The blob's torn edge: radius grows with _Fill past the farthest corner, and
                // the boundary is displaced by the noise so it billows, never a clean circle.
                float radius = _Fill * 1.35;
                float radialEdge = radius - d - (n - 0.5) * 0.38;

                // The pocket's shape: the mask is bilinear, so it crosses 0.5 exactly at the
                // cell boundary and fades over one cell. A second, finer churn field displaces
                // that threshold, so the smoke bulges past the bricks by a fraction of a cell -
                // pressure straining at the seams - instead of clipping to a hard rectangle
                // once the blob outgrows the cavity.
                float2 maskUV = (i.worldXY - _MaskOrigin.xy) * _MaskInvSize.xy;
                float m = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, maskUV).r;
                float nEdge = smoke((_Center.xy + sw) * _NoiseScale * 1.9 + 37.0, t * 0.7);
                float maskEdge = (m - 0.5 + (nEdge - 0.5) * 0.5) * 1.5;

                float edge = min(radialEdge, maskEdge);
                float density = smoothstep(0.0, 0.22, edge);
                if (density <= 0.003) discard;

                // Body: deepest at the heart of the cloud, mottled by the churn.
                half4 body = lerp(_SmokeColor, _DeepColor, saturate(edge * 1.6));
                body.rgb *= 0.85 + 0.3 * n;

                // Ember rim riding the torn edge, pulsing faster as the fuse runs down. It
                // burns hottest near the source in the first beat, then chases the boundary.
                float pulse = 0.75 + 0.25 * sin(_Time.y * (2.0 + 10.0 * _Fill) + _Seed * 17.0);
                float rim = 1.0 - saturate(abs(edge - 0.05) * 7.0);
                rim = rim * rim * _RimStrength * pulse * (0.35 + 0.65 * _Fill);
                body.rgb += _EmberColor.rgb * rim;

                // Soft global fade-in for the first moments (smoke POURING in, not popping on).
                body.a *= density * saturate(_Fill * 5.0) * i.color.a;
                return body;
            }
            ENDHLSL
        }
    }
}
