using UnityEngine;

/// <summary>
/// Ability/UI glyph sprites that grew up around the ability cards: the placeholder ability
/// glyph, the shared rounded-outline stroke, HUD bar/cube shapes, and the small decorative
/// diamond. (The old cut-corner card frame/plate sprites are gone - cards are now composed
/// from the shared rounded-panel vocabulary in AbilityCardView.)
/// </summary>
public static partial class RuntimeSprites
{
    // ---- neon card chrome ------------------------------------------------------------------
    // The ability cards' body + edge, drawn on a PADDED canvas so the neon bloom has room
    // OUTSIDE the card edge: a rounded-rect vertical gradient fill (accent-dark at the top,
    // near-black at the bottom) and a matching bright ring with an outer glow. Both share the
    // exact same geometry, so fill + ring always align; stretch both to the card rect expanded
    // by CardSpritePad per side. 9-sliced; every column carries the same vertical gradient, so
    // slicing never distorts it.
    public const float CardSpritePad = 16f;
    private const int CardTexSize = 128;
    private const float CardCornerRadius = 30f;

    private static readonly System.Collections.Generic.Dictionary<string, Sprite> _cardGradients =
        new System.Collections.Generic.Dictionary<string, Sprite>();

    // Signed distance to the card's rounded box (negative inside), centered coords.
    private static float CardBoxDistance(float cx, float cy, float half, float radius)
    {
        float qx = Mathf.Abs(cx) - (half - radius);
        float qy = Mathf.Abs(cy) - (half - radius);
        return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
               + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    public static Sprite CardGradient(Color top, Color bottom)
    {
        Color32 t32 = top; Color32 b32 = bottom;
        string key = $"{t32.r:x2}{t32.g:x2}{t32.b:x2}{t32.a:x2}:{b32.r:x2}{b32.g:x2}{b32.b:x2}{b32.a:x2}";
        if (_cardGradients.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = CardTexSize;
        const float half = S * 0.5f - CardSpritePad;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            float t = Mathf.Clamp01((y - CardSpritePad) / (S - 2f * CardSpritePad));
            Color row = Color.Lerp(bottom, top, t);
            for (int x = 0; x < S; x++)
            {
                float d = CardBoxDistance(x + 0.5f - S * 0.5f, y + 0.5f - S * 0.5f, half, CardCornerRadius);
                Color c = row;
                c.a *= Mathf.Clamp01(0.5f - d);
                tex.SetPixel(x, y, c);
            }
        }
        float border = CardSpritePad + CardCornerRadius + 2f;
        return _cardGradients[key] = Finish(tex, 100f, new Vector4(border, border, border, border));
    }

    private static Sprite _cardNeonRing;

    public static Sprite CardNeonRing()
    {
        if (_cardNeonRing != null) return _cardNeonRing;

        const int S = CardTexSize;
        const float half = S * 0.5f - CardSpritePad;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = CardBoxDistance(x + 0.5f - S * 0.5f, y + 0.5f - S * 0.5f, half, CardCornerRadius);

                // A crisp bright core stroke, an outer bloom forced to zero before the canvas
                // edge (an exponential alone leaves a visible cut-off square), and a faint
                // inner glow so the interior reads lit from its edges (the card BODY stays
                // near-black - the edge light is where the rarity colour lives).
                float core = Mathf.Clamp01(1.7f - Mathf.Abs(d) + 0.5f);
                float outer = d > 0f
                    ? Mathf.Exp(-d / 5f) * 0.5f * Mathf.Clamp01((CardSpritePad - 2f - d) / 6f)
                    : 0f;
                float inner = d < 0f ? Mathf.Exp(d / 5.5f) * 0.28f : 0f;

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(core + outer + inner)));
            }
        }
        float border = CardSpritePad + CardCornerRadius + 2f;
        return _cardNeonRing = Finish(tex, 100f, new Vector4(border, border, border, border));
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
