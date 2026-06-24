Shader "MadTowers/Vine"
{
    // A vine OVERLAY: stems + leaves drawn ON TOP of the kept chapter art (unlike Anchor/Boulder which
    // replace it), so a Vine brick is "a normal chapter-coloured block with vines growing over it".
    // Fixed green (theme-independent) so it always reads as vine; sparse, so the block's real colour
    // shows between the stems. _Growth (0..1) reveals the vine from its root edge outward (it grows in,
    // and - phase 2 - creeps onto a welded neighbour from the contact side, set via _RootDir). _Seed
    // varies each cell so a 1x4 isn't four identical vines; a gentle sway makes it feel alive.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _VineColor ("Stem Colour", Color) = (0.26, 0.28, 0.13, 1)
        _LeafColor ("Leaf Colour", Color) = (0.32, 0.60, 0.22, 1)
        _Growth ("Growth (0..1)", Range(0, 1)) = 1
        _Seed ("Per-cell Seed", Float) = 0
        _Sway ("Sway Amount", Range(0, 0.1)) = 0.02
        _RootDir ("Root Direction (xy)", Vector) = (0, 1, 0, 0)
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
                float4 _VineColor;
                float4 _LeafColor;
                float _Growth;
                float _Seed;
                float _Sway;
                float4 _RootDir;
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

            float stemCenter(float g, float ph)
            {
                return 0.20 * sin(g * 5.0 + ph);
            }

            // Leaf blob in (lat, along) space: an ellipse rotated by ang, taller than wide.
            float leafMask(float2 q, float ang, float2 sz)
            {
                float c = cos(ang), s = sin(ang);
                float2 r = float2(c * q.x - s * q.y, s * q.x + c * q.y);
                float2 e = r / sz;
                float d = length(e) - 1.0;
                return smoothstep(0.10, 0.0, d);
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
                float leafScale = 0.85 + 0.30 * frac(_Seed * 3.1 + 0.2); // slight per-cell leaf-size variance

                // Vine coordinate frame: g runs 0 (root edge) -> 1 (far edge) along _RootDir,
                // lat is the perpendicular offset across the stem.
                float2 dir = normalize(_RootDir.xy + float2(1e-4, 1e-4));
                float2 perp = float2(-dir.y, dir.x);
                float2 rel = uv - 0.5;
                float g = saturate(dot(rel, dir) + 0.5);
                float lat = dot(rel, perp);

                // Living sway - free ends (high g) move more than the rooted base.
                float sway = _Sway * sin(_Time.y * 1.6 + ph) * g;
                float lat2 = lat - sway;

                // Main winding stem, tapering toward the tip; revealed up to _Growth.
                float sc = stemCenter(g, ph);
                float stemW = lerp(0.075, 0.024, g);
                float stem = smoothstep(stemW, stemW * 0.4, abs(lat2 - sc)) * step(g, _Growth);

                // A thinner offshoot for density.
                float sc2 = 0.20 * sin(g * 5.0 + ph + 2.5) + 0.12;
                float stem2 = smoothstep(0.042, 0.014, abs(lat2 - sc2)) * step(g, _Growth) * 0.7;
                stem = max(stem, stem2);

                // Leaves sprout along the stem, alternating sides; each pops in as growth passes it.
                float2 q = float2(lat2, g);
                float leaves = 0.0;
                leaves = max(leaves, leafMask(q - float2(stemCenter(0.22, ph) - 0.07, 0.22), -0.8, float2(0.092, 0.150) * leafScale) * step(0.22, _Growth));
                leaves = max(leaves, leafMask(q - float2(stemCenter(0.44, ph) + 0.07, 0.44),  0.8, float2(0.066, 0.112) * leafScale) * step(0.44, _Growth));
                leaves = max(leaves, leafMask(q - float2(stemCenter(0.64, ph) - 0.07, 0.64), -0.8, float2(0.088, 0.146) * leafScale) * step(0.64, _Growth));
                leaves = max(leaves, leafMask(q - float2(stemCenter(0.84, ph) + 0.06, 0.84),  0.8, float2(0.058, 0.100) * leafScale) * step(0.84, _Growth));

                // Shade: stem as a little tube (lighter on one side); leaves a brighter green on top.
                float tube = 0.85 + 0.30 * saturate((sc - lat2) / max(stemW, 1e-3));
                float3 col = _VineColor.rgb * tube;
                float a = stem;

                col = lerp(col, _LeafColor.rgb, leaves);
                a = max(a, leaves);

                return half4(col, saturate(a) * 0.95);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
