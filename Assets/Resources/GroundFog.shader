Shader "MadTowers/GroundFog"
{
    // The "living fog" the floor terrain dissolves into (FloorTerrain.BuildFog owns the geometry,
    // FLOORS.md section 3 the rules). Replaces the flat single-colour alpha-ramp bands of 2026-09:
    //   - TWO-TONE: a deep shade at the bottom lifting to a lit haze at the top, so the bank has
    //     volume instead of being one wall of colour,
    //   - a NOISE-BROKEN top edge that breathes (two octaves of tileable value noise from a
    //     texture, scrolling at different speeds - two taps per pixel, mobile-cheap, never
    //     per-pixel hash noise for something that is always on screen),
    //   - a thin lit RIM where the fog top catches sky light,
    //   - WORLD-SPACE sampling: the quads follow the camera, the fog pattern does not, so a pan
    //     parallaxes the back and front layers against each other for free.
    // Below _BottomY the density clamps to 1 and the colour to _DeepColor, so the quad can run
    // as deep as the camera can ever see and no band bottom or raw backdrop ever shows.
    // Component-driven time (_Phase, scaled) so a pause freezes the fog.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _NoiseTex ("Tileable Noise", 2D) = "gray" {}
        _LightColor ("Lit Haze (top)", Color) = (0.55, 0.62, 0.58, 1)
        _DeepColor ("Deep Shade (bottom)", Color) = (0.08, 0.14, 0.12, 1)
        _RimColor ("Rim (sky light)", Color) = (0.8, 0.85, 0.8, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.25
        _Density ("Density (max alpha)", Range(0, 1)) = 0.9
        _TopY ("Fog Top (world y, density 0)", Float) = 0
        _BottomY ("Fog Bottom (world y, density 1)", Float) = -5
        _EdgeAmp ("Edge Break (fraction of span)", Range(0, 1)) = 0.5
        _NoiseScale ("Noise Scale", Float) = 1
        _Drift ("Drift (world units/s, signed)", Float) = 0.35
        _Phase ("Scaled Time (driven)", Float) = 0
        _Splash ("Splash: x, unused, start time, strength (driven)", Vector) = (0, 0, -100, 0)
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
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _LightColor;
                float4 _DeepColor;
                float4 _RimColor;
                float _RimStrength;
                float _Density;
                float _TopY;
                float _BottomY;
                float _EdgeAmp;
                float _NoiseScale;
                float _Drift;
                float _Phase;
                float4 _Splash;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 worldXY     : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 ws = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(ws);
                OUT.worldXY = ws.xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 w = IN.worldXY;
                float t = _Phase;
                float span = max(0.01, _TopY - _BottomY);
                // 0 at the dense bottom, 1 at the nominal top, > 1 in the headroom above.
                float h = (w.y - _BottomY) / span;

                // Fog is wider than it is tall: stretch the noise horizontally (x period ~11 u,
                // y period ~4.5 u at scale 1). Octave 2 is finer, scrolls the other way and
                // RISES slowly (tendrils lifting off the bank - the most readable "alive" cue),
                // so the two never read as one sliding sheet. Motion must register within a
                // couple of seconds yet stay calm: at the default 0.35 u/s a wisp crosses its
                // own width in ~10 s (0.1 u/s was "no movement", Nick 2026-09-04).
                float2 freq = float2(0.09, 0.22) * _NoiseScale;
                float2 p1 = (w + float2(t * _Drift, 0.0)) * freq;
                float2 p2 = (w * 1.9 + float2(-t * _Drift * 0.6, t * 0.12)) * freq + 0.37;
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p1).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p2).r;
                float n = n1 * 0.65 + n2 * 0.35 - 0.5;                 // -0.5 .. 0.5

                // The top edge is the height field displaced by the noise; density falls off
                // smoothly toward it. Below h = 0.3 the break can never reach, so the bottom
                // of the ramp (and everything under it) is exactly _Density / _DeepColor.
                // Plus a slow BREATH: two long low swells along x lift and lower the whole top
                // edge (about 0.1 of the span), so the bank heaves instead of only sliding.
                float breath = sin(w.x * 0.35 + t * 0.55) * 0.06
                             + sin(w.x * 0.9 - t * 0.4 + 1.7) * 0.04;
                // SPLASH: where a block sank, the bank heaves up and settles back over ~1 s
                // (a damped bob, ~1.6 u wide) - the same fog the LifeLossFx puffs are torn from.
                float age = t - _Splash.z;
                float heave = exp(-age * 2.6) * saturate(age * 40.0)            // instant on, slow decay
                            * exp(-(w.x - _Splash.x) * (w.x - _Splash.x) * 0.7)
                            * sin(age * 7.0) * 0.32 * _Splash.w;
                float edge = h + n * _EdgeAmp + breath - heave;
                float dens = 1.0 - smoothstep(0.3, 1.0, edge);
                dens *= dens * (3.0 - 2.0 * dens);                    // ease: softer shoulder
                float alpha = dens * _Density;

                // Colour: deep at the base, lit toward the top; the noise also mottles the
                // interior (brighter where the fog is thinner), fading to nothing at h = 0.
                float lift = saturate(h * 1.15 + n * 0.35 * saturate(h * 2.0));
                float3 col = lerp(_DeepColor.rgb, _LightColor.rgb, lift);

                // Rim: a thin lit band just under the displaced top edge.
                float rim = smoothstep(0.42, 0.62, edge) * (1.0 - smoothstep(0.62, 0.92, edge));
                col = lerp(col, _RimColor.rgb, rim * _RimStrength);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
