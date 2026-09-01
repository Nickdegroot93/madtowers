Shader "MadTowers/PlacementBeam"
{
    // The landing-preview beam under the falling piece, as a Photoshop layer mode rather
    // than a milky alpha sheet. Two modes, picked per chapter (ChapterDefinition):
    //   Screen   (_Mode 0, Blend OneMinusDstColor One): out = dst + src - dst*src. LIFTS a
    //            dark backdrop toward the tint; the default for night/dusk skies.
    //   Multiply (_Mode 1, Blend DstColor Zero):        out = dst * lerp(1, tint, k). DARKENS a
    //            bright backdrop toward the tint - Screen is invisible on snow/daylight.
    // Tint + strength come from the renderer colour (unity_SpriteColor); the sprite's alpha is
    // the per-texel intensity (full core, feathered edges, strongest at the landing end).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Mode ("Mode (0 screen, 1 multiply)", Float) = 0
        _SrcBlend ("Src Blend", Float) = 4   // UnityEngine.Rendering.BlendMode.OneMinusDstColor
        _DstBlend ("Dst Blend", Float) = 1   // UnityEngine.Rendering.BlendMode.One
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
        Blend [_SrcBlend] [_DstBlend]
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
                float _Mode;
                float _SrcBlend;
                float _DstBlend;
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
                o.positionHCS = TransformWorldToHClip(TransformObjectToWorld(v.positionOS.xyz));
                o.uv = v.uv;
                // Unity 6 URP delivers SpriteRenderer.color as the per-draw unity_SpriteColor
                // (the vertex colour stays white) - multiply both, exactly as URP's own
                // Sprite-Unlit-Default does, or the renderer tint/alpha is silently ignored.
                o.color = v.color * unity_SpriteColor;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half k = tex.a * i.color.a;   // per-texel intensity x renderer strength
                if (_Mode > 0.5)
                {
                    // Multiply: the framebuffer is scaled by this factor - 1 leaves it alone,
                    // the tint fully darkens it toward the tint's hue.
                    return half4(lerp(half3(1, 1, 1), i.color.rgb, k), 1);
                }
                // Screen consumes RGB only: fold intensity into the colour, emit no alpha so
                // nothing double-counts.
                return half4(tex.rgb * i.color.rgb * k, 0);
            }
            ENDHLSL
        }
    }
}
