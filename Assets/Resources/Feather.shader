Shader "MadTowers/Feather"
{
    // Feather cell: a fixed, theme-independent TRANSLUCENT frosted block - light because you can see
    // through it. Keeps the brick silhouette + bevel so it still reads as a block, is more see-through in
    // the centre and frostier toward the rim, with a soft glowing rim (bloom-lit). Suspended INSIDE the
    // glass are a couple of very faint down wisps - feathers built FROM soft barbs fanning off a curved
    // shaft within a tapered vane (not a solid ellipse), so they read as down, not leaves. Not a true blur
    // (that needs the screen behind it); this is frosted translucency. Motion (float + flutter) is in
    // FeatherBlockSkin. Theme-locked.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _GlassColor ("Glass Colour", Color) = (0.86, 0.92, 0.98, 1)
        _WispColor ("Down Wisp Colour", Color) = (0.99, 0.98, 0.94, 1)
        _WispStrength ("Wisp Strength", Range(0, 1)) = 0.3
        _Alpha ("Centre Opacity", Range(0, 1)) = 0.6
        _RimGlow ("Rim Glow", Range(0, 2)) = 0.6
        _CornerRadius ("Corner Radius", Range(0, 0.3)) = 0.16
        _Seed ("Per-cell Seed", Float) = 0
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
                float4 _GlassColor;
                float4 _WispColor;
                float _WispStrength;
                float _Alpha;
                float _RimGlow;
                float _CornerRadius;
                float _Seed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++) { s += a * vnoise(p); p *= 2.0; a *= 0.5; }
                return s;
            }

            float sdRoundBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, float2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;
            }

            // A soft down feather built FROM its barbs: fine barb strands fanning off a gently curved shaft
            // within a tapered vane (lobe that points at the tip). Returns 0..1 intensity. Soft everywhere
            // so it reads as down, not a crisp leaf.
            float featherWisp(float2 p, float2 center, float angle, float len, float maxW, float ph)
            {
                float2 q = p - center;
                float c = cos(angle), s = sin(angle);
                float2 r = float2(c * q.x + s * q.y, -s * q.x + c * q.y); // r.y along shaft, r.x lateral
                float t = r.y / len + 0.5;                                // 0 base -> 1 tip
                if (t < 0.0 || t > 1.0) return 0.0;

                float shaftX = 0.05 * sin(t * 3.14159 + ph) * (1.0 - t);  // gentle curve, straighter at tip
                float adist = abs(r.x - shaftX);

                float env = pow(sin(3.14159 * t), 0.7);                   // vane lobe: 0 at ends, full mid
                float w = maxW * env * (1.0 - 0.40 * t);                  // narrower toward the pointed tip
                float vane = smoothstep(w + 0.02, w - 0.02, adist);       // soft vane edge

                float barbId = (r.y - adist * 0.6) * 34.0 + ph * 2.0;     // barbs sweep up-and-out
                float fr = frac(barbId);
                float barb = 1.0 - smoothstep(0.0, 0.40, min(fr, 1.0 - fr)); // soft, downy strands

                float shaft = 1.0 - smoothstep(0.0, 0.012, adist);
                float ends = smoothstep(0.0, 0.10, t) * smoothstep(1.0, 0.85, t);
                return saturate(max(shaft, barb * vane)) * ends;
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
                float2 uv = IN.uv;
                float ph = _Seed * 6.2831;

                float2 p = uv - 0.5;
                float halfBox = 0.5;
                float rad = min(_CornerRadius, halfBox - 0.001);
                float d = sdRoundBox(p, float2(halfBox, halfBox), rad);
                float aa = max(fwidth(d), 0.001);
                float mask = 1.0 - smoothstep(0.0, aa, d);

                // Form: gradient + a soft in-hue bevel so it still reads as a block, not a flat pane.
                float grad = 0.92 + 0.14 * uv.y;
                float3 col = _GlassColor.rgb * grad;

                float e = 0.012;
                float dY = sdRoundBox(p + float2(0, e), float2(halfBox, halfBox), rad)
                         - sdRoundBox(p - float2(0, e), float2(halfBox, halfBox), rad);
                float ny = dY / (2.0 * e);
                float band = saturate((d + 0.16) / 0.16);
                if (ny > 0.4) col *= 1.0 + 0.10 * band;
                else if (ny < -0.4) col *= 1.0 - 0.08 * band;

                // Soft frosted cloud so the glass isn't dead flat.
                float fn = fbm(uv * 5.0 + ph);
                col *= (0.94 + 0.12 * fn);

                // Translucency: see-through in the centre, frostier (more opaque) toward the rim.
                float rim = saturate(1.0 + d / 0.16);
                float alpha = _Alpha + 0.24 * rim + 0.06 * (fn - 0.5);

                // Gentle glowing rim (bloom-lit) for an ethereal, weightless feel.
                col += _GlassColor.rgb * rim * _RimGlow * 0.25;

                // A couple of faint down wisps suspended inside the glass (seeded angles per cell).
                float wisp = featherWisp(p, float2(-0.04, 0.02), 0.30 + (frac(ph) - 0.5) * 0.5, 0.72, 0.17, ph);
                wisp = max(wisp, featherWisp(p, float2(0.14, -0.10), -0.45 + (frac(ph * 1.7) - 0.5) * 0.5, 0.50, 0.115, ph + 2.3));
                col = lerp(col, _WispColor.rgb, wisp * _WispStrength);
                alpha += wisp * _WispStrength * 0.20;

                return half4(col, saturate(alpha) * mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
