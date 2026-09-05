Shader "MadTowers/Vine"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _HazardSurface ("Baked stone relief", 2D) = "gray" {}
        _StoneColor ("Moss stone", Color) = (.32, .38, .19, 1)
        _VineColor ("Woody stem", Color) = (.30, .29, .13, 1)
        _LeafColor ("Leaf", Color) = (.35, .54, .17, 1)
        _Growth ("Growth", Range(0, 1)) = 1
        _StoneBody ("Intrinsic brick", Float) = 1
        _Sway ("Tip sway", Float) = .006
        _RootDir ("Root direction", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
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
            #define MADTOWERS_HAZARD_FLOAT
            #include "HazardSurface.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _StoneColor, _VineColor, _LeafColor, _RootDir;
                float _Growth, _StoneBody, _Sway;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionHCS=TransformObjectToHClip(i.positionOS.xyz);
                o.uv=i.uv; o.color=i.color*unity_SpriteColor; return o;
            }
            float stemCenter(float g) { return .17*sin(g*5.1)*sin(g*3.14159); }
            // Composite straight-alpha layers without adding a dark halo on transparent neighbours.
            void over(float3 paint, float mask, inout float3 rgb, inout float alpha)
            {
                float a=mask+alpha*(1-mask);
                rgb=(paint*mask+rgb*alpha*(1-mask))/max(a,.0001); alpha=a;
            }
            void leaf(float2 q, float2 centre, float angle, float2 size, float reveal,
                      float2 dir, float2 perp, float4 surface, inout float3 col, inout float alpha)
            {
                float cs=cos(angle), sn=sin(angle);
                float2 local=q-centre;
                float2 p=float2(cs*local.x-sn*local.y,sn*local.x+cs*local.y)/size;
                // Pointed lens, with a central fold and small painted secondary veins.
                float width=pow(saturate(1-p.y*p.y),.82);
                float d=max(abs(p.x)-width,abs(p.y)-1);
                float grown=smoothstep(reveal,reveal+.18,_Growth);
                float mask=(1-smoothstep(-.035,.035,d))*grown;
                float rim=smoothstep(-.24,-.07,d);
                float fold=smoothstep(-.035,.035,p.x);
                float top=dot(float2(sn,cs),float2(perp.y,dir.y));
                float vein=1-smoothstep(.018,.045,abs(p.x));
                float sideVeins=(1-smoothstep(.035,.075,abs(frac(p.y*3.2-abs(p.x)*.7)-.5)))*.11;
                float3 paint=_LeafColor.rgb*(.75+.40*fold+.12*p.y*top)*(surface.r*1.3+.34);
                paint*=1-sideVeins;
                paint=lerp(paint,_LeafColor.rgb*1.26,vein*.65);
                paint=lerp(paint,_LeafColor.rgb*.25,rim);
                float shadow=(1-smoothstep(-.05,.12,d)) * grown * .46;
                over(float3(.055,.072,.025),shadow,col,alpha);
                over(paint,mask,col,alpha);
            }
            float4 frag(Varyings i):SV_Target
            {
                float2 uv=i.uv;
                float radius=22.0/256, outline=17.0/256, bevel=26.0/256;
                float d=HazardRoundBox(uv-.5,.5, radius);
                float body=1-smoothstep(-.003,.001,d);
                float4 surface=HazardSurface(uv);
                float3 stone=HazardStone(uv,_StoneColor.rgb,d,radius,outline,bevel,surface);
                // Moss catches in porous depressions, while the bevel stays exposed.
                float moss=smoothstep(.56,.72,surface.g)*(1-smoothstep(-.20,-.10,d));
                stone=lerp(stone,float3(.20,.28,.085)*(surface.r+.45),moss*.36);
                float3 col=HazardOutline(stone,_StoneColor.rgb,d,outline);
                float alpha=body*_StoneBody;
                float2 dir=normalize(_RootDir.xy+float2(.00001,.00001));
                float2 perp=float2(-dir.y,dir.x);
                float2 rel=uv-.5;
                float g=dot(rel,dir)+.5;
                float lat=dot(rel,perp)-_Sway*sin(_Time.y*.7)*sin(g*3.14159)*g;
                float sc=stemCenter(g), width=lerp(.044,.019,saturate(g));
                float dist=abs(lat-sc)-width;
                float grow=1-smoothstep(_Growth-.025,_Growth+.015,g);
                float stem=(1-smoothstep(-.004,.004,dist))*grow;
                float shadow=(1-smoothstep(.005,.029,dist))*grow*.66;
                over(float3(.055,.06,.022),shadow,col,alpha);
                float stemOffset=(lat-sc)/width;
                float tube=sqrt(saturate(1-stemOffset*stemOffset)); // defined on both sides of the stem under DXC
                float topLip=(1-smoothstep(.004,.015,abs(lat-sc+width*.47)))*.17;
                float3 bark=_VineColor.rgb*(.55+.55*tube+topLip)*(surface.r+.46);
                bark*=1-.24*surface.a;
                over(bark,stem,col,alpha);
                float2 q=float2(lat,g);
                leaf(q,float2(stemCenter(.23)-.085,.25),-.72,float2(.086,.16),.10,dir,perp,surface,col,alpha);
                leaf(q,float2(stemCenter(.44)+.08,.46),.8,float2(.10,.17),.30,dir,perp,surface,col,alpha);
                leaf(q,float2(stemCenter(.64)-.085,.65),-.75,float2(.09,.16),.49,dir,perp,surface,col,alpha);
                leaf(q,float2(stemCenter(.84)+.063,.83),.68,float2(.068,.135),.69,dir,perp,surface,col,alpha);
                // Keep plants inside the closed stone silhouette, including when growing on neighbours.
                return float4(col,alpha*body)*i.color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
