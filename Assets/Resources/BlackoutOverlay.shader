Shader "MadTowers/BlackoutOverlay"
{
    // The Blackout game state's look: a near-TOTAL blackout with one world-space light
    // hole - the LANTERN riding the falling piece. Deliberately no other light source
    // (a tower-peak glow was tried and rejected: it lit the very thing the mode is
    // supposed to hide - you memorize the tower during the slow power-down instead).
    // Evaluated in world coordinates (the quad is just a camera-covering window), so the
    // hole tracks the piece exactly however the camera pans/zooms. _Fade drives the
    // power-down/relight ramp; BlackoutOverlay.cs owns all the per-frame values.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Darkness ("Darkness", Range(0, 1)) = 1
        _Fade ("Fade In/Out", Range(0, 1)) = 0
        _LanternPos ("Lantern (world xy)", Vector) = (0, 0, 0, 0)
        _LanternRadius ("Lantern Radius", Float) = 7
        _NightColor ("Night Colour", Color) = (0.004, 0.004, 0.01, 1)
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

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Darkness;
                float _Fade;
                float4 _LanternPos;
                float _LanternRadius;
                float4 _NightColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 worldXY     : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(world);
                o.worldXY = world.xy;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Lantern with a FEATHERED edge: the falloff band runs from well inside the
                // radius to well past it, and the light eases QUADRATICALLY into the dark
                // (1 - s*s) so the approach to full black has no perceptible rim - a plain
                // smoothstep read as a hard circle because display gamma compresses the last
                // few percent of alpha into a visible ring.
                float dLantern = distance(i.worldXY, _LanternPos.xy);
                float s = 1.0 - smoothstep(_LanternRadius * 0.45, _LanternRadius * 1.35, dLantern);
                float a = _Darkness * _Fade * (1.0 - s * s);
                return half4(_NightColor.rgb, a);
            }
            ENDHLSL
        }
    }
}
