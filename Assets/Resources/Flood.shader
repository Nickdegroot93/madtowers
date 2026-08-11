Shader "MadTowers/Flood"
{
    // The Rising Flood surface (RisingFloodModifier owns the rules; FloodFx drives this).
    // CARTOON water, v3 (Nick 2026-08-10: v1 glowed like fog, v2 was "a white line over a
    // flat colour"). The Rayman-class recipe - layered, but every layer flat-shaded:
    //   - a BACK WAVE behind the crest (darker tone, own phase) so the waterline has depth,
    //   - a THIN broken foam crest, two-tone (bright lip over tinted base), with foam
    //     trails flaking off below it and a dark wet-line under it,
    //   - a smooth shallow->deep gradient body carrying two parallax layers of drifting
    //     light streaks (near-surface caustics) and sparse rising outline bubbles,
    //   - _Danger speeds/steepens everything and whitens the crest - the flood IS the timer.
    // No glow, no bloom, no soft mist - AA'd hard edges everywhere, mobile-cheap noise.
    // Component-driven time (_Phase) so a pause freezes the water.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _ShallowColor ("Shallow Colour", Color) = (0.22, 0.62, 0.58, 0.8)
        _DeepColor ("Deep Colour", Color) = (0.07, 0.30, 0.34, 0.88)
        _FoamColor ("Foam Colour", Color) = (0.88, 0.98, 0.92, 0.95)
        _SurfaceFrac ("Rest Waterline (uv.y)", Range(0.5, 0.98)) = 0.9
        _WaveAmp ("Wave Amplitude (uv)", Range(0, 0.05)) = 0.009
        _FoamBand ("Foam Band Thickness (uv)", Range(0, 0.05)) = 0.0045
        _TilesX ("World Units Across Quad", Float) = 300
        _Phase ("Scaled Time (driven)", Float) = 0
        _AgitPhase ("Agitated Time (driven)", Float) = 0
        _Danger ("Doom Proximity (driven)", Range(0, 1)) = 0
        _Seed ("Seed", Range(0, 1)) = 0
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
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float _SurfaceFrac;
                float _WaveAmp;
                float _FoamBand;
                float _TilesX;
                float _Phase;
                float _AgitPhase;
                float _Danger;
                float _Seed;
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

            // The swell: few, low, smooth, ROUND - cartoon water rolls, it never fizzes.
            // tAgit is the INTEGRATED agitated time (FloodFx advances it at 1 + danger*1.1x),
            // never `t * agit` computed here: multiplying total elapsed time made the phase
            // jump by t * Δagit whenever danger moved - the waterline visibly reseated every
            // block landing, worse the longer the run (Nick 2026-08-11). Danger now only
            // changes how FAST the rolls travel; the slow third term stays on plain time.
            float swell(float x, float t, float tAgit)
            {
                return sin(x * 0.55 + tAgit * 0.9) * 0.6
                     + sin(x * 1.15 - tAgit * 1.3 + 2.1) * 0.4
                     + sin(x * 0.13 + t * 0.23) * 0.35;
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
                // x in WORLD units (the quad is _TilesX wide): physical wavelengths.
                float x = IN.uv.x * _TilesX + _Seed * 37.0;
                float t = _Phase;
                float tA = _AgitPhase;
                // Amplitude/steepness scaling only - every PHASE reads the integrated _AgitPhase.
                float agit = 1.0 + _Danger * 1.1;
                float aa = max(fwidth(IN.uv.y) * 1.5, 0.0006);

                // FRONT surface and the BACK WAVE: the back line rides slightly higher with
                // its own phase, so crests part and cross - the waterline gets a far side.
                float surf = _SurfaceFrac + swell(x, t, tA) * _WaveAmp * agit;
                float surfB = _SurfaceFrac + _WaveAmp * 0.9
                            + swell(x * 1.2 + 5.3, t * 1.15 + 1.0, tA * 1.15 + 1.0) * _WaveAmp * agit;
                float d = surf - IN.uv.y;                  // > 0 below the front waterline
                float dB = surfB - IN.uv.y;

                float inWater = smoothstep(0.0, aa, d);
                float inBack = smoothstep(0.0, aa, dB) * (1.0 - inWater);

                // BODY: smooth depth gradient (v2's flat two-band read as unfinished).
                // Depth gates below are in QUAD uv - the quad is 24 world units tall, so
                // 0.1 uv = 2.4 m. Detail must DECAY with depth (the AAA hierarchy): busy
                // in the top ~2 m, calm below, or the whole body reads soapy.
                float4 body = lerp(_ShallowColor, _DeepColor, smoothstep(0.01, 0.25, d));

                // LIGHT STREAKS: two parallax layers of horizontally-stretched drifting
                // noise, gated sparse - flat caustic ribbons that die out fast with depth.
                float3 glint = lerp(_ShallowColor.rgb, float3(1, 1, 1), 0.55);
                float s1 = vnoise(float2(x * 0.85 - tA * 0.9, IN.uv.y * 260.0));
                s1 = smoothstep(0.64, 0.72, s1) * (1.0 - smoothstep(0.01, 0.07, d));
                float s2 = vnoise(float2(x * 0.4 + t * 0.35, IN.uv.y * 150.0 + 7.0));
                s2 = smoothstep(0.68, 0.78, s2) * (1.0 - smoothstep(0.03, 0.14, d));
                body.rgb += glint * (s1 * 0.16 + s2 * 0.10);

                // BUBBLES: sparse rising outline circles, cartoon-flat, mid-water only.
                float2 bg = float2(x * 0.55, IN.uv.y * 16.0 - t * 0.5);
                float2 bid = floor(bg);
                float2 bf = frac(bg) - 0.5;
                float brnd = hash21(bid + _Seed * 11.0);
                float2 bjit = float2(frac(brnd * 13.7), frac(brnd * 29.3)) * 0.5 - 0.25;
                float brad = 0.05 + 0.06 * frac(brnd * 7.1);
                float bdist = length(bf - bjit);
                float bubble = smoothstep(brad, brad - 0.02, bdist)
                             - smoothstep(brad - 0.035, brad - 0.055, bdist); // ring, not disc
                bubble *= step(0.78, brnd);                                    // sparse
                bubble *= smoothstep(0.03, 0.10, d) * (1.0 - smoothstep(0.25, 0.55, d));
                body.rgb += glint * saturate(bubble) * 0.30;

                // WET LINE: a thin darker seam right under the foam - anchors the crest.
                float foamW = _FoamBand * (1.0 + _Danger * 0.8);
                float wet = smoothstep(foamW, foamW + aa, d)
                          * (1.0 - smoothstep(foamW * 2.2, foamW * 2.2 + aa, d));
                body.rgb *= 1.0 - wet * 0.18;

                // FOAM CREST: thin and TWO-TONE (near-white lip over the tinted base),
                // broken by drifting gaps, with trails flaking off below.
                float inFoam = inWater * (1.0 - smoothstep(foamW, foamW + aa, d));
                float gaps = vnoise(float2(x * 1.3 + t * 0.5, _Seed * 5.0));
                float foamMask = smoothstep(0.28, 0.45, gaps);                 // mostly on, honest gaps
                float lip = 1.0 - smoothstep(0.0, foamW * 0.45, d);            // top sliver of the band
                float3 foamCol = lerp(_FoamColor.rgb * 0.88, lerp(_FoamColor.rgb, float3(1, 1, 1), 0.7), lip);
                float trails = vnoise(float2(x * 2.1 - t * 0.7, IN.uv.y * 220.0 + 40.0));
                trails = smoothstep(0.72, 0.82, trails)
                       * smoothstep(foamW * 5.0, foamW * 1.5, d) * inWater;

                float foamAmt = saturate(inFoam * foamMask + trails * 0.8);
                // Doom whitens the crest - flat, never glowing.
                foamCol = lerp(foamCol, float3(1, 1, 1), _Danger * 0.5);
                body.rgb = lerp(body.rgb, foamCol, foamAmt);

                // Compose: front water over the darker back wave.
                float3 backCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, 0.30) * 0.82;
                float3 rgb = lerp(backCol, body.rgb, inWater);
                float alpha = max(body.a * inWater, _ShallowColor.a * 0.9 * inBack);
                alpha = max(alpha, saturate(foamAmt) * _FoamColor.a);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
