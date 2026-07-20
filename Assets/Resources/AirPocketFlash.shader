Shader "MadTowers/AirPocketFlash"
{
    // The detonation flare for an Airtight air pocket: renders through the SAME cell mask as
    // AirPocketSmoke, so the flash is cavity-shaped with a noise-eaten boundary - never the
    // bounding-box rectangle a plain sprite would give an L-shaped pocket. Colour and fade
    // come from the SpriteRenderer's vertex colour (AirPocketFx.Update drives the two-beat
    // white-hot -> ember decay), so this shader stays a dumb masked wash.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _MaskTex ("Pocket Cell Mask", 2D) = "white" {}
        _MaskOrigin ("Mask Origin (world xy of texel 0,0)", Vector) = (0, 0, 0, 0)
        _MaskInvSize ("Mask Inverse World Size", Vector) = (1, 1, 0, 0)
        _Seed ("Per-pocket Seed", Float) = 0
        _NoiseScale ("Noise Scale (per world unit)", Float) = 4.2
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
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _MaskOrigin;
                float4 _MaskInvSize;
                float _Seed;
                float _NoiseScale;
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
                float2 worldXY     : TEXCOORD0;
                float4 color       : COLOR;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(world);
                o.worldXY = world.xy;
                o.color = v.color;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345) + _Seed);
                p += dot(p, p + 34.23);
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

            half4 frag(Varyings i) : SV_Target
            {
                float2 maskUV = (i.worldXY - _MaskOrigin.xy) * _MaskInvSize.xy;
                float m = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, maskUV).r;

                // The bilinear mask crosses 0.5 at the cell boundary; noise eats the edge so
                // the flare's rim is torn, drifting slightly over the flash's short life.
                float n = vnoise(i.worldXY * _NoiseScale + _Time.y * 0.8);
                float presence = smoothstep(0.32, 0.62, m + (n - 0.5) * 0.35);
                if (presence <= 0.003) discard;

                // A hotter core deeper inside the cavity, so the flash reads as a burst from
                // within the smoke rather than a flat wash.
                float core = 0.75 + 0.25 * smoothstep(0.55, 0.95, m);
                return half4(i.color.rgb * core, i.color.a * presence);
            }
            ENDHLSL
        }
    }
}
