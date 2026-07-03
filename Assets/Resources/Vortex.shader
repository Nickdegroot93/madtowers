Shader "MadTowers/Vortex"
{
    // A Vortex VORTEX overlay: an inset disc of swirling pink marble set into each cell, drawn ON TOP of the
    // kept chapter art (like Vine, unlike Anchor/Boulder which replace it) so the brick stays solid and the
    // chapter colour shows in the frame around the gem. "Vortex" isn't a material, so this is an optical cue:
    // a churning whirlpool that the skin winds and REVERSES (drives _Swirl) - the on-block metaphor for
    // inverted left/right steering. Fixed pink/plum marble (theme-independent). _Swirl (radians, driven on
    // scaled time so a pause freezes it) rotates the vortex; the inner winds more than the rim (_TwistAmt).
    // _Seed varies each cell so a multi-cell piece isn't N identical gems. See BLOCKVARIANTS.md.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _DeepColor ("Deep (void indigo)", Color) = (0.13, 0.09, 0.32, 1)
        _MidColor ("Mid (violet)", Color) = (0.44, 0.30, 0.82, 1)
        _LightColor ("Vein / starlight", Color) = (0.82, 0.88, 1.0, 1)
        _AccentColor ("Energy accent (cyan)", Color) = (0.30, 0.92, 0.95, 1)
        _Swirl ("Swirl angle (driven)", Float) = 0
        _Seed ("Per-cell seed", Float) = 0
        _DiscRadius ("Disc radius", Range(0.1, 0.5)) = 0.36
        _Edge ("Edge softness", Range(0.005, 0.1)) = 0.03
        _TwistAmt ("Vortex twist", Range(0, 8)) = 3.2
        _Arms ("Spiral arms", Range(1, 8)) = 3
        _Spiral ("Spiral tightness", Range(-8, 8)) = 4.0
        _WarpFreq ("Marble warp freq", Range(1, 8)) = 3.5
        _WarpAmt ("Marble warp amount", Range(0, 4)) = 2.0
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
                float4 _DeepColor;
                float4 _MidColor;
                float4 _LightColor;
                float4 _AccentColor;
                float _Swirl;
                float _Seed;
                float _DiscRadius;
                float _Edge;
                float _TwistAmt;
                float _Arms;
                float _Spiral;
                float _WarpFreq;
                float _WarpAmt;
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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.uv - 0.5;
                float r = length(p);

                // Inset disc: opaque gem inside _DiscRadius, fully transparent outside so the brick frames it.
                float disc = smoothstep(_DiscRadius, _DiscRadius - _Edge, r);
                if (disc <= 0.001) return half4(0, 0, 0, 0);

                float nr = saturate(r / _DiscRadius);          // 0 centre .. 1 rim
                float theta = atan2(p.y, p.x);
                float phase = _Swirl + _Seed * 6.2831853;

                // Whirlpool: the inner winds more than the rim, and the whole thing rotates with _Swirl.
                float swirlTheta = theta + phase + (1.0 - nr) * _TwistAmt;

                // Domain-warped marble veins following the spiral - they churn as _Swirl changes.
                float2 sp = float2(cos(swirlTheta), sin(swirlTheta)) * nr;
                float warp = fbm(sp * _WarpFreq + _Seed * 7.3);
                float veins = 0.5 + 0.5 * sin(swirlTheta * _Arms + nr * _Spiral + warp * _WarpAmt);
                veins = pow(saturate(veins), 1.4);

                // Galaxy colour ramp: void indigo -> violet arms -> starlight vein cores, with a cyan
                // energy band riding the arm edges.
                float3 col = lerp(_DeepColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.6, veins));
                col = lerp(col, _LightColor.rgb, smoothstep(0.66, 1.0, veins));
                col += _AccentColor.rgb * (0.22 * (1.0 - abs(veins - 0.55) * 2.5)) * saturate(nr * 2.0);

                // Star specks caught in the spiral (they orbit with the churn).
                float2 star = float2(swirlTheta * 1.2732, nr * 7.0);
                float sh = hash21(floor(star));
                float spk = step(0.955, sh) * smoothstep(0.4, 0.1, length(frac(star) - 0.5));
                col += _LightColor.rgb * spk * (0.5 + 0.5 * sin(_Swirl * 3.0 + sh * 6.2831));

                // Deep dark vortex eye at the centre - the void you steer into.
                col *= lerp(0.28, 1.0, smoothstep(0.0, 0.30, nr));

                // Glassy dome highlight (top-left) so it reads as a recessed gem, not a flat decal.
                float2 nd = p / max(r, 1e-4);
                float dome = saturate(dot(nd, normalize(float2(-0.6, 0.8))));
                col += _LightColor.rgb * pow(dome, 2.0) * (1.0 - nr) * 0.18;

                // Thin darker socket ring at the rim, seating the gem into the brick.
                float ring = smoothstep(_DiscRadius - _Edge * 1.8, _DiscRadius - _Edge * 0.4, r);
                col *= (1.0 - ring * 0.45);

                return half4(col, disc);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
