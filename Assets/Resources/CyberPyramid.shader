Shader "MadTowers/CyberPyramid"
{
    // Whole-piece coordinates preserve the original 3-wide, pointed monument silhouette.
    // Baked relief, full-precision material arithmetic and DXC follow the mobile hazard fixes.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _HazardSurface ("Carved stone relief", 2D) = "gray" {}
        _Charge ("Charged engine segments", Range(0,3)) = 0
        _Ignition ("Engine ignition", Range(0,1)) = 0
        _Phase ("Scaled phase", Float) = 0
        _BeatAt ("Last charge", Float) = -10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma use_dxc vulkan
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #define MADTOWERS_HAZARD_FLOAT
            #include "HazardSurface.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Charge, _Ignition, _Phase, _BeatAt;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionHCS=TransformObjectToHClip(v.positionOS.xyz);
                o.uv=v.uv; o.color=v.color*unity_SpriteColor; return o;
            }
            float band(float d,float w,float aa) { return 1-smoothstep(w-aa,w+aa,abs(d)); }
            float4 frag(Varyings i):SV_Target
            {
                float2 p=(i.uv-.5)*float2(3.3,3.5)+float2(0,.35);
                float side=abs(p.x)-1.5;
                float slope=(abs(p.x)*.946667+p.y-1.92)/1.377;
                float d=max(side,max(-p.y-.5,slope));
                float aa=max(fwidth(d),.002);
                float mask=1-smoothstep(-aa,aa,d);
                float4 relief=HazardSurface(p*.53+float2(.17,.31));
                float4 fine=HazardSurface(p*1.73+float2(.43,.11));
                float grad=1.13-.36*pow(saturate((1.92-p.y)/2.42),1.15);
                // Egyptian sandstone, authored in sRGB like the chapter's painted sprites.
                // The plinth is stone too; the machinery lives UNDER the monument.
                float3 stone=float3(.79,.65,.43);
                float grain=(fine.b-.5)*.10;
                float3 body=stone*grad*(relief.r*1.85+.075+grain);
                float rightFace=smoothstep(-.015,.035,p.x-(1.92-p.y)*.12);
                body*=1-rightFace*.16;
                float wobble=(relief.g-.5)*.075;
                float course=min(abs(p.y-.48+wobble),min(abs(p.y-.96+wobble),abs(p.y-1.43+wobble)));
                float joint=abs(abs(p.x)-.50+wobble);
                if(p.y>.5) joint=abs(p.x+.13+wobble);
                if(p.y>1.02) joint=abs(p.x-.16+wobble);
                float seam=min(course,joint);
                float crack=band(seam,.020,aa);
                body*=1-.63*crack;
                float lip=band(course-.032,.012,aa)*(1-crack);
                body+=stone*lip*.19;
                body*=1-relief.a*.46;
                // Scattered pits and strata are read from the same baked relief as the
                // restyled hazard stones. No per-pixel random noise or coloured frame.
                body*=1-smoothstep(.67,.84,fine.g)*.24;
                float bevel=saturate((d+.17)/.105)*(1-smoothstep(-.07,-.045,d));
                float upward=step(max(side,-p.y-.5),slope);
                body=lerp(body,stone+(1-stone)*.40,bevel*upward*.65);
                body*=1-bevel*(1-upward)*.28;
                // Three carved sun seals in the base course. The seals progressively
                // kindle from dark engraving to amber; no metal sockets or neon trim.
                float cell=clamp(floor((p.x+1.35)/.9),0,2);
                float2 q=p-float2((cell-1)*.9,-.015);
                float radius=length(q);
                float sun=band(radius-.115,.021,aa);
                float stem=band(q.x,.021,aa)*step(-.24,q.y)*step(q.y,-.105);
                float foot=band(q.y+.22,.018,aa)*step(abs(q.x),.080);
                float carving=max(sun,max(stem,foot));
                body*=1-(1-smoothstep(.013,.042,abs(radius-.115)))*.16;
                float lit=step(cell+.5,_Charge);
                float beat=exp(-max(0,_Phase-_BeatAt)*5);
                float3 amber=float3(1,.64,.19);
                float3 inscription=lerp(stone*.26,lerp(amber,float3(1,.88,.48),beat*.35+_Ignition*.30),lit);
                body=lerp(body,inscription,carving);
                // Recessed nozzles are small dark cuts in the underside. Their shutters
                // open at ignition, while all three visible base stones keep their mass.
                float nozzle=abs(p.x-(cell-1)*.9);
                float vent=band(p.y+.42,.021+_Ignition*.025,aa)*step(nozzle,.12);
                body=lerp(body,stone*.14,vent);
                body=HazardOutline(body,stone,d,.060);
                float below=max(0,-.43-p.y);
                float exhaust=1-smoothstep(.04,.14*(1-below*.60),nozzle);
                exhaust*=step(p.y,-.43)*(1-smoothstep(.08,.82*_Ignition+.09,below));
                exhaust*=.70+.16*sin(below*24-_Phase*35);
                float plume=exhaust*_Ignition*(1-mask);
                float alpha=max(mask,plume);
                // Edge coverage always carries sandstone's own dark colour. Blending a
                // coloured exhaust into uncovered pixels created the rejected outer rim.
                float3 col=plume>mask ? lerp(amber,float3(1,.90,.62),.45) : body;
                #ifndef UNITY_COLORSPACE_GAMMA
                col=SRGBToLinear(max(col,0));
                #endif
                return float4(col,alpha)*i.color;
            }
            ENDHLSL
        }
    }
}
