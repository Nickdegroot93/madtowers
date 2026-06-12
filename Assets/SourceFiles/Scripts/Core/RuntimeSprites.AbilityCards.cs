using UnityEngine;

/// <summary>
/// Ability-card chrome: the cut-corner sci-fi frame from the design mockups, drawn as
/// SDF sprites. Two layers per card - a fixed dark PLATE and a rarity-tinted FRAME
/// (bright border + soft outer glow) - so one white frame sprite serves all four
/// rarities via Image.color. Both are 9-sliceable; the chamfered corners live inside
/// the slice border so they stay crisp at any card size.
/// </summary>
public static partial class RuntimeSprites
{
    private const int CardTexSize = 96;
    private const float CardChamfer = 18f;   // 45-degree corner cut, px
    private const float CardBorderWidth = 1.6f; // half-width of the crisp line core
    private const float CardGlowWidth = 2.2f;   // hairline aura
    private const float CardGlowStrength = 0.16f;
    private const float CardGlowCutoff = 5.5f;  // aura is fully gone past this distance
    private const float CardSliceBorder = 30f;

    // Signed distance to a chamfered (octagon) box centered in the texture - the max of
    // the three half-plane distances (box sides + 45-degree corner planes). Not a true
    // Euclidean SDF, but exact along the borders we draw.
    private static float ChamferBoxDistance(float x, float y, float half, float chamfer)
    {
        float ax = Mathf.Abs(x);
        float ay = Mathf.Abs(y);
        float side = Mathf.Max(ax, ay) - half;
        float corner = (ax + ay - (2f * half - chamfer)) * 0.70710678f;
        return Mathf.Max(side, corner);
    }

    // ---- card frame (rarity-tinted border + glow) ----------------------------------------
    private static Sprite _cardFrame;

    public static Sprite CardFrame()
    {
        if (_cardFrame != null) return _cardFrame;

        const int S = CardTexSize;
        const float half = S * 0.5f - CardGlowWidth - 2f;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = ChamferBoxDistance(x + 0.5f - S * 0.5f, y + 0.5f - S * 0.5f, half, CardChamfer);

                // A crisp solid line (1px anti-alias ramp) with a hairline aura. The
                // aura is force-faded to zero before the cutoff: an exponential tail
                // reaching the texture edge reads as a square halo around the chamfered
                // corners - the EDGE is the design, not a feather.
                float border = Mathf.Clamp01(CardBorderWidth - Mathf.Abs(d) + 0.5f);
                float fade = Mathf.Clamp01(1f - Mathf.Abs(d) / CardGlowCutoff);
                float glow = Mathf.Exp(-Mathf.Abs(d) / CardGlowWidth) * CardGlowStrength * fade * fade;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(border + glow)));
            }
        }
        return _cardFrame = Finish(tex, 100f,
            new Vector4(CardSliceBorder, CardSliceBorder, CardSliceBorder, CardSliceBorder));
    }

    // ---- card plate (fixed dark fill behind the frame) -----------------------------------
    private static Sprite _cardPlate;

    public static Sprite CardPlate()
    {
        if (_cardPlate != null) return _cardPlate;

        const int S = CardTexSize;
        const float half = S * 0.5f - CardGlowWidth - 2f;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = ChamferBoxDistance(x + 0.5f - S * 0.5f, y + 0.5f - S * 0.5f, half, CardChamfer);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)));
            }
        }
        return _cardPlate = Finish(tex, 100f,
            new Vector4(CardSliceBorder, CardSliceBorder, CardSliceBorder, CardSliceBorder));
    }

    // ---- placeholder ability glyph --------------------------------------------------------
    // A four-point spark: diamond core + thin cross rays. Stands in for every ability
    // illustration until the real AI-generated icons land; tinted to the rarity color.
    private static Sprite _abilityGlyph;

    public static Sprite AbilityGlyph()
    {
        if (_abilityGlyph != null) return _abilityGlyph;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float cx = x + 0.5f - S * 0.5f;
                float cy = y + 0.5f - S * 0.5f;

                // Diamond core with concave edges (|x|^.7 metric reads as a spark).
                float spark = Mathf.Pow(Mathf.Abs(cx) / (S * 0.42f), 0.7f)
                            + Mathf.Pow(Mathf.Abs(cy) / (S * 0.42f), 0.7f);
                float core = Mathf.Clamp01((1f - spark) * 3f);

                // Soft halo behind it.
                float halo = Mathf.Exp(-(cx * cx + cy * cy) / (S * S * 0.045f)) * 0.35f;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(core + halo)));
            }
        }
        return _abilityGlyph = Finish(tex, S);
    }

    // ---- card header band ---------------------------------------------------------------
    // The top section of the card as its own region (the mockups' header is a COLOR
    // AREA, not a divider line): chamfered top corners, straight bottom edge, subtle
    // baked vertical gradient (brighter at the top). White; tint per rarity.
    private static Sprite _cardHeaderBand;

    public static Sprite CardHeaderBand()
    {
        if (_cardHeaderBand != null) return _cardHeaderBand;

        const int W = 96, H = 64;
        const float half = 96 * 0.5f - CardGlowWidth - 2f;
        Texture2D tex = NewTexture(W, H);
        for (int y = 0; y < H; y++)
        {
            // Rows map to the TOP band of the full 96x96 chamfer box.
            float cy = (y + 32f) + 0.5f - 48f;
            float gradient = Mathf.Lerp(0.30f, 0.60f, (float)y / (H - 1));
            for (int x = 0; x < W; x++)
            {
                float d = ChamferBoxDistance(x + 0.5f - W * 0.5f, cy, half, CardChamfer);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d) * gradient));
            }
        }
        return _cardHeaderBand = Finish(tex, 100f, new Vector4(30f, 6f, 30f, 30f));
    }

    // ---- rounded outline (Details button border) -------------------------------------------
    private static Sprite _roundedOutline;

    public static Sprite RoundedOutline()
    {
        if (_roundedOutline != null) return _roundedOutline;

        // Identical geometry to RoundedPanel (radius 14, no inset): the stroke sits
        // exactly on the fill's edge, so panel+outline pairs read as ONE bordered shape
        // instead of two offset edges.
        const int S = 64;
        const float R = 14f;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - S * 0.5f) - (S * 0.5f - R);
                float qy = Mathf.Abs(y + 0.5f - S * 0.5f) - (S * 0.5f - R);
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                          + Mathf.Min(Mathf.Max(qx, qy), 0f) - R;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1.2f - Mathf.Abs(d))));
            }
        }
        return _roundedOutline = Finish(tex, 100f, new Vector4(24f, 24f, 24f, 24f));
    }

    // ---- ability type glyphs (badge plate icons) --------------------------------------------
    // Small white icons, tinted at runtime: infinity = passive, ring-with-bar = one-time,
    // flask = consumable. Instant reuses the spark (AbilityGlyph at badge size).

    private static Sprite _glyphInfinity;

    public static Sprite GlyphInfinity()
    {
        if (_glyphInfinity != null) return _glyphInfinity;

        const int W = 64, H = 40;
        const float R = 10f, Spread = 9f, Stroke = 3.2f;
        Texture2D tex = NewTexture(W, H);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float cx = x + 0.5f - W * 0.5f;
                float cy = y + 0.5f - H * 0.5f;
                float left = Mathf.Abs(new Vector2(cx + Spread, cy).magnitude - R);
                float right = Mathf.Abs(new Vector2(cx - Spread, cy).magnitude - R);
                float d = Mathf.Min(left, right);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(Stroke - d)));
            }
        }
        return _glyphInfinity = Finish(tex, 64f);
    }

    private static Sprite _glyphOneShot;

    public static Sprite GlyphOneShot()
    {
        if (_glyphOneShot != null) return _glyphOneShot;

        const int S = 48;
        const float R = 16f, Stroke = 2.8f;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float cx = x + 0.5f - S * 0.5f;
                float cy = y + 0.5f - S * 0.5f;
                float ring = Mathf.Clamp01(Stroke - Mathf.Abs(new Vector2(cx, cy).magnitude - R));
                // The "1": a vertical bar with a small flag at the top left.
                float bar = (Mathf.Abs(cx) < 2.2f && Mathf.Abs(cy) < 9f) ? 1f : 0f;
                float flag = (cx > -6f && cx < 0f && cy > 4.5f && cy < 9f) ? 1f : 0f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(ring + bar + flag)));
            }
        }
        return _glyphOneShot = Finish(tex, 48f);
    }

    private static Sprite _glyphFlask;

    public static Sprite GlyphFlask()
    {
        if (_glyphFlask != null) return _glyphFlask;

        const int S = 48;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float cx = x + 0.5f - S * 0.5f;
                float cy = y + 0.5f - S * 0.5f;

                // Erlenmeyer silhouette: lip, neck, conical body.
                bool lip = Mathf.Abs(cx) < 7f && cy > 16f && cy < 20f;
                bool neck = Mathf.Abs(cx) < 4f && cy >= 4f && cy <= 17f;
                float t = Mathf.InverseLerp(4f, -18f, cy); // 0 at neck base, 1 at flask bottom
                bool body = cy < 4f && cy > -18f && Mathf.Abs(cx) < Mathf.Lerp(4f, 16f, t);
                bool baseLine = cy <= -16f && cy > -19f && Mathf.Abs(cx) < 15f;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, (lip || neck || body || baseLine) ? 1f : 0f));
            }
        }
        return _glyphFlask = Finish(tex, 48f);
    }

    // ---- half-rounded panel (HUD bar segments) -------------------------------------------
    // Rounded corners on the LEFT side only; the right edge cuts off square. The HUD
    // bar's segments use it so they read as one bar passing BEHIND the next-card (their
    // square inner edges sit flush under its border) while no geometry actually renders
    // behind the translucent card. The right segment is this sprite rotated 180 degrees.
    private static Sprite _roundedPanelSquareRight;

    public static Sprite RoundedPanelSquareRight()
    {
        if (_roundedPanelSquareRight != null) return _roundedPanelSquareRight;

        const int S = 64;
        const float R = 14f;
        // The rounded-rect's box extends past the right texture edge by R, so only the
        // left corners ever round; the right edge is a clean square cut.
        const float hw = (S + R) * 0.5f;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - hw) - (hw - R);
                float qy = Mathf.Abs(y + 0.5f - S * 0.5f) - (S * 0.5f - R);
                float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                          + Mathf.Min(Mathf.Max(qx, qy), 0f) - R;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)));
            }
        }
        return _roundedPanelSquareRight = Finish(tex, 100f, new Vector4(24f, 24f, 2f, 24f));
    }

    // ---- HUD cube glyph -----------------------------------------------------------------
    // Isometric cube for the "blocks placed" stat: three faces at different alphas so a
    // single white sprite reads as shaded once tinted.
    private static Sprite _cubeGlyph;

    public static Sprite CubeGlyph()
    {
        if (_cubeGlyph != null) return _cubeGlyph;

        const int S = 64;
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f + 2f);
        const float W = 20f;  // half-width of the cube silhouette
        const float T = 11f;  // top-face half-height (isometric squash)
        const float H = 22f;  // side-face height

        Vector2 top = c + new Vector2(0f, T + H * 0.5f);
        Vector2 right = c + new Vector2(W, H * 0.5f);
        Vector2 left = c + new Vector2(-W, H * 0.5f);
        Vector2 mid = c + new Vector2(0f, H * 0.5f - T);
        Vector2 bottomL = left + new Vector2(0f, -H);
        Vector2 bottomM = mid + new Vector2(0f, -H);
        Vector2 bottomR = right + new Vector2(0f, -H);

        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                float a = 0f;
                if (PointInQuad(point, left, top, right, mid)) a = 1f;            // top face
                else if (PointInQuad(point, left, mid, bottomM, bottomL)) a = 0.72f;  // left face
                else if (PointInQuad(point, mid, right, bottomR, bottomM)) a = 0.5f;  // right face
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        return _cubeGlyph = Finish(tex, S);
    }

    private static bool PointInQuad(Vector2 p, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        return PointInTriangle(p, a, b, c) || PointInTriangle(p, a, c, d);
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s1 = Cross(b - a, p - a);
        float s2 = Cross(c - b, p - b);
        float s3 = Cross(a - c, p - c);
        return (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    // ---- small decorative diamond (header flourishes) -------------------------------------
    private static Sprite _diamond;

    public static Sprite Diamond()
    {
        if (_diamond != null) return _diamond;

        const int S = 24;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Abs(x + 0.5f - S * 0.5f) + Mathf.Abs(y + 0.5f - S * 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(S * 0.4f - d)));
            }
        }
        return _diamond = Finish(tex, S);
    }
}
