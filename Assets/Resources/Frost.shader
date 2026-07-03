Shader "MadTowers/Frost"
{
    // Per-cell ice pane used by the Freeze ability. The renderer colour carries the sampled block
    // colour for that cell; the shader desaturates it into translucent ice, then adds a rounded glass
    // bevel, cloudy mottle, thin scratches and branching frost cracks. _Freeze reveals the panes with
    // an irregular bottom-up crawl driven by FrostFx.
    Properties
    {
        [PerRendererData] _MainTex ("Cell Sprite", 2D) = "white" {}
        _Freeze ("Freeze Amount", Range(0,1)) = 0
        _Seed ("Per-Cell Seed", Float) = 0
        _Pattern ("Crack Pattern", Range(0,4)) = 0
        _Turn ("Pattern Quarter Turn", Range(0,3)) = 0
        _DetailStrength ("Detail Strength", Range(0,1)) = 1

        _IceColor ("Ice Body", Color) = (0.47, 0.74, 0.96, 1)
        _FrostColor ("Frost Lines", Color) = (0.96, 0.99, 1, 1)
        _ShadowColor ("Cold Shadow", Color) = (0.2, 0.32, 0.54, 1)
        _BodyOpacity ("Body Opacity", Range(0,1)) = 0.9
        _ColorPreserve ("Source Colour", Range(0,1)) = 0.42

        _PaneInset ("Pane Inset", Range(0, 0.16)) = 0.035
        _CornerRadius ("Corner Radius", Range(0, 0.2)) = 0.075
        _BevelWidth ("Bevel Width", Range(0.01, 0.3)) = 0.15
        _RimStrength ("Rim Brightness", Range(0, 2)) = 1.15

        _CrackWidth ("Crack Width", Range(0.001, 0.05)) = 0.01
        _CrackStrength ("Crack Brightness", Range(0, 2)) = 0.9
        _ScratchStrength ("Scratch Brightness", Range(0, 1)) = 0.16
        _MottleStrength ("Cloudiness", Range(0,1)) = 0.26
        _FrontStrength ("Crawl Front Brightness", Range(0, 2)) = 0.9
        _EdgeWidth ("Crawl Front Width", Range(0.01, 0.4)) = 0.12
        _NoiseScale ("Crawl Noise Scale", Float) = 5
        _UpwardBias ("Freeze-from-bottom Bias", Range(0,1)) = 0.35
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
                float _Freeze;
                float _Seed;
                float _Pattern;
                float _Turn;
                float _DetailStrength;
                float4 _IceColor;
                float4 _FrostColor;
                float4 _ShadowColor;
                float _BodyOpacity;
                float _ColorPreserve;
                float _PaneInset;
                float _CornerRadius;
                float _BevelWidth;
                float _RimStrength;
                float _CrackWidth;
                float _CrackStrength;
                float _ScratchStrength;
                float _MottleStrength;
                float _FrontStrength;
                float _EdgeWidth;
                float _NoiseScale;
                float _UpwardBias;
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
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float v = 0.0;
                float amp = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += amp * vnoise(p);
                    p *= 2.03;
                    amp *= 0.5;
                }
                return v;
            }

            float2 seedOffset(float id)
            {
                return float2(
                    hash21(float2(_Seed + id * 1.37, id * 4.11)),
                    hash21(float2(id * 7.31, _Seed - id * 2.09))) - 0.5;
            }

            float2 quarterTurn(float2 uv, float turn)
            {
                float t = floor(turn + 0.5);
                if (t < 0.5) return uv;
                if (t < 1.5) return float2(uv.y, 1.0 - uv.x);
                if (t < 2.5) return 1.0 - uv;
                return float2(1.0 - uv.y, uv.x);
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, float2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;
            }

            float segmentDistance(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 0.0001));
                return length(pa - ba * h);
            }

            float crackSegment(float2 p, float2 a, float2 b, float width)
            {
                float d = segmentDistance(p, a, b);
                float core = 1.0 - smoothstep(width, width * 2.4, d);
                float bloom = 1.0 - smoothstep(width * 1.8, width * 6.0, d);
                return saturate(core + bloom * 0.22);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 sprite = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float2 uv = IN.uv;

                float paneHalf = max(0.05, 0.5 - _PaneInset);
                float radius = min(_CornerRadius, paneHalf - 0.001);
                float d = sdRoundBox(uv - 0.5, float2(paneHalf, paneHalf), radius);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                // Irregular crawl front: compare a noise field to a rising threshold. The bottom of
                // the block (low uv.y) gets a head start so the ice climbs upward.
                float n = fbm(uv * _NoiseScale + float2(_Seed, _Seed * 1.37));
                float m = n - _UpwardBias * (1.0 - IN.uv.y);
                float threshold = lerp(-_UpwardBias - _EdgeWidth * 2.0, 1.0 + _EdgeWidth, _Freeze);
                float coverage = 1.0 - smoothstep(threshold - _EdgeWidth, threshold + _EdgeWidth, m);
                float front = 1.0 - smoothstep(0.0, _EdgeWidth, abs(m - threshold));

                float3 tint = saturate(IN.color.rgb);
                float luminance = dot(tint, float3(0.299, 0.587, 0.114));
                float3 keptColour = lerp(luminance.xxx, tint, _ColorPreserve);
                float3 body = lerp(_IceColor.rgb, keptColour, _ColorPreserve);

                float cloud = fbm(uv * 4.6 + float2(_Seed * 0.19, _Seed * 0.31));
                float cloudy = saturate((cloud - 0.24) * 1.45) * _MottleStrength;
                body = lerp(body, _FrostColor.rgb, cloudy * 0.35);
                body = lerp(body, _ShadowColor.rgb, (1.0 - cloud) * 0.08);

                float edgeDist = saturate((-d) / max(_BevelWidth, 0.001));
                float rim = (1.0 - edgeDist) * mask;
                float topRim = rim * saturate(uv.y * 1.25 + (1.0 - uv.x) * 0.28);
                float bottomRim = rim * saturate((1.0 - uv.y) * 1.1 + uv.x * 0.22);
                body = lerp(body, _FrostColor.rgb, saturate(topRim * _RimStrength * 0.52));
                body = lerp(body, _ShadowColor.rgb, bottomRim * 0.2);

                float sheen = smoothstep(0.66, 0.78, uv.y) * (1.0 - smoothstep(0.82, 0.96, uv.y));
                sheen *= smoothstep(0.08, 0.22, uv.x) * (1.0 - smoothstep(0.78, 0.94, uv.x));
                body = lerp(body, _FrostColor.rgb, sheen * 0.22);

                float2 crackUv = quarterTurn(uv, _Turn);
                float jitter = 0.045;
                float pattern = floor(_Pattern + 0.5);
                float crack = 0.0;

                if (pattern < 0.5)
                {
                    // Nearly clean pane: a couple of tiny edge nicks, no central symbol.
                    float2 a0 = float2(0.02, 0.70) + seedOffset(1.0) * jitter;
                    float2 a1 = float2(0.18, 0.63) + seedOffset(2.0) * jitter;
                    float2 b0 = float2(0.83, 0.98) + seedOffset(3.0) * jitter;
                    float2 b1 = float2(0.73, 0.82) + seedOffset(4.0) * jitter;
                    crack = max(crack, crackSegment(crackUv, a0, a1, _CrackWidth * 0.42));
                    crack = max(crack, crackSegment(crackUv, b0, b1, _CrackWidth * 0.36));
                }
                else if (pattern < 1.5)
                {
                    // Single fracture entering from one edge and dying before the exact centre.
                    float2 a0 = float2(0.01, 0.58) + seedOffset(8.0) * jitter;
                    float2 a1 = float2(0.23, 0.54) + seedOffset(9.0) * jitter;
                    float2 a2 = float2(0.42, 0.47) + seedOffset(10.0) * jitter;
                    float2 b1 = float2(0.24, 0.72) + seedOffset(11.0) * jitter;
                    crack = max(crack, crackSegment(crackUv, a0, a1, _CrackWidth * 0.82));
                    crack = max(crack, crackSegment(crackUv, a1, a2, _CrackWidth * 0.62));
                    crack = max(crack, crackSegment(crackUv, a1, b1, _CrackWidth * 0.42));
                }
                else if (pattern < 2.5)
                {
                    // Corner fracture: strongest at the corner, with short branches nearby.
                    float2 a0 = float2(0.98, 0.96) + seedOffset(15.0) * jitter;
                    float2 a1 = float2(0.78, 0.78) + seedOffset(16.0) * jitter;
                    float2 a2 = float2(0.63, 0.60) + seedOffset(17.0) * jitter;
                    float2 b1 = float2(0.91, 0.70) + seedOffset(18.0) * jitter;
                    float2 c1 = float2(0.66, 0.84) + seedOffset(19.0) * jitter;
                    crack = max(crack, crackSegment(crackUv, a0, a1, _CrackWidth * 0.9));
                    crack = max(crack, crackSegment(crackUv, a1, a2, _CrackWidth * 0.58));
                    crack = max(crack, crackSegment(crackUv, a1, b1, _CrackWidth * 0.46));
                    crack = max(crack, crackSegment(crackUv, a1, c1, _CrackWidth * 0.42));
                }
                else if (pattern < 3.5)
                {
                    // Exterior stress marks: all near the rim, leaving the pane centre quiet.
                    float2 a0 = float2(0.00, 0.28) + seedOffset(21.0) * jitter;
                    float2 a1 = float2(0.18, 0.36) + seedOffset(22.0) * jitter;
                    float2 b0 = float2(0.33, 0.02) + seedOffset(23.0) * jitter;
                    float2 b1 = float2(0.42, 0.20) + seedOffset(24.0) * jitter;
                    float2 c0 = float2(1.00, 0.66) + seedOffset(25.0) * jitter;
                    float2 c1 = float2(0.84, 0.61) + seedOffset(26.0) * jitter;
                    crack = max(crack, crackSegment(crackUv, a0, a1, _CrackWidth * 0.5));
                    crack = max(crack, crackSegment(crackUv, b0, b1, _CrackWidth * 0.42));
                    crack = max(crack, crackSegment(crackUv, c0, c1, _CrackWidth * 0.38));
                }
                else
                {
                    // A longer hairline from an edge, offset from centre, with one tiny fork.
                    float2 a0 = float2(0.56, 0.99) + seedOffset(27.0) * jitter;
                    float2 a1 = float2(0.53, 0.76) + seedOffset(28.0) * jitter;
                    float2 a2 = float2(0.46, 0.58) + seedOffset(29.0) * jitter;
                    float2 b1 = float2(0.65, 0.69) + seedOffset(30.0) * jitter;
                    crack = max(crack, crackSegment(crackUv, a0, a1, _CrackWidth * 0.72));
                    crack = max(crack, crackSegment(crackUv, a1, a2, _CrackWidth * 0.5));
                    crack = max(crack, crackSegment(crackUv, a1, b1, _CrackWidth * 0.34));
                }

                float scratchNoise = fbm(crackUv * 13.0 + float2(_Seed * 0.73, _Seed * 0.41));
                float scratchLines = abs(frac((crackUv.x * 1.85 + crackUv.y * 2.45 + _Seed * 0.071) * 3.1) - 0.5);
                float scratches = (1.0 - smoothstep(0.012, 0.055, scratchLines)) *
                    smoothstep(0.54, 0.86, scratchNoise) * _ScratchStrength;

                float detail = saturate(_DetailStrength);
                float frostLines = saturate((crack * _CrackStrength + scratches) * detail * mask);
                body = lerp(body, _FrostColor.rgb, frostLines);
                body = lerp(body, _FrostColor.rgb, saturate(front * _FrontStrength) * mask);

                float alpha = sprite.a * IN.color.a * mask *
                    saturate(coverage * (_BodyOpacity + rim * 0.12 + frostLines * 0.08) + front * 0.35);
                return half4(body, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
