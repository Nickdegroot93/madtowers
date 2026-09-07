Shader "MadTowers/Flood"
{
    // The Rising Flood surface (RisingFloodModifier owns the rules; FloodFx drives this).
    // Historical CARTOON water, v3 (Nick 2026-08-10: v1 glowed like fog, v2 was "a white line over a
    // flat colour"). The former Rayman-class recipe, preserved here as design history:
    //   - a BACK WAVE behind the crest (darker tone, own phase) so the waterline has depth,
    //   - a THIN broken foam crest, two-tone (bright lip over tinted base), with foam
    //     trails flaking off below it and a dark wet-line under it,
    //   - a smooth shallow->deep gradient body carrying two parallax layers of drifting
    //     light streaks (near-surface caustics) and sparse rising outline bubbles,
    //   - _Danger speeds/steepens everything and whitens the crest - the flood IS the timer.
    // No glow, no bloom, no soft mist - AA'd hard edges everywhere, mobile-cheap noise.
    // Component-driven time (_Phase) so a pause freezes the water.
    // Painted-water pass (September 2026): the rejected fog glow / white cord above remain
    // rejected. A dark rolling shoulder supports scattered foam; absorption, half-resolution
    // scene refraction/reflection and broken caustic ribbons give the body volume. Exactly TWO
    // tileable-noise taps, no per-fragment hash. Phase and danger envelopes remain FloodFx's.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _NoiseTex ("Tileable surface noise", 2D) = "gray" {}
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
        _Splash ("Swallow ripple: x, phase", Vector) = (0,-100,0,0)
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
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_FloodSceneTex); SAMPLER(sampler_FloodSceneTex);

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
                float2 uv          : TEXCOORD0;
                float2 worldXY     : TEXCOORD1;
            };

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
                OUT.worldXY = TransformObjectToWorld(IN.positionOS.xyz).xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float x = IN.worldXY.x + _Seed * 37.0;
                float t = _Phase, tA = _AgitPhase;
                float agit = 1.0 + _Danger * 1.1;
                float aa = max(fwidth(IN.uv.y) * 1.1, 0.0005);
                float surf = _SurfaceFrac + swell(x, t, tA) * _WaveAmp * agit;
                float surfB = _SurfaceFrac + _WaveAmp * .8
                    + swell(x * 1.2 + 5.3, t * 1.15 + 1.0, tA * 1.15 + 1.0) * _WaveAmp * agit;
                float age = max(0, t - _Splash.y);
                float dx = IN.worldXY.x - _Splash.x;
                float ripple = exp(-age*4) * sin(age*13-abs(dx)*3) * exp(-dx*dx*.6) * saturate(age*25);
                surf += ripple*.012;
                float d = surf - IN.uv.y;
                float inWater = smoothstep(0, aa, d);
                float inBack = smoothstep(0, aa, surfB - IN.uv.y) * (1-inWater);
                float depth = max(0, d * 24.0);

                // Shared two samples: broad body planes, current, foam breakup and ribbons.
                float4 n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                    float2(x * .042 - t * .009, depth * .035 + t * .004));
                float4 n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                    float2(x * .105 + tA * .018, depth * .14 - t * .012));
                float current = n1.r * .65 + n2.g * .35;
                float slope = cos(x*.55+tA*.9)*.33 + cos(x*1.15-tA*1.3+2.1)*.46;
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                float2 bend = float2((current-.5)*.007 + slope*.0015, (n2.r-.5)*.002);
                bend *= smoothstep(0,.35,depth) * (1 + _Danger*.35);
                half3 scene = SAMPLE_TEXTURE2D(_FloodSceneTex, sampler_FloodSceneTex, saturate(screenUV+bend)).rgb;

                float absorption = 1-exp(-depth*.38-.28);
                float planes = .78 + n1.g*.36 + (n2.r-.5)*.10;
                half3 pigment = lerp(_ShallowColor.rgb*.84, _DeepColor.rgb*.66,
                    saturate(depth*.17 + (n1.r-.5)*.24));
                half3 tint = lerp(half3(1,1,1), _ShallowColor.rgb/max(.1,max(_ShallowColor.r,max(_ShallowColor.g,_ShallowColor.b))), .38);
                half3 body = lerp(scene*tint, pigment*planes, absorption);

                // Reflection is a second scene tap only in the shallow band. The source
                // includes the actual tower and sky, bilinearly softened at half resolution.
                if (depth < 5.0)
                {
                    float2 reflectedUV = float2(screenUV.x + bend.x*1.7,
                        screenUV.y + 2*depth/max(1,unity_OrthoParams.y) + bend.y);
                    half3 reflection = SAMPLE_TEXTURE2D(_FloodSceneTex, sampler_FloodSceneTex, saturate(reflectedUV)).rgb;
                    body = lerp(body, reflection * tint, exp(-depth*.85) * (.19+n2.g*.12));
                }
                float ribbon = 1-smoothstep(.025,.15,abs(sin(depth*9 + n1.r*5 + sin(x*.8+t*.5)*.65)));
                ribbon *= smoothstep(.45,.7,n2.g) * exp(-depth*1.05) * smoothstep(.08,.3,depth);
                body += lerp(_ShallowColor.rgb, _FoamColor.rgb, .42) * ribbon * .16;

                // The rounded shoulder has a shaded underside and a few broad, lit facets.
                float shoulder = exp(-depth*5);
                float light = saturate(.35 + slope*.55 + n2.r*.28);
                body *= 1 - exp(-abs(depth-.17)*14)*.30;
                body = lerp(body, _ShallowColor.rgb*(.68+light*.43), shoulder*.48);
                float foamW = _FoamBand * .52 * (1 + _Danger*.8);
                float foam = inWater * (1-smoothstep(foamW,foamW+aa,d));
                float breaks = smoothstep(.43,.66,n2.g + sin(x*3.2+tA*.7)*.12);
                foam *= breaks;
                half3 foamColor = lerp(_FoamColor.rgb*.64,_FoamColor.rgb,light);
                body = lerp(body,foamColor,foam*.90);
                half3 back = lerp(_DeepColor.rgb,_ShallowColor.rgb,.32)*.72;
                float alpha = saturate(inWater + inBack*.9);
                return half4(lerp(back,body,inWater),alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
