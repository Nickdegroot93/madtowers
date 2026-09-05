Shader "MadTowers/Feather"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _HazardSurface ("Fine down relief", 2D) = "gray" {}
        _DownLight ("Ivory down", Color) = (.94,.88,.73,1)
        _DownDeep ("Warm shadow", Color) = (.65,.53,.36,1)
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
            CBUFFER_START(UnityPerMaterial)
                float4 _DownLight, _DownDeep;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes i) { Varyings o;o.positionHCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color*unity_SpriteColor;return o; }
            void plume(float2 uv,float2 centre,float angle,float2 size,half4 surface,inout half3 col)
            {
                float cs=cos(angle),sn=sin(angle);
                float2 q=uv-centre;
                float2 p=float2(cs*q.x-sn*q.y,sn*q.x+cs*q.y)/size;
                float width=pow(saturate(1-p.y*p.y),.7);
                float dist=max(abs(p.x)-width,abs(p.y)-1);
                float mask=1-smoothstep(-.025,.035,dist);
                float shadow=(1-smoothstep(.0,.18,dist))*.27;
                col=lerp(col,_DownDeep.rgb*.73,shadow);
                float fold=smoothstep(-.025,.025,p.x);
                float ribs=.5+.5*cos((p.y+abs(p.x)*.65)*58);
                float shaft=1-smoothstep(.015,.045,abs(p.x));
                float soft=1-smoothstep(.65,.98,abs(p.x));
                half3 feather=_DownLight.rgb*(.77+.18*fold+.13*saturate(p.y*.5+.5));
                feather*=.97+.045*ribs*soft;
                feather*=.94+.12*surface.b;
                feather=lerp(feather,_DownLight.rgb*1.08,shaft*.65);
                float edge=smoothstep(-.20,-.025,dist);
                feather=lerp(feather,_DownDeep.rgb*.83,edge*.50);
                col=lerp(col,feather,mask);
            }
            half4 frag(Varyings i):SV_Target
            {
                float2 uv=i.uv;
                float d=HazardRoundBox(uv-.5,.5,22.0/256);
                half4 surface=HazardSurface(uv);
                half3 col=lerp(_DownDeep.rgb,_DownLight.rgb,uv.y*.35+.35);
                plume(uv,float2(.27,.28),-.38,float2(.18,.43),surface,col);
                plume(uv,float2(.70,.31),.30,float2(.21,.43),surface,col);
                plume(uv,float2(.42,.64),-.38,float2(.19,.43),surface,col);
                plume(uv,float2(.78,.76),.32,float2(.18,.37),surface,col);
                // Soft fine fibres; no rock fractures and no idle particle shimmer after settling.
                surface.r=lerp(.5,surface.r,.20);surface.a=0;
                col=HazardStone(uv,col,d,22.0/256,17.0/256,26.0/256,surface);
                col=HazardOutline(col,_DownDeep.rgb,d,17.0/256);
                return half4(col,1-smoothstep(-.003,.001,d))*i.color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
