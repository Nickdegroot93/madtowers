Shader "MadTowers/Bomb"
{
    // Weathered stone held by forged iron hoops, with a copper fuse socket.
    // The authoritative fuse is still supplied by BombBlockBehaviour.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _HazardSurface ("Baked stone relief", 2D) = "gray" {}
        _IronColor ("Stone casing", Color) = (.39, .405, .375, 1)
        _BandColor ("Forged iron", Color) = (.22, .245, .23, 1)
        _EmberColor ("Fuse ember", Color) = (1, .30, .055, 1)
        _HotColor ("White-hot fuse", Color) = (1, .88, .61, 1)
        _CornerRadius ("Corner radius", Range(0, .3)) = .0859375
        _OutlineWidth ("Outline", Range(0, .2)) = .06640625
        _BevelWidth ("Bevel", Range(0, .3)) = .1015625
        _CoreRadius ("Fuse socket radius", Range(.05, .3)) = .15
        _IdleEmber ("Idle ember", Range(0, 1)) = .30
        _Fuse ("Authoritative fuse", Range(0, 1)) = 0
        _Pulse ("Heartbeat", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
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
                half4 _IronColor, _BandColor, _EmberColor, _HotColor;
                float _CornerRadius, _OutlineWidth, _BevelWidth, _CoreRadius;
                float _IdleEmber, _Fuse, _Pulse;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color * unity_SpriteColor;
                return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                float2 uv = i.uv, p = uv - .5;
                float d = HazardRoundBox(p, float2(.5, .5), _CornerRadius);
                float aa = max(fwidth(d), .001);
                float mask = 1 - smoothstep(0, aa, d);
                half4 surface = HazardSurface(uv);
                half3 body = HazardStone(uv, _IronColor.rgb, d, _CornerRadius, _OutlineWidth, _BevelWidth, surface);
                // Raised forged hoops: cast seat shadow, worn top lip and lower chamfer.
                float by = abs(p.y) - .245;
                float hoopD = abs(by) - .045;
                float hoop = 1 - smoothstep(-aa, aa, hoopD);
                float seat = (1 - smoothstep(.008, .035, hoopD)) * (1 - hoop);
                body *= 1 - .35 * seat;
                float localY = by * sign(p.y);
                half3 metal = _BandColor.rgb * (surface.r * 1.8 + .10);
                metal *= 1.0 + .18 * p.y;
                float upperLip = smoothstep(.023, .039, localY);
                float lowerLip = 1 - smoothstep(-.041, -.021, localY);
                metal = lerp(metal, half3(.48, .50, .44), upperLip * .80);
                metal *= 1 - .42 * lowerLip;
                body = lerp(body, metal, hoop);
                // Symmetric forged studs; no random surface or colour per cell.
                float2 rp = float2(abs(p.x) - .31, abs(p.y) - .245);
                float rd = length(rp);
                float rivet = 1 - smoothstep(.023, .027, rd);
                float studSeat = 1 - smoothstep(.028, .040, rd);
                body *= 1 - .48 * studSeat;
                half3 stud = lerp(_BandColor.rgb * .58, half3(.65, .64, .52), saturate(.45 + rp.y * sign(p.y) * 20));
                body = lerp(body, stud, rivet);
                // Recessed hexagonal copper collar, lit from straight above.
                float2 hp = abs(p);
                float hex = max(hp.x * .8660254 + hp.y * .5, hp.y);
                float socket = 1 - smoothstep(.197, .202, hex);
                float socketSeat = 1 - smoothstep(.204, .225, hex);
                body *= 1 - .60 * socketSeat;
                float top = saturate(.5 + p.y * 2.5);
                half3 copper = lerp(half3(.14, .085, .047), half3(.60, .40, .19), top);
                copper *= surface.r * 1.75 + .12;
                body = lerp(body, copper, socket);
                float pr = length(p);
                float core = 1 - smoothstep(_CoreRadius - .003, _CoreRadius + .003, pr);
                float inner = saturate(1 - pr / _CoreRadius);
                float heat = _IdleEmber * (.92 + .08 * _Pulse) + _Fuse * _Fuse * (1.1 + .65 * _Pulse);
                half3 hot = lerp(_EmberColor.rgb, _HotColor.rgb, saturate(_Fuse * 1.1));
                half3 ember = lerp(half3(.07, .025, .008), hot * (.65 + heat), pow(inner, .85));
                // Dark grate bars give the ember physical depth at rest.
                float grateD = min(abs(p.x), abs(abs(p.x) - .070));
                float grate = 1 - smoothstep(.008, .015, grateD);
                ember *= 1 - grate * (.76 - _Fuse * .35);
                body = lerp(body, ember, core);
                // Arming lights the existing stone fractures, never a second timer.
                float reach = .19 + .48 * _Fuse;
                float fissure = surface.a * (1 - smoothstep(reach - .12, reach, pr)) * (1 - socketSeat);
                body = lerp(body, hot * (1 + heat), fissure * _Fuse * (.35 + .65 * _Pulse));
                body += hot * _Fuse * _Fuse * .08 * exp(-pr * 5) * (1 - core);
                body = HazardOutline(body, _IronColor.rgb, d, _OutlineWidth);
                return half4(body, mask) * i.color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
