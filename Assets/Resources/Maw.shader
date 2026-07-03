Shader "MadTowers/Maw"
{
    // Maw cell: a fleshy violet brick that IS a monster. Falling it's a smooth brick with a pressed-shut
    // smiling mouth seam on its world-up face. On landing (_Active 0->1) the mouth parts to a hungry grin
    // of ivory zigzag teeth over a dark gullet and two mismatched eyes blink open. On a devour (_Chomp
    // pulsed by MawBlockSkin) the jaw gapes UP past the brick's top edge (the quad is oversized,
    // CellScale 1.8; the body spans _BodyHalf), a tongue shows behind the lower teeth, and the eyes
    // squeeze shut as it slams. Only truly exposed top cells show the face (_Expose) - covered cells
    // stay smooth. _UpDir keeps mouth + eyes world-upright in any landed rotation. Framed by the shared
    // brick recipe. Theme-independent (the chapter art is hidden).
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _FleshColor ("Flesh Colour", Color) = (0.35, 0.15, 0.40, 1)
        _LipColor ("Lip Colour", Color) = (0.16, 0.06, 0.19, 1)
        _GulletColor ("Gullet Colour", Color) = (0.13, 0.025, 0.055, 1)
        _ThroatColor ("Throat Glow Colour", Color) = (0.48, 0.09, 0.11, 1)
        _ToothColor ("Tooth Colour", Color) = (0.93, 0.89, 0.78, 1)
        _TongueColor ("Tongue Colour", Color) = (0.72, 0.24, 0.34, 1)
        _EyeColor ("Eye Colour", Color) = (0.95, 0.92, 0.83, 1)
        _PupilColor ("Pupil Colour", Color) = (0.10, 0.05, 0.09, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.05
        _BodyHalf ("Body Half Extent (driven)", Range(0.1, 0.5)) = 0.2778
        _Seed ("Per-cell Seed (driven)", Float) = 0
        _Active ("Awake (0..1, driven)", Range(0, 1)) = 0
        _Chomp ("Chomp (0..1, driven)", Range(0, 1)) = 0
        _Expose ("Exposed Top Cell (0/1, driven)", Range(0, 1)) = 1
        _UpDir ("World Up (xy, driven)", Vector) = (0, 1, 0, 0)
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
                float4 _FleshColor;
                float4 _LipColor;
                float4 _GulletColor;
                float4 _ThroatColor;
                float4 _ToothColor;
                float4 _TongueColor;
                float4 _EyeColor;
                float4 _PupilColor;
                float _CornerRadius;
                float _BodyHalf;
                float _Seed;
                float _Active;
                float _Chomp;
                float _Expose;
                float4 _UpDir;
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

            float fbm(float2 p)
            {
                float v = 0.0, amp = 0.5;
                for (int i = 0; i < 3; i++) { v += amp * vnoise(p); p *= 2.02; amp *= 0.5; }
                return v;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, float2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;
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
                float2 pc = IN.uv - 0.5;
                float ph = _Seed * 6.2831;
                float tm = _Time.y;
                float bh = _BodyHalf;

                // --- Body: fleshy violet brick, shared frame, axis-aligned in the quad ---
                float2 bb = float2(bh, bh);
                float r = min(_CornerRadius, bh - 0.001);
                float d = sdRoundBox(pc, bb, r);
                float aa = max(fwidth(d), 0.001);
                float bodyMask = 1.0 - smoothstep(0.0, aa, d);

                // The shared frame constants scale with the inset body (body spans 2*bh of the quad).
                float ow = 0.066 * (bh / 0.5);
                float bw = 0.102 * (bh / 0.5);

                float upBody = saturate((pc.y + bh) / (2.0 * bh));   // 0 bottom of body .. 1 top
                float grad = 1.13 - 0.36 * pow(saturate(1.0 - upBody), 1.15);
                float3 flesh = _FleshColor.rgb;
                float mot = fbm(IN.uv * 5.0 + ph);
                float3 col = flesh * grad * (0.86 + 0.26 * mot);

                // Warty bumps: sparse lit dots.
                float wart = step(0.93, hash21(floor((IN.uv + ph) * 13.0)));
                col *= (1.0 + wart * 0.14);

                // Embossed bevel + AO + outline (near-black, violet hue kept).
                float e = 0.012;
                float dY = sdRoundBox(pc + float2(0, e), bb, r) - sdRoundBox(pc - float2(0, e), bb, r);
                float dX = sdRoundBox(pc + float2(e, 0), bb, r) - sdRoundBox(pc - float2(e, 0), bb, r);
                float ny = dY / (2.0 * e);
                float nx = dX / (2.0 * e);
                float band = pow(saturate((d + ow + bw) / max(bw, 0.001)), 1.6);
                band *= saturate((-d - ow * 0.55) / max(ow * 0.45, 0.001));
                float topness  = saturate((ny - 0.25) / 0.5);
                float botness  = saturate((-ny - 0.25) / 0.5);
                float sideness = saturate((abs(nx) - 0.25) / 0.5) * (1.0 - topness) * (1.0 - botness);
                col *= (1.0 - 0.09 * band);
                float3 hiCol = lerp(flesh * 1.5, 1.0 - (1.0 - flesh) * 0.42, 0.45) * grad;
                col = lerp(col, hiCol, 0.55 * band * topness);
                col *= (1.0 - 0.26 * band * botness);
                col *= (1.0 - 0.12 * band * sideness);
                float tOut = saturate(1.0 + d / max(ow, 0.001));
                float lumT = dot(flesh, float3(0.299, 0.587, 0.114));
                float3 outCol = lerp(flesh, float3(lumT, lumT, lumT), 0.30) * 0.22;
                col = lerp(col, outCol * grad, tOut);
                col *= (0.97 + 0.03 * sin(tm * 2.2 + ph));            // slow breathing

                // --- Mouth + eyes live in the world-up frame ---
                float2 up = normalize(_UpDir.xy + float2(1e-4, 1e-4));
                float2 perp = float2(-up.y, up.x);
                float u = dot(pc, up);
                float v = dot(pc, perp);

                float show = _Expose;                                  // covered cells stay smooth
                float wake = saturate(_Active);
                float chomp = saturate(_Chomp);

                // Jaw: dormant = pressed smiling seam; awake = hungry grin; chomp = gape past the top edge.
                float breathe = 1.0 + 0.10 * sin(tm * 1.7 + ph);
                float hu = (0.012 + wake * 0.075 * breathe + chomp * 0.16) * (bh / 0.2778);
                float hv = bh * (0.66 + 0.06 * wake);
                float u0 = bh * (0.42 + 0.42 * chomp);                 // rises above the brick when chomping
                float smile = bh * 0.10 * (1.0 - 0.5 * chomp);         // corners curl up (fades as it gapes)
                float vn = v / max(hv, 1e-4);
                float2 mq = float2(v, u - u0 - smile * (vn * vn - 0.35));
                float dm = sdRoundBox(mq, float2(hv, hu), min(hu, hv) * 0.7);
                float mAA = max(fwidth(dm), 0.002);
                float mouth = 1.0 - smoothstep(0.0, mAA, dm);
                float lipW = 0.028 * (bh / 0.2778);
                float lip = (1.0 - smoothstep(lipW, lipW + mAA * 2.0, dm)) - mouth;    // rim band

                // Gullet: darker toward the throat with a faint hot glow at the very back.
                float depth = saturate(0.5 - mq.y / max(hu * 2.0, 1e-4));              // 0 top lip .. 1 bottom
                float3 gullet = lerp(_GulletColor.rgb, _ThroatColor.rgb, pow(1.0 - abs(depth - 0.5) * 2.0, 2.5) * 0.8);

                // Tongue: a rounded muscle rising behind the lower teeth as the jaw gapes.
                float tongueAmt = smoothstep(0.15, 0.6, chomp);
                float2 tq = float2(v / max(hv * 0.55, 1e-4), (mq.y + hu * (1.0 - 0.5 * tongueAmt)) / max(hu * 0.9, 1e-4));
                float tongue = (1.0 - smoothstep(0.75, 1.0, length(tq))) * tongueAmt;
                float3 tongueCol = _TongueColor.rgb * (0.75 + 0.35 * saturate(1.0 - length(tq)));
                gullet = lerp(gullet, tongueCol, tongue);

                // Ivory zigzag teeth from both lips (offset half a tooth so they interlock when shut).
                float tw = 9.0;
                float triU = abs(frac(v * tw + _Seed * 3.1) - 0.5) * 2.0;              // upper 0 tip..1 gap
                float triL = abs(frac(v * tw + 0.5 + _Seed * 3.1) - 0.5) * 2.0;
                float toothLen = min(hu * 1.6, 0.075 * (bh / 0.2778));
                float upperEdge = hu - mq.y;                                           // depth below upper lip
                float lowerEdge = mq.y + hu;                                           // height above lower lip
                float upperTooth = smoothstep(0.004, 0.0, upperEdge - toothLen * (1.0 - triU));
                float lowerTooth = smoothstep(0.004, 0.0, lowerEdge - toothLen * 0.8 * (1.0 - triL));
                float tooth = max(upperTooth * step(0.0, upperEdge), lowerTooth * step(0.0, lowerEdge));

                float3 mouthCol = gullet;
                float toothShade = 0.78 + 0.22 * saturate(1.0 - triU * 0.7);
                mouthCol = lerp(mouthCol, _ToothColor.rgb * toothShade, tooth);

                // Compose mouth over body. Lips: dark rim with a lit lower lip (light from above catches it).
                float lowerLip = lip * saturate(-mq.y / max(hu + lipW, 1e-4));
                float3 lipCol = _LipColor.rgb * (0.85 + 0.30 * grad) * (1.0 + 0.55 * lowerLip);
                col = lerp(col, lipCol, lip * show);
                col = lerp(col, mouthCol, mouth * show);
                float mouthAll = max(mouth, lip) * show;

                // --- Eyes: two mismatched, blink open with _Active, squeeze shut on chomp ---
                float open = saturate(wake * 1.2 - chomp * 1.4);
                float2 eq1 = float2(v + bh * 0.34, u + bh * 0.05);
                float2 eq2 = float2(v - bh * 0.38, u + bh * 0.00);
                float er1 = bh * 0.22, er2 = bh * 0.135;
                float de1 = length(eq1 / float2(er1, er1 * (0.35 + 0.65 * open))) - 1.0;
                float de2 = length(eq2 / float2(er2, er2 * (0.35 + 0.65 * open))) - 1.0;
                float eye1 = (1.0 - smoothstep(-0.08, 0.08, de1)) * wake * show;
                float eye2 = (1.0 - smoothstep(-0.10, 0.10, de2)) * wake * show;
                col = lerp(col, _EyeColor.rgb, max(eye1, eye2) * 0.95);
                // Pupils look up toward the prey.
                float pup1 = 1.0 - smoothstep(0.0, 0.02, length(eq1 - float2(0.0, er1 * 0.28)) - er1 * 0.38);
                float pup2 = 1.0 - smoothstep(0.0, 0.02, length(eq2 - float2(0.0, er2 * 0.30)) - er2 * 0.42);
                col = lerp(col, _PupilColor.rgb, max(pup1 * eye1, pup2 * eye2) * open);

                float alpha = max(bodyMask, mouthAll);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
