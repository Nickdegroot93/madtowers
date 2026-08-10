Shader "MadTowers/Curse"
{
    // Curse cell v3 (Nick's look review 2026-08-02: no rune dots, no rim glow, not gray, not
    // Maw-violet - the EYE IS the countdown). A near-black CURSED OBSIDIAN brick with a deep
    // green cast. At rest it barely differs from a dark stone: a faint eyelid seam on EVERY
    // cell (each cell carries the eye; a covered cell's eye shuts via its own _Expose).
    // As the countdown burns (_Left of _MaxLeft) the corruption leaks out:
    //   - hairline CRACK VEINS across every cell glow acid green from within, brighter and
    //     pulsing harder as doom rises (the whole-brick alarm, replacing v2's rim wash),
    //   - the EYE slowly opens - half-lidded at 2, and on the LAST sigil it is huge, round,
    //     bloodshot and staring, pupil twitching with the heartbeat (_Pulse),
    //   - it occasionally BLINKS (_Phase) so it reads alive, not painted.
    // SOUL SMOKE rises from exposed top cells (_Expose, per cell) - the bury-me beacon.
    // _Fire is the detonation: blinding flash + two shock rings expanding past the brick edge.
    // The body sits inset in an oversized quad (_BodyHalf, Maw pattern) for smoke headroom.
    // The stone body draws in the CELL frame (rotates with the piece, like every brick);
    // _UpDir keeps only the eye + smoke upright however the piece landed (Maw pattern -
    // rotating the whole frame made falling pieces render as a wheel of upright cubes).
    // Component-driven time (_Phase)
    // so a pause freezes it. Framed by the shared brick recipe. Theme-independent.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _StoneColor ("Obsidian Colour", Color) = (0.085, 0.115, 0.10, 1)
        _SheenColor ("Polish Sheen Colour", Color) = (0.22, 0.30, 0.27, 1)
        _VeinColor ("Corruption Colour", Color) = (0.42, 1.0, 0.35, 1)
        _ScleraColor ("Eye Sclera Colour", Color) = (0.82, 0.88, 0.62, 1)
        _IrisColor ("Iris Colour", Color) = (0.35, 0.95, 0.25, 1)
        _BloodColor ("Bloodshot Colour", Color) = (0.75, 0.25, 0.18, 1)
        _SmokeColor ("Soul Smoke Colour", Color) = (0.50, 0.95, 0.55, 1)
        _FireColor ("Detonation Colour", Color) = (0.92, 1.0, 0.85, 1)
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.086
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.066
        _BevelWidth ("Bevel Width", Range(0, 0.3)) = 0.102
        _BodyHalf ("Body Half Extent", Range(0.1, 0.5)) = 0.2778
        _Seed ("Per-cell Seed", Range(0, 1)) = 0
        _UpDir ("World Up (cell frame)", Vector) = (0, 1, 0, 0)
        _Active ("Awake (0..1, driven)", Range(0, 1)) = 0
        _Expose ("Exposed (0..1, driven)", Range(0, 1)) = 1
        _Left ("Placements Left (driven)", Float) = 4
        _MaxLeft ("Countdown Length (driven)", Float) = 4
        _Tick ("Sigil Burn Flash (driven)", Range(0, 1)) = 0
        _Fire ("Detonation (driven)", Range(0, 1)) = 0
        _Pulse ("Heartbeat (driven)", Range(0, 1)) = 0
        _Phase ("Scaled Time (driven)", Float) = 0
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
                float4 _StoneColor;
                float4 _SheenColor;
                float4 _VeinColor;
                float4 _ScleraColor;
                float4 _IrisColor;
                float4 _BloodColor;
                float4 _SmokeColor;
                float4 _FireColor;
                float _CornerRadius;
                float _OutlineWidth;
                float _BevelWidth;
                float _BodyHalf;
                float _Seed;
                float4 _UpDir;
                float _Active;
                float _Expose;
                float _Left;
                float _MaxLeft;
                float _Tick;
                float _Fire;
                float _Pulse;
                float _Phase;
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

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, float2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;
            }

            // Hairline crack network: Voronoi plate edges (F2-F1) - real fracture geometry,
            // thin polygonal seams, never the closed blobs a noise iso-line makes.
            float veins(float2 p, float seed)
            {
                float2 g = floor(p);
                float2 f = frac(p);
                float f1 = 8.0, f2 = 8.0;
                [unroll(3)]
                for (int y = -1; y <= 1; y++)
                [unroll(3)]
                for (int x = -1; x <= 1; x++)
                {
                    float2 o = float2(x, y);
                    float2 site = o + float2(hash21(g + o + seed * 13.0), hash21(g + o + seed * 13.0 + 7.7)) - f;
                    float dd = dot(site, site);
                    if (dd < f1) { f2 = f1; f1 = dd; }
                    else if (dd < f2) { f2 = dd; }
                }
                float edge = sqrt(f2) - sqrt(f1);      // 0 exactly on a plate boundary
                return 1.0 - smoothstep(0.0, 0.05, edge);
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
                // The BODY lives in the cell's OWN frame so the stone rotates rigidly with
                // the piece, like every other brick - rotating the whole frame made a
                // falling piece render as world-upright cubes orbiting the pivot ("the
                // wheel", Nick 2026-08-10). Only the LIVING parts - eye and soul smoke -
                // use the world-up frame, so they stay upright however the piece landed.
                // This is the actual Maw pattern: its flesh is raw-frame too, only the
                // mouth and eyes rotate.
                float2 pRaw = IN.uv - 0.5;
                float2 upDir = _UpDir.xy;
                float2 u2 = (dot(upDir, upDir) < 1e-5) ? float2(0, 1) : normalize(upDir);
                float2 r2 = float2(u2.y, -u2.x);
                float2 p = float2(dot(pRaw, r2), dot(pRaw, u2)); // up-frame: eye + smoke only

                // Body frame: pb spans [-0.5, 0.5] over the inset brick body (cell frame).
                float2 pb = pRaw / (2.0 * _BodyHalf);
                float2 bodyUv = pb + 0.5;
                float3 stone = _StoneColor.rgb;

                float halfBox = 0.5;
                float r = min(_CornerRadius, halfBox - 0.001);
                float2 bb = float2(halfBox, halfBox);
                float d = sdRoundBox(pb, bb, r);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                // Shared frame: gradient + embossed bevel + AO ring.
                float grad = 1.13 - 0.36 * pow(saturate(1.0 - bodyUv.y), 1.15);
                float3 body = stone * grad;
                float e = 0.012;
                float dY = sdRoundBox(pb + float2(0, e), bb, r) - sdRoundBox(pb - float2(0, e), bb, r);
                float dX = sdRoundBox(pb + float2(e, 0), bb, r) - sdRoundBox(pb - float2(e, 0), bb, r);
                float ny = dY / (2.0 * e);
                float nx = dX / (2.0 * e);
                float band = pow(saturate((d + _OutlineWidth + _BevelWidth) / max(_BevelWidth, 0.001)), 1.6);
                band *= saturate((-d - _OutlineWidth * 0.55) / max(_OutlineWidth * 0.45, 0.001));
                float topness  = saturate((ny - 0.25) / 0.5);
                float botness  = saturate((-ny - 0.25) / 0.5);
                float sideness = saturate((abs(nx) - 0.25) / 0.5) * (1.0 - topness) * (1.0 - botness);
                body *= (1.0 - 0.09 * band);
                float3 hiCol = lerp(stone * 2.2, _SheenColor.rgb, 0.55) * grad;
                body = lerp(body, hiCol, 0.55 * band * topness);
                body *= (1.0 - 0.30 * band * botness);
                body *= (1.0 - 0.14 * band * sideness);

                // Obsidian character: a soft diagonal polish sheen + dark mottle, so the black
                // reads as glassy volcanic stone, never flat gray.
                float sheenBand = 1.0 - smoothstep(0.0, 0.5, abs(pb.x + pb.y * 0.6 - 0.12 + (_Seed - 0.5) * 0.3));
                body += _SheenColor.rgb * sheenBand * 0.10;
                float mottle = vnoise(pb * 5.0 + _Seed * 17.0) - 0.5;
                body *= (1.0 + mottle * 0.14);

                float doom = 1.0 - saturate(_Left / max(_MaxLeft, 1.0));
                float lastOne = saturate(1.0 - abs(_Left - 1.0));   // 1 exactly when one sigil remains
                float sig = saturate(_Active);
                float alive = sig * _Expose;                        // buried curses go dark and quiet
                float flare = _Fire * _Fire;

                // CORRUPTION CRACKS: faint dark hairline seams at rest; as doom rises they glow
                // acid green from within, pulsing with the heartbeat - the whole-brick alarm.
                // A sigil burn (_Tick) makes them flash briefly on every cell.
                float vein = veins(pb * 2.6 + float2(_Seed * 3.7, _Seed * 9.2), _Seed);
                body *= (1.0 - vein * 0.18);                        // engraved, always
                float veinGlow = alive * (doom * doom * (0.30 + 0.55 * _Pulse) + _Tick * 0.8);
                veinGlow = max(veinGlow, alive * lastOne * (0.40 + 0.45 * _Pulse));
                body += _VeinColor.rgb * vein * saturate(veinGlow) * 0.75;

                // THE EYE - on every cell (a covered cell's shuts via its own _Expose).
                // Closed: a subtle curved lid seam (the brick barely differs from plain stone,
                // Nick's brief). It opens stepwise with the countdown; on the last placement
                // it is huge, round, bloodshot, staring.
                {
                    // Occasional slow blink so it reads alive (never while wide - terror doesn't blink).
                    float blinkT = frac(_Phase * 0.21 + _Seed * 5.0);
                    float blink = 1.0 - smoothstep(0.0, 0.05, blinkT) * (1.0 - smoothstep(0.07, 0.12, blinkT));
                    blink = lerp(blink, 1.0, lastOne);

                    // LINEAR steps so every burned sigil visibly opens the lids; sig and the
                    // cell's own exposure HARD-gate it - falling or buried, the eye is SHUT.
                    float openAmt = sig * _Expose * (0.10 + 0.90 * doom);
                    openAmt = max(openAmt, sig * _Expose * lastOne);     // last sigil: fully wide
                    openAmt *= lerp(1.0, blink, step(openAmt, 0.75));

                    // Up-frame body coords: the eye (and its closed seam) stays world-
                    // upright in any landed rotation, exactly like the Maw's face.
                    float2 ep = p / (2.0 * _BodyHalf) - float2(0.0, 0.02);
                    // Wide almond that goes ROUND as it opens fully (scary-stage silhouette).
                    float almondW = 0.36;
                    float roundness = lerp(2.0, 1.05, openAmt);          // exponent: sliver -> round
                    float lidHalf = (0.29 * openAmt)
                                    * pow(saturate(1.0 - pow(abs(ep.x) / almondW, 2.0)), 0.5 * roundness);
                    float inEye = step(abs(ep.y), lidHalf) * step(abs(ep.x), almondW) * step(0.02, openAmt);

                    // The closed seam: one soft curved lash-line, only visible while mostly shut.
                    float seamY = ep.y - 0.03 * (1.0 - pow(abs(ep.x) / almondW, 2.0));
                    float seam = smoothstep(0.016, 0.0, abs(seamY)) * step(abs(ep.x), almondW)
                                 * (1.0 - inEye) * saturate(1.0 - openAmt * 1.4);
                    body *= (1.0 - seam * 0.6);

                    // Lid edge shading so the opening reads as a socket, not a sticker.
                    float lidEdge = smoothstep(0.025, 0.0, abs(abs(ep.y) - lidHalf))
                                    * step(abs(ep.x), almondW) * step(0.1, openAmt);
                    body *= (1.0 - lidEdge * 0.45);

                    // Sclera: shaded toward the lids so the ball reads round, dimmer when barely open.
                    float3 eyeCol = _ScleraColor.rgb * (0.55 + 0.45 * saturate(openAmt * 1.6))
                                    * (1.0 - 0.35 * saturate(abs(ep.y) / max(lidHalf, 1e-4)));
                    // Bloodshot: thin RADIAL strokes creeping in from the corners as doom rises.
                    float theta = atan2(ep.y, ep.x);
                    float streaks = 1.0 - smoothstep(0.0, 0.30,
                        abs(frac(theta * 3.5 + hash21(float2(_Seed, 3.3)) * 7.0) - 0.5) * 2.0);
                    float blood = streaks * smoothstep(0.10, 0.30, abs(ep.x)) * saturate(doom * 1.2);
                    eyeCol = lerp(eyeCol, _BloodColor.rgb, blood * 0.6);

                    // Iris: radial gradient + dark limbal ring; pupil a true vertical ellipse
                    // that TWITCHES with the heartbeat on the last sigil.
                    float irisR = length(ep * float2(1.0, 1.15));
                    float iris = 1.0 - smoothstep(0.115, 0.135, irisR);
                    float limbal = smoothstep(0.085, 0.13, irisR);       // darker toward the rim
                    float3 irisCol = _IrisColor.rgb * lerp(1.15, 0.45, limbal)
                                     * (0.75 + 0.35 * _Pulse + 0.9 * lastOne * _Pulse);
                    float pupilW = lerp(0.042, 0.020 + 0.030 * _Pulse, lastOne);
                    float pupilD = length(ep * float2(1.0 / max(pupilW, 1e-4), 1.0 / 0.085));
                    float pupil = 1.0 - smoothstep(0.85, 1.0, pupilD);
                    eyeCol = lerp(eyeCol, irisCol, iris);
                    eyeCol = lerp(eyeCol, float3(0.01, 0.02, 0.01), pupil * iris);
                    // Catchlight: one tiny off-centre glint sells "wet eyeball".
                    float glint = 1.0 - smoothstep(0.0, 0.035, length(ep - float2(-0.045, 0.045)));
                    eyeCol = lerp(eyeCol, float3(0.95, 1.0, 0.92), glint * 0.8 * saturate(openAmt * 1.4));

                    body = lerp(body, eyeCol, inEye);
                    // A soft green under-glow bleeds off a wide-open eye onto the stone.
                    float bleed = (1.0 - smoothstep(lidHalf, lidHalf + 0.16, length(ep * float2(0.8, 1.4))))
                                  * (1.0 - inEye) * openAmt * 0.45;
                    body += _IrisColor.rgb * bleed * (0.4 + 0.6 * _Pulse) * alive;
                }

                // DETONATION: a radial blast - blinding at the core, falling off outward so the
                // shock rings stay readable instead of drowning in a flat white square.
                float prBody = length(pb);
                body = lerp(body, _FireColor.rgb, saturate(flare * (1.7 - prBody * 1.6)) * sig);

                // Outline: thick, near-black.
                float tOut = saturate(1.0 + d / max(_OutlineWidth, 0.001));
                float lumT = dot(stone, float3(0.299, 0.587, 0.114));
                float3 outCol = lerp(stone, float3(lumT, lumT, lumT), 0.30) * 0.22;
                body = lerp(body, outCol * grad, tOut);

                // SOUL SMOKE above the body - only while exposed and awake; thicker and faster
                // as doom rises.
                float headroom = 0.5 - _BodyHalf;
                float sy = saturate((p.y - _BodyHalf) / max(headroom, 1e-4));
                float smokeA = 0.0;
                float3 smokeCol = _SmokeColor.rgb;
                if (alive > 0.001 && p.y > _BodyHalf)
                {
                    float thick = 1.0 + doom * 0.9;
                    float speed = 1.0 + doom * 1.4;
                    [unroll(2)]
                    for (int w = 0; w < 2; w++)
                    {
                        float fw2 = (float)w;
                        float side = fw2 * 2.0 - 1.0;
                        float baseX = side * (0.075 + 0.05 * frac(_Seed * 7.31 + fw2 * 0.37));
                        float sway = sin(sy * 9.0 + _Phase * (1.6 + fw2 * 0.7) * speed + _Seed * 20.0 + fw2 * 3.1)
                                     * (0.02 + 0.10 * sy);
                        float cx = baseX + sway;
                        float width = lerp(0.055, 0.016, sy) * thick;
                        float strand = 1.0 - smoothstep(0.0, width, abs(p.x - cx));
                        float fade = (1.0 - sy) * (0.55 + 0.45 * vnoise(float2(sy * 5.0 - _Phase * 1.2 * speed, _Seed * 9.0 + fw2)));
                        smokeA = max(smokeA, strand * fade);
                    }
                    smokeA = saturate(smokeA * alive * (0.7 + 0.35 * doom));
                    smokeCol = lerp(_SmokeColor.rgb, _FireColor.rgb, flare);
                    smokeA = saturate(smokeA * (1.0 + flare * 2.0));
                }

                // DETONATION shock rings crossing the whole quad, past the brick edge.
                float pr = length(p);
                float ring1 = lerp(0.10, 0.70, 1.0 - _Fire);
                float ring2 = lerp(0.02, 0.55, 1.0 - _Fire);
                float shock = smoothstep(0.085, 0.0, abs(pr - ring1)) * _Fire
                            + smoothstep(0.055, 0.0, abs(pr - ring2)) * _Fire * 0.7;
                shock *= sig;

                float3 outRgb = lerp(smokeCol, body, mask);
                outRgb = lerp(outRgb, _FireColor.rgb, saturate(shock));
                float outA = max(max(mask, smokeA), saturate(shock));
                return half4(outRgb, outA);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
