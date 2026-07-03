Shader "MadTowers/Bomb"
{
    // Bomb cell: a near-black iron POWDER KEG. Two riveted reinforcement bands hoop each cell, and a
    // recessed round fuse-porthole sits in the centre with an ember burning inside. As the fuse runs
    // (_Fuse 0 -> 1, driven by BombBlockSkin with an accelerating heartbeat in _Pulse) jagged radial
    // cracks split outward from the porthole and the glow climbs from sleepy ember to white-hot pre-flash.
    // Framed by the shared brick recipe (gradient, embossed bevel, near-black outline, grain).
    // Theme-independent (the chapter art is hidden).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _IronColor ("Iron Colour", Color) = (0.25, 0.26, 0.31, 1)
        _BandColor ("Band Colour", Color) = (0.42, 0.44, 0.50, 1)
        _EmberColor ("Ember Colour", Color) = (1.0, 0.55, 0.15, 1)
        _HotColor ("White-hot Colour", Color) = (1.0, 0.93, 0.80, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.086
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.066
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.102
        _CoreRadius ("Fuse Porthole Radius", Range(0.05, 0.3)) = 0.17
        _IdleEmber ("Idle Ember Strength", Range(0, 1)) = 0.35
        _Fuse ("Fuse (0..1, driven)", Range(0, 1)) = 0
        _Pulse ("Heartbeat (0..1, driven)", Range(0, 1)) = 0
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
                float4 _IronColor;
                float4 _BandColor;
                float4 _EmberColor;
                float4 _HotColor;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float _CoreRadius;
                float _IdleEmber;
                float _Fuse;
                float _Pulse;
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

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, float2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;
            }

            // A dome rivet: returns (mask, shade) - top-left highlight, dark seat ring.
            float2 rivet(float2 p, float2 c, float rad, float aa)
            {
                float2 d = p - c;
                float sd = length(d) - rad;
                float m = 1.0 - smoothstep(0.0, aa, sd);
                float2 nd = d / max(length(d), 1e-4);
                float dome = saturate(dot(nd, normalize(float2(-1.0, 1.0))));
                float shade = (dome - 0.45) * m;
                float ring = smoothstep(0.0, aa, sd) * (1.0 - smoothstep(aa, rad * 0.6, sd));
                return float2(m, shade - ring * 0.5);
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
                float3 iron = _IronColor.rgb;

                float2 p = uv - 0.5;
                float halfBox = 0.5;
                float r = min(_CornerRadius, halfBox - 0.001);
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(p, bb, r);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                // Shared frame: gradient + embossed bevel + AO ring.
                float grad = 1.13 - 0.36 * pow(saturate(1.0 - uv.y), 1.15);
                float3 body = iron * grad;
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
                body *= (1.0 - 0.09 * band);
                float3 hiCol = lerp(iron * 1.6, 1.0 - (1.0 - iron) * 0.42, 0.4) * grad;
                body = lerp(body, hiCol, 0.60 * band * topness);
                body *= (1.0 - 0.26 * band * botness);
                body *= (1.0 - 0.12 * band * sideness);

                // Brushed iron grain.
                float streak = (hash21(float2(floor(uv.y * 150.0), 3.0)) - 0.5) * 0.05;
                body *= (1.0 + streak);

                // Two riveted reinforcement bands hooping the keg (raised: lit top edge, shaded bottom).
                float bandHalf = 0.052;
                float by1 = abs(uv.y - 0.235) - bandHalf;
                float by2 = abs(uv.y - 0.765) - bandHalf;
                float bandD = min(by1, by2);
                float bandMask = smoothstep(0.008, 0.0, bandD);
                float3 bandCol = _BandColor.rgb * grad;
                float bandEdgeHi = smoothstep(0.014, 0.0, abs(bandD + bandHalf * 1.6)) * 0.35;  // top lip
                float bandEdgeLo = smoothstep(0.014, 0.0, abs(bandD - 0.004)) * 0.30;           // seat shadow
                body = lerp(body, bandCol * (1.0 + bandEdgeHi) * (1.0 - bandEdgeLo), bandMask);

                float2 rv;
                rv = rivet(p, float2(-0.30, 0.265), 0.030, aa);
                float rivetMask = rv.x; float rivetShade = rv.y;
                rv = rivet(p, float2( 0.30, 0.265), 0.030, aa); rivetMask = max(rivetMask, rv.x); rivetShade += rv.y;
                rv = rivet(p, float2(-0.30, -0.265), 0.030, aa); rivetMask = max(rivetMask, rv.x); rivetShade += rv.y;
                rv = rivet(p, float2( 0.30, -0.265), 0.030, aa); rivetMask = max(rivetMask, rv.x); rivetShade += rv.y;
                body = lerp(body, _BandColor.rgb * 1.35 * grad, rivetMask * 0.6);
                body *= (1.0 + rivetShade * 0.9);

                // Fuse energy: idle ember -> accelerating heartbeat -> white-hot pre-flash.
                float heat = _IdleEmber * (0.7 + 0.3 * _Pulse) + _Fuse * (1.2 + 1.8 * _Fuse) * (0.6 + 0.4 * _Pulse);
                float3 hot = lerp(_EmberColor.rgb, _HotColor.rgb, saturate(_Fuse * 1.2 - 0.2));

                // Recessed fuse porthole: dark seat ring, molten interior, glassy inner shading.
                float pr = length(p);
                float coreD = pr - _CoreRadius;
                float seat = smoothstep(0.020, 0.0, abs(coreD)) ;
                float coreMask = 1.0 - smoothstep(0.0, aa * 2.0, coreD);
                float coreInner = saturate(1.0 - pr / max(_CoreRadius, 1e-4));
                float3 coreCol = lerp(_EmberColor.rgb * 0.25, hot * (0.6 + 1.4 * heat), pow(coreInner, 1.6));
                body = lerp(body, coreCol, coreMask);
                body *= (1.0 - seat * 0.55);                 // the porthole sits IN the casing

                // Jagged radial cracks splitting outward as the fuse runs; they reach further as _Fuse rises.
                float theta = atan2(p.y, p.x);
                float spokes = abs(frac(theta / 6.2831853 * 7.0 + (vnoise(float2(pr * 9.0, theta * 2.2)) - 0.5) * 0.35) - 0.5) * 2.0;
                float reach = _CoreRadius + 0.06 + 0.30 * saturate(_Fuse * 1.15);
                float inReach = smoothstep(reach, reach - 0.10, pr) * smoothstep(_CoreRadius - 0.02, _CoreRadius + 0.04, pr);
                float crack = (1.0 - smoothstep(0.0, 0.14, spokes)) * inReach;
                body = lerp(body, hot * (0.5 + 1.5 * heat), crack * saturate(0.25 + heat));

                // Outline: thick, near-black iron.
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                float lumT = dot(iron, float3(0.299, 0.587, 0.114));
                float3 outCol = lerp(iron, float3(lumT, lumT, lumT), 0.30) * 0.22;
                body = lerp(body, outCol * grad, tOut);

                return half4(body, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
