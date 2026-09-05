Shader "MadTowers/Lava"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _HazardSurface ("Stone relief", 2D) = "gray" {}
        _MagmaCracks ("Cooling plate boundaries", 2D) = "white" {}
        _CrustColor ("Basalt", Color) = (.24,.19,.17,1)
        _Heat ("Heat", Range(0,1)) = 1
        _Edges ("Exposed left right bottom top", Vector) = (1,1,1,1)
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
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HazardSurface.hlsl"
            TEXTURE2D(_MagmaCracks); SAMPLER(sampler_MagmaCracks);
            CBUFFER_START(UnityPerMaterial)
                float4 _CrustColor, _Edges;
                float _Heat;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes i) { Varyings o; o.positionHCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color*unity_SpriteColor;return o; }
            half4 frag(Varyings i):SV_Target
            {
                float2 uv=i.uv;
                float xEdge=uv.x<.5?_Edges.x:_Edges.y;
                float yEdge=uv.y<.5?_Edges.z:_Edges.w;
                float radius=(22.0/256)*xEdge*yEdge;
                float d=HazardRoundBox(uv-.5,.5,radius);
                half4 surface=HazardSurface(uv);
                half3 basalt=_CrustColor.rgb*lerp(.86,1,_Heat);
                half3 col=HazardStone(uv,basalt,d,22.0/256,17.0/256,26.0/256,surface);
                float gap=SAMPLE_TEXTURE2D(_MagmaCracks,sampler_MagmaCracks,uv).r*20;
                float halo=1-smoothstep(2,11,gap);
                float vein=1-smoothstep(.8,4.1,gap);
                float core=1-smoothstep(0,1.2,gap);
                float heat=lerp(.055,.92,_Heat)*(1+.06*_Heat*sin(_Time.y*.8));
                col*=1-.37*halo;
                col+=half3(.88,.19,.025)*halo*heat*.30;
                col=lerp(col,half3(.94,.25,.035)*heat,vein);
                col=lerp(col,half3(1,.72,.25)*heat*1.65,core);
                // Joined fragments have carved internal seams, with one continuous outside outline.
                float external=min(min(uv.x+(1-_Edges.x),1-uv.x+(1-_Edges.y)),min(uv.y+(1-_Edges.z),1-uv.y+(1-_Edges.w)));
                if(xEdge*yEdge>.5)external=min(external,-d);
                col=HazardOutline(col,basalt,-external,17.0/256);
                float internal=min(min(uv.x+_Edges.x,1-uv.x+_Edges.y),min(uv.y+_Edges.z,1-uv.y+_Edges.w));
                col*=1-.55*(1-smoothstep(3.0/256,5.0/256,internal));
                return half4(col,1-smoothstep(-.002,.001,d))*i.color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
