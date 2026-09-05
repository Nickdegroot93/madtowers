Shader "MadTowers/Sandstone"
{
    // Sandstone cell: warm layered sediment stone with a LOAD-BEARING read-out. _Damage (the
    // ratcheted worst load, 0..1) grows a crack network CONTINUOUSLY - veins appear, deepen
    // and widen as weight is added, never heal - and _Load (current pressure, 0..1) makes
    // fine sand trickle from the open cracks. Together with the skin's >85% shiver the brick
    // reads unambiguously: "one more thing on top and it bursts". Framed like the other
    // bricks (rounded cell, bevel, near-black outline, grain) so it tiles next to them.
    // Theme-independent by design (the chapter art is hidden).
    Properties
    {
        _HazardSurface ("Baked stone relief", 2D) = "gray" {}
        _MagmaCracks ("Baked plate boundaries", 2D) = "white" {}
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Seed ("Per-cell Seed", Float) = 0
        _Damage ("Damage (ratcheted load)", Range(0, 1)) = 0
        _Load ("Current Load", Range(0, 1)) = 0
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.086
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.066
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.102
        _SandLight ("Sand Light", Color) = (0.87, 0.74, 0.5, 1)
        _SandDark ("Sand Dark", Color) = (0.7, 0.56, 0.36, 1)
        _CrackColor ("Crack Colour", Color) = (0.32, 0.22, 0.13, 1)
        _OutlineColor ("Outline Colour", Color) = (0.16, 0.11, 0.07, 1)
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
            #include "HazardSurface.hlsl"
            TEXTURE2D(_MagmaCracks); SAMPLER(sampler_MagmaCracks);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Seed;
                float _Damage;
                float _Load;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float4 _SandLight;
                float4 _SandDark;
                float4 _CrackColor;
                float4 _OutlineColor;
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

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color * unity_SpriteColor;
                return o;
            }

            float sampleGrain(float2 p) { return HazardSurface(p/64.0).b; }
            float2 crackNet(float2 uv)
            {
                return float2(SAMPLE_TEXTURE2D(_MagmaCracks,sampler_MagmaCracks,uv).r*.38,HazardSurface(uv).g);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv - 0.5;

                // Rounded-cell frame (the shared brick silhouette).
                float2 q = abs(p) - (0.5 - _CornerRadius);
                float cell = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - _CornerRadius + 0.5;
                float d = cell - 0.5; // <0 inside
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d); // house AA edge (Boulder pattern)
                if (d > aa) discard;

                half4 surface=HazardSurface(i.uv);
                float strata=HazardSurface(float2(i.uv.x*.6,i.uv.y*2.8)).g;
                half3 tint=lerp(_SandDark.rgb,_SandLight.rgb,.54);
                half3 col=HazardStone(i.uv,tint,d,22.0/256,17.0/256,26.0/256,surface);
                col*=.92+.16*strata;

                // THE CRACKS: plates fracture one by one as damage grows (reveal order per
                // plate), and every open crack WIDENS with damage - continuous, never healing.
                float2 net = crackNet(i.uv);
                float revealed = step(net.y, _Damage * 1.25);
                float width = 0.02 + 0.09 * _Damage;
                float open = (1.0 - smoothstep(width * 0.4, width, net.x)) * revealed;
                float shade = (1.0 - smoothstep(width, width * 2.5, net.x)) * revealed;
                col = lerp(col, _CrackColor.rgb * 1.3, shade * 0.3 * (0.4 + 0.6 * _Damage));
                col = lerp(col, _CrackColor.rgb, open * (0.65 + 0.35 * _Damage));

                // Fine sand TRICKLE inside the open cracks while under load: grain streaks
                // sliding down the crack columns.
                float trickleMask = open * _Load;
                float streak = step(0.86, sampleGrain(floor(float2(i.uv.x * 30.0, 0.0))))
                             * frac(i.uv.y * 3.0 + _Time.y * 1.6);
                col = lerp(col, _SandDark.rgb * 0.8, trickleMask * streak * 0.5);

                // Edge chipping at high damage: corners and rim erode darker.
                float rim = smoothstep(-0.10, 0.0, d);
                col = lerp(col, _CrackColor.rgb * 0.9,
                           rim * _Damage * _Damage * (0.4 + 0.4 * sampleGrain(floor(i.uv * 14.0))));

                // Near-black outline (house framing).
                col = HazardOutline(col,tint,d,17.0/256);

                return half4(col, mask)*i.color;
            }
            ENDHLSL
        }
    }
}
