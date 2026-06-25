Shader "MadTowers/Maw"
{
    // The Maw: a fleshy dark-purple monster brick that DEVOURS anything placed on it. The body is shaded
    // with the same rounded-brick recipe as the other blocks (gradient, in-hue bevel, outline, grain) so it
    // sits next to them - just purple flesh. While falling it is dormant (_Active = 0, no tentacles). On
    // landing the skin ramps _Active 0->1 and thick, suckered TENTACLES grow out and writhe. They always
    // grow toward WORLD-up (_UpDir, fed per-frame by the skin) so however the piece is rotated they reach
    // up, never out the side. Each bite (_Chomp) makes them lash + flush. Quad is larger than the cell
    // (CellScale) for tentacle headroom; body inset to _BodyHalf = 0.5/CellScale so it still tiles.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _FleshColor ("Flesh", Color) = (0.21, 0.07, 0.27, 1)
        _TentacleColor ("Tentacle", Color) = (0.31, 0.11, 0.43, 1)
        _TentacleHi ("Tentacle Highlight", Color) = (0.60, 0.32, 0.72, 1)
        _Active ("Maw Active (0..1, driven)", Range(0,1)) = 0
        _Chomp ("Chomp / Lash (0..1, driven)", Range(0,1)) = 0
        _UpDir ("World-up in local UV (driven)", Vector) = (0,1,0,0)
        _Expose ("Tentacles Exposed (0/1, per-cell driven)", Range(0,1)) = 1
        _BodyHalf ("Body Half (= 0.5/CellScale)", Range(0.2, 0.5)) = 0.278
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.07
        _Seed ("Per-cell Seed", Float) = 0
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
                float4 _FleshColor;
                float4 _TentacleColor;
                float4 _TentacleHi;
                float _Active;
                float _Chomp;
                float4 _UpDir;
                float _Expose;
                float _BodyHalf;
                float _CornerRadius;
                float _Seed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p); float2 f = frac(p); f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0)), c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }
            float fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++) { s += a * vnoise(p); p *= 2.0; a *= 0.5; }
                return s;
            }
            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, float2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;
            }

            // A thick, suckered tentacle in (u = along world-up, v = across) space. Fat rooted base, tapering
            // tip, two-harmonic writhe. Returns coverage; lit = tube-shading side; suck = sucker highlight.
            float tentacle(float u, float v, float rootU, float baseV, float phase, float reach, float lash, float t,
                           out float lit, out float suck)
            {
                lit = 0.0; suck = 0.0;
                float uMax = 0.5;
                float span = uMax - rootU;
                if (u < rootU) return 0.0;
                float grown = rootU + saturate(reach) * span;
                if (u > grown) return 0.0;

                float tN = saturate((u - rootU) / span);
                float w = 0.10 * sin(u * 7.0 + t * 2.4 + phase) + 0.045 * sin(u * 13.0 + t * 3.6 + phase * 1.7);
                w *= (1.0 + lash * 1.3) * smoothstep(0.0, 0.35, tN);   // calm at the base, writhes toward the tip
                float dv = v - (baseV + w);

                float bulb = 1.0 + 1.1 * smoothstep(0.14, 0.0, tN);    // fat where it roots into the flesh
                float halfW = lerp(0.060, 0.010, tN) * bulb;
                float m = smoothstep(halfW, halfW * 0.55, abs(dv));
                m *= smoothstep(grown, grown - 0.04, u);               // round the tip

                lit = saturate(0.5 - dv / max(halfW, 1e-4));
                float sp = frac((u - rootU) * 9.0 + phase);
                suck = (1.0 - smoothstep(0.0, 0.16, abs(sp - 0.5)))
                     * (1.0 - smoothstep(0.0, halfW * 0.6, abs(dv + halfW * 0.3))) * m;
                return m;
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
                float ph = _Seed * 6.2831;
                float tm = _Time.y;
                float2 pc = uv - 0.5;

                // --- Body: rounded-brick form (gradient + in-hue bevel + outline + grain), fleshy purple ---
                float bh = _BodyHalf;
                float2 bb = float2(bh, bh);
                float r = min(_CornerRadius, bh - 0.001);
                float d = sdRoundBox(pc, bb, r);
                float aa = max(fwidth(d), 0.001);
                float bodyMask = 1.0 - smoothstep(0.0, aa, d);

                float grad = 0.82 + 0.34 * uv.y;
                float mot = fbm(uv * 5.0 + ph);
                float3 col = _FleshColor.rgb * grad * (0.85 + 0.30 * mot);
                float vein = 1.0 - smoothstep(0.0, 0.05, abs(frac(uv.x * 3.0 + mot * 2.0) - 0.5));
                col *= (1.0 - 0.16 * vein);

                float e = 0.012;
                float dY = sdRoundBox(pc + float2(0, e), bb, r) - sdRoundBox(pc - float2(0, e), bb, r);
                float ny = dY / (2.0 * e);
                float band = saturate((d + 0.24) / 0.14);
                if (ny > 0.4) col *= 1.0 + 0.22 * band;
                else if (ny < -0.4) col *= 1.0 - 0.18 * band;

                float tOut = saturate(1.0 + d / 0.10);
                col = lerp(col, _FleshColor.rgb * 0.30 * grad, tOut);
                col *= (0.96 + 0.04 * sin(tm * 2.2 + ph)); // breathe

                // --- Tentacles, always growing toward world-up ---
                float2 up = normalize(_UpDir.xy + float2(1e-4, 1e-4));
                float2 perp = float2(-up.y, up.x);
                float u = dot(pc, up);
                float v = dot(pc, perp);
                float rootU = bh - 0.02; // root right at the brick's top edge, not over its face

                float l1, l2, l3, s1, s2, s3;
                float t1 = tentacle(u, v, rootU,         -0.16, ph,        _Active, _Chomp, tm, l1, s1);
                float t2 = tentacle(u, v, rootU + 0.02,   0.00, ph + 2.1,  _Active, _Chomp, tm, l2, s2);
                float t3 = tentacle(u, v, rootU,          0.16, ph + 4.2,  _Active, _Chomp, tm, l3, s3);

                float tnt = max(t1, max(t2, t3)) * _Expose; // only the piece's exposed top cells sprout
                float lit = max(l1, max(l2, l3));
                float suck = max(s1, max(s2, s3));

                float3 tcol = lerp(_TentacleColor.rgb, _TentacleHi.rgb, lit * 0.65);
                tcol = lerp(tcol, _TentacleHi.rgb, suck * 0.8);
                tcol += _TentacleHi.rgb * _Chomp * 0.4;

                col = lerp(col, tcol, tnt);
                float alpha = max(bodyMask, tnt);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
