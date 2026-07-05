Shader "MadTowers/HeatHaze"
{
    // Full-screen hot-air shimmer: thin horizontal ripple bands rising and wobbling over the
    // backdrop, like the air above sun-baked ground. Uses 2x-multiply blending (0.5 = neutral)
    // so the bands both brighten AND darken what's behind them - that signed flicker is what
    // makes it read as refracting air rather than fog or smoke. No screen grab needed, so it
    // works on the URP 2D renderer without touching renderer-data settings.
    // _Strength is driven every frame by LevelPresentationController.Ambience (preset amount
    // x gust envelope x ground fade); the renderer is disabled outright when it reaches zero.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Strength ("Strength", Range(0, 1)) = 0
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
        Blend DstColor SrcColor  // 2x multiply: output 0.5 leaves the screen untouched
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
                float _Strength;
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

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float t = _Time.y;

                // Two noise fields, both stretched flat (high y frequency = thin horizontal
                // bands) and scrolling upward at different speeds; their interference keeps
                // the ripple pattern from ever visibly looping. A slow sine wobble on x makes
                // the bands snake sideways as they rise.
                float wobble = 0.35 * sin(uv.y * 9.0 + t * 0.9);
                float n1 = vnoise(float2(uv.x * 3.5 + wobble, uv.y * 22.0 - t * 2.2)) - 0.5;
                float n2 = vnoise(float2(uv.x * 6.0 - wobble, uv.y * 34.0 - t * 3.5)) - 0.5;

                // Stronger toward the bottom of the view: heat rises off the ground.
                float groundBias = lerp(1.0, 0.45, uv.y);

                float ripple = (n1 * 0.7 + n2 * 0.5) * _Strength * 0.22 * groundBias;
                return half4(0.5 + ripple.xxx, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
