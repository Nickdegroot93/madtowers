#ifndef MADTOWERS_HAZARD_SURFACE_INCLUDED
#define MADTOWERS_HAZARD_SURFACE_INCLUDED
// Offline-authored periodic material fields; R is top-lit relief at half scale.
// Some Adreno shader paths also require full-precision material arithmetic.
// Opt in per shader; preserve the established path for other hazards.
#if defined(MADTOWERS_HAZARD_FLOAT)
#define HAZARD_COLOR3 float3
#define HAZARD_COLOR4 float4
TEXTURE2D_FLOAT(_HazardSurface);
#else
#define HAZARD_COLOR3 half3
#define HAZARD_COLOR4 half4
TEXTURE2D(_HazardSurface);
#endif
SAMPLER(sampler_HazardSurface);
HAZARD_COLOR4 HazardSurface(float2 uv)
{
    return SAMPLE_TEXTURE2D(_HazardSurface, sampler_HazardSurface, uv);
}
float HazardRoundBox(float2 p, float2 halfSize, float radius)
{
    float2 q = abs(p) - halfSize + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}
HAZARD_COLOR3 HazardStone(float2 uv, HAZARD_COLOR3 tint, float d, float radius,
                  float outline, float bevel, HAZARD_COLOR4 surface)
{
#if !defined(MADTOWERS_HAZARD_BRANCH_BEVEL)
    float2 p = uv - .5;
    float2 q = abs(p) - .5 + radius;
    float2 normal = q.x > 0 || q.y > 0
        ? normalize(max(q, .00001)) * sign(p)
        : (q.x > q.y ? float2(sign(p.x), 0) : float2(0, sign(p.y)));
#endif
    float grad = 1.13 - .36 * pow(saturate(1 - uv.y), 1.15);
    HAZARD_COLOR3 body = tint * grad * (surface.r * 2);
    float band = saturate((d + outline + bevel) / max(bevel, .001));
#if defined(MADTOWERS_HAZARD_BRANCH_BEVEL)
    // The flat face has no bevel contribution. Keep its inactive normal/power
    // calculations out of the fragment path (Adreno device regression).
    [branch] if (band > 0)
    {
        float2 p = uv - .5;
        float2 q = abs(p) - .5 + radius;
        float2 normal = q.x > 0 || q.y > 0
            ? normalize(max(q, .00001)) * sign(p)
            : (q.x > q.y ? float2(sign(p.x), 0) : float2(0, sign(p.y)));
#endif
    band = pow(saturate(band + (surface.g - .5) * band * (1 - band) * 2), .85);
    band *= saturate((-d - outline * .55) / max(outline * .45, .001));
    float top = saturate((normal.y - .25) * 2);
    float bottom = saturate((-normal.y - .25) * 2);
    float side = saturate((abs(normal.x) - .25) * 2) * (1 - top) * (1 - bottom);
    body *= 1 - .09 * band;
    body = lerp(body, (tint + (1 - tint) * .40) * grad * 1.04, .72 * band * top);
    body *= (1 - .26 * band * bottom) * (1 - .12 * band * side);
#if defined(MADTOWERS_HAZARD_BRANCH_BEVEL)
    }
#endif
    float lip = HazardSurface(uv + float2(0, 3.0 / 256)).a;
    body *= 1 - .40 * surface.a;
    body *= 1 + .22 * max(lip - surface.a, 0);
    return body;
}
HAZARD_COLOR3 HazardOutline(HAZARD_COLOR3 body, HAZARD_COLOR3 tint, float distance, float width)
{
    float luma = dot(tint, float3(.299, .587, .114));
    HAZARD_COLOR3 colour = lerp(tint, luma.xxx, .30) * .22;
    // Filter the inner outline edge across one screen pixel. A fixed UV-width
    // transition was subpixel at gameplay zoom. Keep the authored rim width;
    // this filters coverage, not Android-specific surface arithmetic artifacts.
    float aa = max(fwidth(distance) * .5, .002);
    return lerp(body, colour, smoothstep(-width - aa, -width + aa, distance));
}
#undef HAZARD_COLOR3
#undef HAZARD_COLOR4
#endif
