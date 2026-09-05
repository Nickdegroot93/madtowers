Shader "MadTowers/Tremor"
{
    // Tremor cell: a fixed, theme-independent slab of warm ochre fault-stone (same rounded-brick
    // silhouette + fbm lumps as Boulder, so it tiles next to the normal bricks) - but where Boulder is
    // dead-still grey basalt, Tremor is RESTLESS earth under stress: a network of fault cracks that glow
    // amber from within, with a pulse of light travelling along them (_Wave) so the brick reads as
    // charged seismic energy even at rest. On landing the behaviour drives _Quake 0->1: the cracks flash
    // and a shockwave ring rips outward across the face, marrying the look to the tower jolt it triggers.
    // Theme-independent: the chapter art is hidden, only the quad alpha is used (ART.md s13).
    Properties
    {
        _HazardSurface ("Baked stone relief", 2D) = "gray" {}
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _RockColor ("Rock Colour", Color) = (0.50, 0.38, 0.21, 1)
        _GlowColor ("Fault Glow Colour", Color) = (1.0, 0.55, 0.16, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.35)) = 0.14
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.066
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.102
        _RockScale ("Rock Lump Scale", Float) = 4.0
        _CrackScale ("Fault Scale", Float) = 5.0
        _CrackWidth ("Fault Width", Range(0.01, 0.15)) = 0.06
        _IdleEmber ("Idle Ember (glow at rest)", Range(0, 1)) = 0.08
        _Wave ("Travelling Pulse (0..1, driven)", Range(0, 1)) = 0
        _Quake ("Quake Discharge (0..1, driven)", Range(0, 1)) = 0
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _RockColor;
                float4 _GlowColor;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float _RockScale;
                float _CrackScale;
                float _CrackWidth;
                float _IdleEmber;
                float _Wave;
                float _Quake;
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

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                float2 p = uv - 0.5;
                float halfBox = 0.5;
                float r = 22.0/256;
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(p, bb, r);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                half4 surface=HazardSurface(uv);
                float3 rock=HazardStone(uv,_RockColor.rgb,d,22.0/256,17.0/256,26.0/256,surface);
                // Broad fractured ochre plates, with short hot faults seated below the face.
                rock*=.87+.24*smoothstep(.35,.66,surface.g);
                float fault=surface.a;
                float halo=max(fault,HazardSurface(uv+float2(0,2.0/256)).a)*.7;
                rock*=1-.35*fault;

                // Travelling pulse: a bright band sweeps diagonally across the brick, lighting the part of
                // the fault network it passes over (energy looking for a way out).
                float axis = saturate((uv.x + uv.y) * 0.5);            // 0..1 corner-to-corner
                float waveOffset = (axis - _Wave) * 6.0;
                float waveBand = exp(-waveOffset * waveOffset); // defined on both sides of the travelling pulse
                float glow = _IdleEmber + waveBand * 0.30 + _Quake * 1.6; // quake flashes the whole network
                rock += _GlowColor.rgb * (fault * glow + halo * glow * 0.30);

                // Quake shockwave: a ring expands from the centre to the rim as _Quake goes 0->1, fading as
                // it grows. Reads as the seismic discharge on landing.
                if (_Quake > 0.0)
                {
                    float dist = length(p) * 2.0;                       // 0 centre .. ~1 edge
                    float ringR = _Quake;
                    float ring = 1.0 - smoothstep(0.0, 0.16, abs(dist - ringR));
                    rock += _GlowColor.rgb * ring * (1.0 - _Quake) * 1.4 * mask;
                }

                rock=HazardOutline(rock,_RockColor.rgb,d,17.0/256);
                return half4(rock, mask)*IN.color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
