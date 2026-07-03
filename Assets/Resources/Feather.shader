Shader "MadTowers/Feather"
{
    // Feather cell: a warm, downy PLUME PILLOW - the block is upholstered in overlapping feather
    // shingles (cosine-scalloped rows, light cream at the top sinking to warm shadow at the bottom,
    // each row casting a soft crescent shadow on the one below). A few tiny down-flecks drift slowly
    // upward inside the fill - the whole brick reads soft, warm and nearly weightless, the opposite of
    // Ice's cold glossy pane. Extra-round corners + a softer outline keep it pillowy while still
    // sitting in the outlined art style. _Seed staggers the scallops per cell. Motion (float + sway +
    // landing flutter) is FeatherBlockSkin's job. Theme-independent (the chapter art is hidden).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _DownLight ("Down Colour (top)", Color) = (0.97, 0.94, 0.87, 1)
        _DownDeep ("Down Colour (bottom)", Color) = (0.80, 0.71, 0.55, 1)
        _ShadowColor ("Scallop Shadow", Color) = (0.62, 0.54, 0.44, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.15
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.06
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.11
        _Rows ("Plume Rows", Float) = 4
        _Seed ("Per-cell Seed (driven)", Float) = 0
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
                float4 _DownLight;
                float4 _DownDeep;
                float4 _ShadowColor;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float _Rows;
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

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, float2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;
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

                float2 p = uv - 0.5;
                float halfBox = 0.5;
                float r = min(_CornerRadius, halfBox - 0.001);
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(p, bb, r);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                // Plume shingles: rows from the top down; each row's lower edge is a cosine scallop and
                // the row below darkens slightly, with a soft crescent shadow right under the edge.
                float rows = max(2.0, _Rows);
                float scAmp = 0.45 / rows;                     // scallop depth
                float freq = 3.0;                              // scallops per row
                float yTop = 1.0 - uv.y;                       // 0 at top edge
                float rowF = yTop * rows;
                float shade = 0.0;                             // accumulated shadow
                float rowIdx = 0.0;
                // Walk the two rows that can affect this pixel (its own and the one above).
                for (int k = 0; k < 2; k++)
                {
                    float row = floor(rowF) - float(k);
                    if (row < 0.0) continue;
                    float phase = row * 0.5 + ph + _Seed * 3.7;
                    float edgeY = (row + 1.0) / rows + scAmp * (0.5 - 0.5 * cos((uv.x * freq + phase) * 6.2831));
                    float below = yTop - edgeY;                // >0 = below this row's scalloped hem
                    if (k == 0) rowIdx = row;
                    // Soft crescent shadow cast on whatever is below the hem.
                    shade = max(shade, smoothstep(0.05, 0.0, abs(below - 0.012)) * step(0.0, below) * 0.68);
                    // The hem also owns the pixel just above it: track the deepest row covering us.
                    if (k == 0 && below > 0.0) rowIdx = row + 1.0;
                }
                float rowT = saturate(rowIdx / (rows - 0.5));
                float3 body = lerp(_DownLight.rgb, _DownDeep.rgb, rowT);
                body = lerp(body, _ShadowColor.rgb, shade);

                // Barb hint: whisper-fine vertical strands, denser toward each hem.
                float strand = (hash21(float2(floor(uv.x * 90.0), rowIdx)) - 0.5) * 0.05;
                body *= (1.0 + strand);

                // Soft pillow bevel: gentle all-round light falloff, extra top light, mild bottom shade.
                float grad = 1.08 - 0.22 * pow(saturate(1.0 - uv.y), 1.2);
                body *= grad;
                float e = 0.012;
                float dY = sdRoundBox(p + float2(0, e), bb, r) - sdRoundBox(p - float2(0, e), bb, r);
                float ny = dY / (2.0 * e);
                float band = pow(saturate((d + _OutlineWidth + _BevelWidth) / max(_BevelWidth, 0.001)), 1.6);
                band *= saturate((-d - _OutlineWidth * 0.55) / max(_OutlineWidth * 0.45, 0.001));
                float topness = saturate((ny - 0.25) / 0.5);
                float botness = saturate((-ny - 0.25) / 0.5);
                body *= (1.0 - 0.06 * band);
                body = lerp(body, float3(1.0, 0.99, 0.96) * grad, 0.45 * band * topness);
                body *= (1.0 - 0.16 * band * botness);

                // Tiny down-flecks drifting upward inside the pillow (scaled time via _Time is fine here -
                // it's ambient shimmer, not gameplay motion).
                float2 fuv = uv * 7.0 + float2(0.0, -_Time.y * 0.25) + ph;
                float fleck = step(0.972, hash21(floor(fuv)));
                float2 ff = frac(fuv) - 0.5;
                fleck *= smoothstep(0.30, 0.05, length(ff));
                body = lerp(body, float3(1.0, 1.0, 0.98), fleck * 0.35);

                // Outline: softer & warmer than stone bricks (still closed, still darkest thing on the brick).
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                float3 outCol = _DownDeep.rgb * 0.34;
                body = lerp(body, outCol * grad, tOut);

                return half4(body, mask * 0.985);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
