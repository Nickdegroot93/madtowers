Shader "MadTowers/Vortex"
{
    // The Vortex BRICK: the whole cell is churning dusk marble - spiral arms winding into a dark eye -
    // framed by the standard brick language (rounded box, bevel, near-black outline) so it reads as a
    // BRICK made of vortex, not chapter art with a gem stuck on (the original inset-disc overlay look
    // was rejected as ugly - Nick, July 2026). Fixed look in every chapter (BLOCKVARIANTS convention for
    // replace-mode skins); the palette deliberately echoes its home chapter, Fangkuai District's dusk
    // pinks: deep plum body, magenta-rose arms, blossom-cream vein cores, lantern-red ember accents.
    // "Vortex" isn't a material, so this is an optical cue: the skin winds and REVERSES _Swirl (scaled
    // time - a pause freezes it, PHYSICS.md), the on-block metaphor for inverted left/right steering.
    // _Seed varies each cell so a multi-cell piece isn't N identical swirls. See BLOCKVARIANTS.md.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _DeepColor ("Deep (dusk plum)", Color) = (0.24, 0.07, 0.20, 1)
        _MidColor ("Arms (magenta rose)", Color) = (0.72, 0.25, 0.42, 1)
        _LightColor ("Vein cores (blossom cream)", Color) = (0.96, 0.80, 0.74, 1)
        _AccentColor ("Ember accent (lantern red)", Color) = (0.92, 0.26, 0.28, 1)
        _Swirl ("Swirl angle (driven)", Float) = 0
        _Seed ("Per-cell seed", Float) = 0
        _TwistAmt ("Vortex twist", Range(0, 8)) = 2.6
        _Arms ("Spiral arms", Range(1, 8)) = 3
        _Spiral ("Spiral tightness", Range(-8, 8)) = 4.5
        _WarpFreq ("Marble warp freq", Range(1, 8)) = 3.5
        _WarpAmt ("Marble warp amount", Range(0, 4)) = 1.8
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.08
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.055
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.10
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
                float _TwistAmt;
                float _Arms;
                float _Spiral;
                float _WarpFreq;
                float _WarpAmt;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
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
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
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

                // Brick silhouette: rounded box + AA mask (the standard full-look frame).
                float halfBox = 0.5;
                float rad = min(_CornerRadius, halfBox - 0.001);
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(p, bb, rad);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);
                if (mask <= 0.001) return half4(0, 0, 0, 0);

                // Whirlpool covering the WHOLE cell: normalize radius to the corner distance so the
                // spiral arms sweep out past the edges instead of dying in a disc.
                float r = length(p);
                float nr = saturate(r / 0.7071);               // 0 centre .. 1 at the corners
                float theta = atan2(p.y, p.x);
                float phase = _Swirl + _Seed * 6.2831853;
                float swirlTheta = theta + phase + (1.0 - nr) * _TwistAmt;

                // Domain-warped marble veins following the spiral - they churn as _Swirl changes.
                float2 sp = float2(cos(swirlTheta), sin(swirlTheta)) * nr;
                float warp = fbm(sp * _WarpFreq + _Seed * 7.3);
                float veins = 0.5 + 0.5 * sin(swirlTheta * _Arms + nr * _Spiral + warp * _WarpAmt);
                veins = pow(saturate(veins), 1.4);

                // Dusk ramp: plum body -> rose arms -> cream vein cores, with a lantern-red ember
                // band riding the arm edges (strongest mid-radius, like sparks caught in the spin).
                float3 col = lerp(_DeepColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.62, veins));
                col = lerp(col, _LightColor.rgb, smoothstep(0.70, 1.0, veins));
                col += _AccentColor.rgb * (0.20 * (1.0 - abs(veins - 0.55) * 2.6)) * saturate(nr * 1.6) * (1.0 - nr * 0.5);

                // Ember specks orbiting with the churn (lantern red, not starlight).
                float2 star = float2(swirlTheta * 1.2732, nr * 7.0);
                float sh = hash21(floor(star) + _Seed);
                float spk = step(0.96, sh) * smoothstep(0.4, 0.1, length(frac(star) - 0.5));
                col += _AccentColor.rgb * spk * (0.45 + 0.45 * sin(_Swirl * 3.0 + sh * 6.2831));

                // Dark eye at the centre - present but shallow, so the brick reads solid, not holed.
                col *= lerp(0.42, 1.0, smoothstep(0.0, 0.24, nr));

                // Bevel: lit top edge, shaded bottom edge (the shared brick lighting language).
                float e = 0.012;
                float dY = sdRoundBox(p + float2(0, e), bb, rad) - sdRoundBox(p - float2(0, e), bb, rad);
                float ny = dY / (2.0 * e);
                float band = pow(saturate((d + _OutlineWidth + _BevelWidth) / max(_BevelWidth, 0.001)), 1.6);
                band *= saturate((-d - _OutlineWidth * 0.55) / max(_OutlineWidth * 0.45, 0.001));
                float topness = saturate((ny - 0.25) / 0.5);
                float botness = saturate((-ny - 0.25) / 0.5);
                col = lerp(col, _LightColor.rgb, 0.34 * band * topness);
                col *= (1.0 - 0.22 * band * botness);

                // Outline: closed, darkest thing on the brick (plum-black, matching the tower language).
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                col = lerp(col, _DeepColor.rgb * 0.30, tOut);

                return half4(col, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
