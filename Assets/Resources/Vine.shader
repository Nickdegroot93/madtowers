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
        _VineColor ("Stem Colour", Color) = (0.24, 0.30, 0.12, 1)
        _LeafColor ("Leaf Colour", Color) = (0.36, 0.66, 0.24, 1)
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

            // Signed distance (normalized) to an ellipse rotated by ang, taller than wide.
            float leafDist(float2 q, float ang, float2 sz)
            {
                float c = cos(ang), s = sin(ang);
                float2 r = float2(c * q.x - s * q.y, s * q.x + c * q.y);
                return length(r / sz) - 1.0;
            }

            // One shaded leaf: dark rim (reads as an outline over the brick), brighter centre, and an
            // extra light lobe offset toward the tip - matches the game's outlined/bevelled language.
            void addLeaf(float2 q, float2 c, float ang, float2 sz, float reveal, float growth, float idx,
                         inout float mask, inout float shade, inout float varr)
            {
                float d = leafDist(q - c, ang, sz);
                float m = smoothstep(0.06, -0.04, d) * step(reveal, growth);
                if (m <= mask) return;
                float inner = saturate(-d * 4.5);
                float hi = saturate(-leafDist(q - c - float2(0.012, 0.024), ang, sz * 0.62) * 4.0);
                mask = m;
                shade = 0.45 + 0.55 * inner + 0.38 * hi;
                varr = frac(idx * 0.618 + 0.13);
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

                // Main winding stem, tapering toward the tip; revealed up to _Growth. Drawn with a dark
                // outline ring (outCore) so it reads as part of the outlined art, not a decal.
                float sc = stemCenter(g, ph);
                float stemW = lerp(0.085, 0.030, g);
                float dstem = abs(lat2 - sc) - stemW;

                // A thinner offshoot for density.
                float sc2 = 0.20 * sin(g * 5.0 + ph + 2.5) + 0.12;
                float dstem2 = abs(lat2 - sc2) - 0.032;
                dstem = min(dstem, dstem2);

                float grow = step(g, _Growth);
                float stem = smoothstep(0.012, 0.0, dstem) * grow;         // stem body
                float stemOut = smoothstep(0.034, 0.016, dstem) * grow;    // body + dark rim

                // Leaves sprout along the stem, alternating sides; each pops in as growth passes it.
                // Larger, layered clusters than before - a big and a small lobe per node.
                float2 q = float2(lat2, g);
                float lm = 0.0, lsh = 1.0, lv = 0.0;
                addLeaf(q, float2(stemCenter(0.20, ph) - 0.085, 0.20), -0.75, float2(0.115, 0.190) * leafScale, 0.20, _Growth, 1.0, lm, lsh, lv);
                addLeaf(q, float2(stemCenter(0.20, ph) + 0.060, 0.16),  0.95, float2(0.070, 0.120) * leafScale, 0.20, _Growth, 2.0, lm, lsh, lv);
                addLeaf(q, float2(stemCenter(0.44, ph) + 0.080, 0.44),  0.80, float2(0.095, 0.160) * leafScale, 0.44, _Growth, 3.0, lm, lsh, lv);
                addLeaf(q, float2(stemCenter(0.62, ph) - 0.080, 0.62), -0.85, float2(0.105, 0.175) * leafScale, 0.62, _Growth, 4.0, lm, lsh, lv);
                addLeaf(q, float2(stemCenter(0.62, ph) + 0.055, 0.70),  0.70, float2(0.062, 0.105) * leafScale, 0.62, _Growth, 5.0, lm, lsh, lv);
                addLeaf(q, float2(stemCenter(0.86, ph) + 0.060, 0.86),  0.85, float2(0.080, 0.135) * leafScale, 0.86, _Growth, 6.0, lm, lsh, lv);
                addLeaf(q, float2(stemCenter(0.33, ph) + 0.075, 0.33),  0.88, float2(0.082, 0.140) * leafScale, 0.33, _Growth, 7.0, lm, lsh, lv);
                addLeaf(q, float2(stemCenter(0.75, ph) - 0.070, 0.76), -0.78, float2(0.092, 0.155) * leafScale, 0.75, _Growth, 8.0, lm, lsh, lv);

                // Stem: dark outline ring under a tube-shaded core (lighter on one side).
                float tube = 0.85 + 0.30 * saturate((sc - lat2) / max(stemW, 1e-3));
                float3 stemDark = _VineColor.rgb * 0.35;
                float3 col = lerp(stemDark, _VineColor.rgb * tube, stem);
                float a = stemOut;

                // Leaves over stems: per-leaf value variance, dark rims, bright tip lobes.
                float3 leafCol = _LeafColor.rgb * lerp(0.85, 1.18, lv) * lsh;
                col = lerp(col, leafCol, lm);
                a = max(a, lm);

                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
