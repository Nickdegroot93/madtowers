Shader "MadTowers/Ice"
{
    // Hazard-only material. The Freeze ability's Frost shader remains independent.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _HazardSurface ("Frozen relief", 2D) = "gray" {}
        _IceColor ("Glacial body", Color) = (.39,.66,.73,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
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
            CBUFFER_START(UnityPerMaterial)
                float4 _IceColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes i) { Varyings o;o.positionHCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color*unity_SpriteColor;return o; }
            half4 frag(Varyings i):SV_Target
            {
                float2 uv=i.uv;
                float d=HazardRoundBox(uv-.5,.5,22.0/256);
                half4 surface=HazardSurface(uv);
                // Cloudy core and deep glacial planes, with fixed air inclusions under the face.
                half3 tint=_IceColor.rgb*lerp(.83,1.12,smoothstep(.32,.70,surface.g));
                half3 col=HazardStone(uv,tint,d,22.0/256,17.0/256,26.0/256,surface);
                float cloud=smoothstep(.53,.69,HazardSurface(uv*.7+.17).g);
                col=lerp(col,half3(.73,.85,.87),cloud*.30);
                float seam=surface.a;
                float under=HazardSurface(uv+float2(0,2.0/256)).a;
                col=lerp(col,half3(.12,.33,.42),under*.64);
                col=lerp(col,half3(.87,.96,.96),seam*.86);
                float facet=smoothstep(.37,.40,uv.x*.6+uv.y)* (1-smoothstep(.64,.67,uv.x*.6+uv.y));
                col+=half3(.075,.095,.10)*facet*(1-smoothstep(-.16,-.08,d));
                float bubbles=smoothstep(.82,.9,surface.b)*(1-smoothstep(-.18,-.10,d));
                col=lerp(col,half3(.84,.94,.95),bubbles*.37);
                col=HazardOutline(col,_IceColor.rgb,d,17.0/256);
                float aa = max(fwidth(d), .001);
                return half4(col,1-smoothstep(0,aa,d))*i.color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
