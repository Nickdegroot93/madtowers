Shader "MadTowers/Locked"
{
    // A Locked HARDWARE overlay: an aged-iron gear bound by a chain and a screw-head locking pin, drawn
    // ON TOP of the kept chapter art (like Vine/Vortex, unlike Anchor which replaces it) so the brick stays
    // solid and the chapter colour shows in the frame around it. A gear is rotation made physical, so a
    // chained, pinned gear reads as "rotation locked" - the cue Locked lacked. Theme-independent fixed
    // cool iron/steel colours (the only warmth is the refusal spark, so it pops). Driven props: _Strain (-1..1) lurches the gear (_MaxLurch) and snaps the chain taut;
    // _Flash sparks the pin. _GearAngle rests each cell's teeth at a different phase; _Col makes the chain's
    // links continuous from one cell to the next so a multi-cell piece reads as one bound chain. The skin
    // drives _Strain/_Flash on scaled time so a pause freezes them (PHYSICS.md). See BLOCKVARIANTS.md.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _RustColor ("Gear iron", Color) = (0.34, 0.31, 0.28, 1)
        _RustHi ("Gear highlight", Color) = (0.80, 0.78, 0.72, 1)
        _ChainColor ("Chain iron", Color) = (0.23, 0.22, 0.22, 1)
        _ChainHi ("Chain highlight", Color) = (0.84, 0.87, 0.93, 1)
        _PinColor ("Pin steel", Color) = (0.70, 0.68, 0.64, 1)
        _FlashColor ("Spark colour", Color) = (1.0, 0.92, 0.72, 1)
        _Strain ("Strain (-1..1, driven)", Float) = 0
        _Flash ("Flash (0..1, driven)", Float) = 0
        _Seed ("Per-cell seed", Float) = 0
        _Col ("Cell column (chain continuity)", Float) = 0
        _GearAngle ("Gear rest angle", Float) = 0
        _GearRadius ("Gear radius", Range(0.1, 0.45)) = 0.30
        _ToothHeight ("Tooth height", Range(0, 0.2)) = 0.075
        _Teeth ("Tooth count", Range(4, 14)) = 8
        _MaxLurch ("Max lurch (rad)", Range(0, 1)) = 0.32
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

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _RustColor;
                float4 _RustHi;
                float4 _ChainColor;
                float4 _ChainHi;
                float4 _PinColor;
                float4 _FlashColor;
                float _Strain;
                float _Flash;
                float _Seed;
                float _Col;
                float _GearAngle;
                float _GearRadius;
                float _ToothHeight;
                float _Teeth;
                float _MaxLurch;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            static const float2 LIGHT = float2(-0.5547, 0.8321); // top-left

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            // Cog outer profile: a disc with _Teeth rounded teeth around the rim.
            float gearDist(float2 q)
            {
                float ang = atan2(q.y, q.x);
                float rad = length(q);
                float tw = cos(ang * _Teeth);
                float tmask = smoothstep(-0.35, 0.35, tw);
                return rad - (_GearRadius + _ToothHeight * tmask);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.uv - 0.5;
                float3 col = 0.0;
                float alpha = 0.0;
                const float3 SEAT = float3(0.09, 0.06, 0.05); // dark contact outline so it pops on any chapter colour

                // ---- Gear (rotates with strain) ----
                float gr = _GearAngle + _Strain * _MaxLurch;
                float cg = cos(gr), sg = sin(gr);
                float2 q = float2(cg * p.x + sg * p.y, -sg * p.x + cg * p.y);

                float gd = gearDist(q);
                float gaa = fwidth(gd) + 1e-4;
                float gmask = 1.0 - smoothstep(0.0, gaa, gd);
                float gout = 1.0 - smoothstep(0.0, gaa, gd - 0.020); // dilated for the dark outline
                col = lerp(col, SEAT, gout);
                alpha = max(alpha, gout);
                if (gmask > 0.001)
                {
                    float e = 0.004;
                    float2 gN = normalize(float2(gearDist(q + float2(e, 0)) - gearDist(q - float2(e, 0)),
                                                 gearDist(q + float2(0, e)) - gearDist(q - float2(0, e))) + 1e-5);
                    float gdir = dot(gN, LIGHT);
                    float rad = length(q);

                    float3 gcol = _RustColor.rgb;
                    gcol *= 0.78 + 0.34 * (0.35 - rad);                         // center brighter, rim darker (form)
                    gcol *= 0.82 + 0.42 * saturate(gdir);                       // domed: top-left face lit, far side in shade
                    float edge = 1.0 - smoothstep(-0.06, 0.0, gd);             // near the rim/teeth
                    gcol += _RustHi.rgb * saturate(gdir) * edge * 0.60;        // bright lit teeth/rim
                    gcol += _RustHi.rgb * 0.05;                                // a touch of overall warmth

                    // Recessed hub: a darker disc with a groove ring (the pin seats in its centre).
                    float hd = rad - 0.145;
                    float hubInside = 1.0 - smoothstep(0.0, gaa, hd);
                    gcol *= (1.0 - 0.34 * hubInside);
                    gcol *= (1.0 - 0.45 * (1.0 - smoothstep(0.0, 0.02, abs(hd))));

                    col = gcol;
                    alpha = gmask;
                }

                // ---- Chain (over the gear; links continuous across cells via _Col) ----
                const float P = 0.25;                       // link pitch (divides 1 -> seamless cell-to-cell)
                const float2 HB = float2(0.155, 0.094);     // link half-extents (rounded box)
                const float CR = 0.085;                     // link corner radius
                const float WALL = 0.040;                   // ring wall thickness
                float xc = p.x + _Col;
                float tension = saturate(abs(_Strain) * 1.6);
                float pull = _Strain * 0.018;
                float kf = floor(xc / P + 0.5);

                float chainMask = 0.0;
                float chainOut = 0.0;
                float3 chainCol = 0.0;
                [unroll]
                for (int di = -1; di <= 1; di++)
                {
                    float k = kf + di;
                    float cxc = k * P;
                    float lx = cxc - _Col;
                    float wave = 0.030 * (1.0 - tension) * sin(cxc * 6.2831853); // slack ripple, flat when taut
                    float2 lp = p - float2(lx + pull, wave);

                    float tilt = (fmod(k, 2.0) == 0.0) ? 0.30 : -0.30;          // interlocking zig-zag
                    float ct = cos(tilt), st = sin(tilt);
                    float2 rp = float2(ct * lp.x + st * lp.y, -st * lp.x + ct * lp.y);

                    float d = sdRoundBox(rp, HB, CR);
                    float ring = abs(d) - WALL;
                    float aa = fwidth(ring) + 1e-4;
                    float m = 1.0 - smoothstep(0.0, aa, ring);
                    chainOut = max(chainOut, 1.0 - smoothstep(0.0, aa, ring - 0.016)); // dilated for outline
                    if (m > chainMask)
                    {
                        float s = saturate(abs(d) / WALL);
                        float crest = 1.0 - s * s;                              // rounded tube across the wall
                        float e = 0.004;
                        float2 g = normalize(float2(sdRoundBox(rp + float2(e, 0), HB, CR) - sdRoundBox(rp - float2(e, 0), HB, CR),
                                                    sdRoundBox(rp + float2(0, e), HB, CR) - sdRoundBox(rp - float2(0, e), HB, CR)) + 1e-5);
                        float2 nW = float2(ct * g.x - st * g.y, st * g.x + ct * g.y); // normal back to world
                        float side = sign(d);
                        float lit = 0.55 + 0.5 * dot(nW, LIGHT) * side;

                        float3 lc = _ChainColor.rgb * (0.5 + 0.85 * lit);
                        lc += _ChainHi.rgb * pow(crest, 2.0) * saturate(dot(nW, LIGHT) * side + 0.4) * 0.75;
                        lc += _ChainHi.rgb * tension * 0.22;                    // whole chain glints when taut

                        chainMask = m;
                        chainCol = lc;
                    }
                }
                col = lerp(col, SEAT, chainOut);   // dark outline (also separates chain from gear)
                alpha = max(alpha, chainOut);
                col = lerp(col, chainCol, chainMask);
                alpha = max(alpha, chainMask);

                // ---- Locking pin (screw head over the hub) ----
                float prad = length(p);
                float pd = prad - 0.082;
                float paa = fwidth(pd) + 1e-4;
                float pinMask = 1.0 - smoothstep(0.0, paa, pd);
                float pinOut = 1.0 - smoothstep(0.0, paa, pd - 0.016);
                col = lerp(col, SEAT, pinOut);
                alpha = max(alpha, pinOut);
                if (pinMask > 0.001)
                {
                    float dome = saturate(1.0 - prad / 0.082);
                    float3 pcol = _PinColor.rgb * (0.65 + 0.55 * dome);
                    float2 nd = p / max(prad, 1e-4);
                    pcol += _PinColor.rgb * saturate(dot(nd, LIGHT)) * 0.35;

                    // Phillips cross slot (darker recess).
                    float sw = 0.014, sl = 0.060;
                    float s1 = (1.0 - smoothstep(sw - 0.006, sw, abs(p.y))) * (1.0 - smoothstep(sl, sl + 0.006, abs(p.x)));
                    float s2 = (1.0 - smoothstep(sw - 0.006, sw, abs(p.x))) * (1.0 - smoothstep(sl, sl + 0.006, abs(p.y)));
                    float slot = max(s1, s2);
                    pcol *= (1.0 - 0.55 * slot);

                    pcol += _FlashColor.rgb * _Flash * (0.6 + 0.4 * dome); // pin glows on refuse
                    col = lerp(col, pcol, pinMask);
                    alpha = max(alpha, pinMask);
                }

                // ---- Spark burst on refuse (radial streaks from the pin) ----
                if (_Flash > 0.001)
                {
                    float ang = atan2(p.y, p.x);
                    float streak = pow(saturate(cos(ang * 8.0)), 18.0);
                    float ring = smoothstep(0.075, 0.13, prad) * smoothstep(0.30, 0.13, prad);
                    float spark = streak * ring * _Flash;
                    col += _FlashColor.rgb * spark * 1.4;
                    alpha = max(alpha, spark);
                }

                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
