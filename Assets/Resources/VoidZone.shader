Shader "MadTowers/VoidZone"
{
    // A rectangular tear with a chipped, inward-facing lip. WorldRect is still the
    // modifier's exact footprint; only the small visual apron distorts the backdrop.
    // Violet cut faces keep the footprint legible. Organic currents turn and breathe
    // inside that fixed edge; tightening, snap and pull remain the strongest moments.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _NoiseTex ("Tileable material noise", 2D) = "gray" {}
        _Aspect ("Zone aspect", Float) = 1
        _Seed ("Zone seed", Float) = 0
        _ZoneSize ("Rule footprint", Vector) = (3,2,0,0)
        _QuadSize ("Visual apron", Vector) = (3.5,2.5,0,0)
        _EyeColor ("Depth", Color) = (.008,.003,.022,1)
        _SwirlColor ("Violet currents", Color) = (.28,.10,.46,1)
        _RimColor ("Violet cut face", Color) = (.64,.33,.90,1)
        _Hunger ("Feeding pulse", Range(0,1)) = 0
        _Phase ("Scaled visual time", Float) = 0
        _Open ("Tear aperture", Range(0,1)) = 1
        _Anticipation ("Tightening", Range(0,1)) = 0
        _Closing ("Scar", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_VoidSceneTex); SAMPLER(sampler_VoidSceneTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _ZoneSize, _QuadSize, _EyeColor, _SwirlColor, _RimColor;
                float _Aspect, _Seed, _Hunger, _Phase, _Open, _Anticipation, _Closing;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionHCS=TransformObjectToHClip(v.positionOS.xyz);
                o.uv=v.uv; o.color=v.color*unity_SpriteColor; return o;
            }
            float box(float2 p, float2 b)
            {
                float2 q=abs(p)-b+.055;
                return length(max(q,0))+min(max(q.x,q.y),0)-.055;
            }
            half4 frag(Varyings i):SV_Target
            {
                float2 p=(i.uv-.5)*_QuadSize.xy;
                float t=_Phase;
                float4 n1=SAMPLE_TEXTURE2D(_NoiseTex,sampler_NoiseTex,p*.16+float2(_Seed*.017+t*.011,t*.016));
                float4 n2=SAMPLE_TEXTURE2D(_NoiseTex,sampler_NoiseTex,p*.37+float2(-t*.014,_Seed*.031+t*.006));
                float2 halfSize=_ZoneSize.xy*.5;
                float full=box(p,halfSize);
                float2 aperture=halfSize*float2(.40+.60*_Open,max(.012,_Open));
                float chips=sin(p.x*24+p.y*19+n2.g*5)*sin(p.y*37-p.x*11)*.014;
                float d=box(p,aperture)+(n2.r-.5)*.07+chips;
                float aa=max(fwidth(d),.008);
                float inside=1-smoothstep(-aa,aa,d);
                float edge=1-smoothstep(.02,.22,d);
                float2 screen=GetNormalizedScreenSpaceUV(i.positionHCS);
                float distortion=(1-smoothstep(0,.24,abs(d)))*(.0025+_Hunger*.003);
                float2 direction=normalize(p+float2(.0001,.0001));
                half3 backdrop=SAMPLE_TEXTURE2D(_VoidSceneTex,sampler_VoidSceneTex,
                    saturate(screen-direction*distortion+float2(n2.g-.5,n1.r-.5)*distortion)).rgb;
                // The interior moves independently of the rule edge: a wandering eye,
                // breathing folds and counter-moving currents instead of nested boxes.
                float2 drift=float2(sin(t*.53+_Seed),cos(t*.41+_Seed*.7))*.075;
                float2 tunnel=(p+float2(.10,-.06))/max(aperture,float2(.1,.1));
                tunnel+=drift+sin(tunnel.yx*3.4+float2(t*.67,-t*.51))*.085;
                tunnel+=(n1.rg-.5)*.16;
                float radius=length(tunnel)*(.92+.055*sin(t*.83+n1.g*2));
                float angle=atan2(tunnel.y,tunnel.x);
                // Integer angular frequencies meet cleanly across the atan2 seam.
                float flow=.5+.5*sin(radius*13-angle*2+t*.92+n1.r*3.2);
                float undertow=.5+.5*sin(radius*20+angle*3-t*.61+n2.r*2.1);
                float body=smoothstep(.12,.72,radius)*(1-smoothstep(1.0,1.5,radius));
                float fold=flow*flow*flow*body;
                undertow=undertow*undertow*body;
                half3 depth=_EyeColor.rgb+_SwirlColor.rgb*(.055+fold*.76+undertow*.12);
                depth+=half3(.06,.11,.24)*undertow*.28;
                // A continuous coloured cut face identifies all four sides. Weathering
                // changes its light, never erases whole stretches of the danger boundary.
                float lip=1-smoothstep(.028,.105,abs(d+.038));
                float top=saturate(p.y/max(.1,halfSize.y)*.5+.5);
                half3 rim=_RimColor.rgb*(.52+top*.38+n2.g*.16);
                float worn=(1-smoothstep(.008,.025,abs(d+.012)))*smoothstep(.35,.72,n2.g);
                rim=lerp(rim,half3(.78,.62,.94),worn*.26);
                depth=lerp(depth,rim,lip*(.84+_Hunger*.12));
                depth*=1-exp(-abs(d+.16)*16)*.28;
                half3 col=lerp(backdrop,depth,inside);
                float opening=smoothstep(.02,.14,_Open);
                float warning=(1-smoothstep(.015,.05,abs(full-(1-_Anticipation)*.14)))
                    * _Anticipation*(1-opening)*(1-_Closing)*smoothstep(.28,.62,n1.g);
                col=lerp(col,_RimColor.rgb*.65,warning);
                float scar=(1-smoothstep(.015,.055,abs(p.y+(n2.r-.5)*.065)))
                    *(1-smoothstep(halfSize.x*.7,halfSize.x,abs(p.x)))*_Closing;
                col=lerp(col,_RimColor.rgb*.35,scar);
                float alpha=max(edge*opening,max(warning,scar))*i.color.a;
                return half4(col*i.color.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
