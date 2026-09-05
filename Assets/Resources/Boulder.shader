Shader "MadTowers/Boulder"
{
    // Boulder cell: a chunk of FACETED GRANITE that reads heavy at a glance. The surface is posterized
    // noise - big flat rock facets at distinct brightness steps - with dark crevice contours running
    // between the facets and a lit lower lip on each crevice (the same carved-emboss language as the
    // normal bricks' cracks). Mica flecks glint in the grain. Worn round corners, a heavier/darker base
    // gradient and NO idle motion: dead-still + matte = mass. The personality is the landing slam
    // (BoulderBlockSkin). Theme-independent (the chapter art is hidden).
    Properties
    {
        _HazardSurface ("Baked stone relief", 2D) = "gray" {}
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _RockColor ("Rock Colour", Color) = (0.40, 0.38, 0.35, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.35)) = 0.16
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.066
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.102
        _FacetScale ("Facet Scale", Float) = 2.6
        _FacetSteps ("Facet Steps", Float) = 5
        _Speckle ("Speckle Strength", Range(0, 1)) = 0.55
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
            // HLSLcc produces flickering edge marks on Adreno 830; use DXC.
            #pragma use_dxc vulkan
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define MADTOWERS_HAZARD_FLOAT
            #define MADTOWERS_HAZARD_BRANCH_BEVEL
            #include "HazardSurface.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _RockColor;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float _FacetScale;
                float _FacetSteps;
                float _Speckle;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv          : TEXCOORD0;
            };

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
                OUT.color = IN.color * unity_SpriteColor;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                float2 p = uv - 0.5;
                float halfBox = 0.5;
                float r = 22.0/256;
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(p, bb, r);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                float4 surface=HazardSurface(uv);
                // Broad quarried planes, rather than small nested pebble contours.
                float planeA=smoothstep(.48,.51,uv.y*.65+uv.x*.55+(surface.g-.5)*.22);
                float planeB=smoothstep(.28,.31,uv.y-.5*uv.x+(surface.g-.5)*.12);
                surface.r*=.81+.23*planeA+.10*planeB;
                float3 rock=HazardStone(uv,_RockColor.rgb,d,22.0/256,17.0/256,26.0/256,surface);
                float mica=smoothstep(.79,.88,surface.b)*(1-smoothstep(-.20,-.10,d));
                rock+=float3(.065,.060,.045)*mica;
                rock=HazardOutline(rock,_RockColor.rgb,d,17.0/256);

                return float4(rock, mask)*IN.color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
