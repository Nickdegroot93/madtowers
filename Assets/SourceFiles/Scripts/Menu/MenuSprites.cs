using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Asset-free placeholder art for the chapter menu. Real chapter videos and level thumbnails
/// override these through ChapterDefinition/LevelDefinition, but the layout stays alive before
/// those assets exist.
/// </summary>
public static class MenuSprites
{
    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    public static Sprite Background(Color top, Color bottom, Color accent)
    {
        string key = $"bg:{Key(top)}:{Key(bottom)}:{Key(accent)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int W = 360, H = 640;
        Texture2D tex = NewTexture(W, H);
        for (int y = 0; y < H; y++)
        {
            float t = (float)y / (H - 1);
            Color sky = Color.Lerp(bottom, top, Mathf.SmoothStep(0f, 1f, t));
            for (int x = 0; x < W; x++)
            {
                float u = (float)x / (W - 1);
                float glow = Mathf.Exp(-Mathf.Pow((u - 0.78f) * 3.4f, 2f) - Mathf.Pow((t - 0.42f) * 4.5f, 2f));
                Color c = Color.Lerp(sky, accent, glow * 0.22f);

                float duneA = 0.24f + 0.10f * Mathf.Sin(u * 7.2f + 0.6f);
                float duneB = 0.14f + 0.08f * Mathf.Sin(u * 5.1f + 1.9f);
                if (t < duneA) c = Color.Lerp(c, new Color(0.42f, 0.22f, 0.08f, 1f), 0.48f);
                if (t < duneB) c = Color.Lerp(c, new Color(0.18f, 0.11f, 0.07f, 1f), 0.72f);

                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, 100f);
    }

    public static Sprite LevelThumbnail(int seed, Color accent, Color secondary)
    {
        string key = $"level:{seed}:{Key(accent)}:{Key(secondary)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 160;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            float t = (float)y / (S - 1);
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / (S - 1);
                float vignette = Mathf.Clamp01(Vector2.Distance(new Vector2(u, t), new Vector2(0.5f, 0.48f)) * 1.7f);
                Color c = Color.Lerp(new Color(0.09f, 0.08f, 0.07f, 1f), secondary, 0.35f + t * 0.25f);
                c = Color.Lerp(c, Color.black, vignette * 0.32f);
                tex.SetPixel(x, y, c);
            }
        }

        int[][] patterns =
        {
            new[] { 0, 0, 1, 0, 2, 0, 2, 1, 3, 1, 3, 2 },
            new[] { 0, 0, 1, 0, 2, 0, 1, 1, 1, 2, 2, 2 },
            new[] { 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3 },
            new[] { 0, 0, 1, 0, 2, 0, 3, 0, 2, 1, 3, 1 },
            new[] { 0, 0, 1, 0, 1, 1, 2, 1, 2, 2, 3, 2 },
        };
        int[] pattern = patterns[Mathf.Abs(seed) % patterns.Length];
        const int Cell = 27;
        int ox = 28;
        int oy = 28;
        for (int i = 0; i < pattern.Length; i += 2)
        {
            int bx = ox + pattern[i] * Cell;
            int by = oy + pattern[i + 1] * Cell;
            Color fill = Color.Lerp(accent, Color.white, 0.12f + (i % 4) * 0.05f);
            DrawRect(tex, bx, by, Cell - 2, Cell - 2, fill);
            DrawRectOutline(tex, bx, by, Cell - 2, Cell - 2, Color.Lerp(Color.black, accent, 0.45f));
        }

        return Cache[key] = Finish(tex, S);
    }

    public static Sprite HexButton(Color top, Color bottom)
    {
        string key = $"hex:{Key(top)}:{Key(bottom)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        // The concepts' centre nav button: a point-top hexagon a little TALLER than wide, with
        // gently ROUNDED corners, sitting on a slightly wider darker back plate that peeks out
        // as a thin vertical seam along the flat left/right sides (barely at the points), the
        // whole thing wrapped in a fine light outline.
        const int W = 160, H = 192;
        const float rOuter = 9f;   // corner rounding
        const float rimSide = 7f;  // back plate visible on the flat sides...
        const float rimTop = 3f;   // ...and only barely at the top/bottom points
        Texture2D tex = NewTexture(W, H);
        Vector2 c0 = new Vector2(W * 0.5f, H * 0.5f);
        float wx = W * 0.5f - 5f, hy = H * 0.5f - 4f;

        Color rim = Color.Lerp(bottom, Color.black, 0.30f);
        Color outline = Color.Lerp(top, Color.white, 0.45f);

        // Signed distance to a rounded point-top hexagon (half extents halfW x halfH). By
        // symmetry only the abs-quadrant matters: the boundary there is the vertical flat side
        // up to the shoulder, then the slant to the top point.
        float HexDist(Vector2 p, float halfW, float halfH, float round)
        {
            float kw = halfW - round, kh = halfH - round;
            float ky = kh * 0.52f; // shoulder height (top of the flat side)
            Vector2 a = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            float dist = Mathf.Min(
                DistToSegment(a, new Vector2(kw, 0f), new Vector2(kw, ky)),
                DistToSegment(a, new Vector2(kw, ky), new Vector2(0f, kh)));
            bool inside = a.x <= kw && a.y <= ky + (kw - a.x) * (kh - ky) / kw;
            return (inside ? -dist : dist) - round;
        }

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - c0;
                float dOut = HexDist(p, wx, hy, rOuter);
                float aOut = Mathf.Clamp01(0.5f - dOut);
                if (aOut <= 0f) { tex.SetPixel(x, y, Color.clear); continue; }

                float dIn = HexDist(p, wx - rimSide, hy - rimTop, rOuter * 0.8f);
                float aIn = Mathf.Clamp01(0.5f - dIn);

                float v = Mathf.Clamp01((p.y / hy + 1f) * 0.5f);
                Color face = Color.Lerp(bottom, top, v);
                // Subtle sheen just inside the face's upper edge, so the front plate reads convex.
                face = Color.Lerp(face, Color.white, Mathf.Clamp01(1f - Mathf.Abs(dIn + 2f) / 3f) * 0.10f * v);
                Color c = Color.Lerp(rim, face, aIn);

                // Fine light outline hugging the outer edge.
                float edge = Mathf.Clamp01(1.4f - Mathf.Abs(dOut + 0.7f));
                c = Color.Lerp(c, outline, edge * 0.85f);
                c.a = aOut;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, H);
    }

    public static Sprite PointHexBadge(Color top, Color bottom, Color border)
    {
        string key = $"point-hex:{Key(top)}:{Key(bottom)}:{Key(border)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 192;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = ((x + 0.5f) / S) * 2f - 1f;
                float v = ((y + 0.5f) / S) * 2f - 1f;
                float au = Mathf.Abs(u);
                float av = Mathf.Abs(v);

                // Point-up hexagon: flat sides, small peak at top/bottom.
                float d = Mathf.Max(au, av * 0.8660254f + au * 0.5f) - 0.74f;
                float edge = Mathf.Clamp01(0.5f - d * 70f);
                float stroke = Mathf.Clamp01(1.05f - Mathf.Abs(d) * 95f);

                Color fill = Color.Lerp(bottom, top, (v + 1f) * 0.5f);
                Color c = Color.Lerp(fill, border, stroke * 0.72f);
                c.a = Mathf.Max(fill.a * edge, border.a * stroke) * edge;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    public static Sprite DiamondBadge(Color fill, Color border)
    {
        string key = $"diamond:{Key(fill)}:{Key(border)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 128;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = ((x + 0.5f) / S) * 2f - 1f;
                float v = ((y + 0.5f) / S) * 2f - 1f;
                float d = Mathf.Abs(u) + Mathf.Abs(v) - 0.80f;
                float inside = Mathf.Clamp01(0.5f - d * 80f);
                // ~3.5px stroke in the 128px source so it survives the downscale to ~62px as a
                // clean, crisp ~1.6px line (the old *74 gave a sub-pixel, near-invisible border).
                float stroke = Mathf.Clamp01(1.05f - Mathf.Abs(d) * 38f);
                Color c = Color.Lerp(fill, border, stroke);
                // Border alpha must NOT be re-multiplied by the fill mask. The old trailing
                // "* inside" halved every border (inside == 0.5 at the rim) and clipped it to the
                // fill edge - that is why the diamond/circle borders read as missing. The stroke
                // band already carries its own AA on both sides.
                c.a = Mathf.Max(fill.a * inside, border.a * stroke);
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A crisp diamond OUTLINE: a bright thin stroke on the diamond edge, inset from the sprite
    // border. The "line around the diamond" for the active timeline node (its soft glow halo is
    // a separate UIEffect-blurred layer behind it).
    public static Sprite DiamondRing(Color color)
    {
        string key = $"diamond-ring:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 160;
        const float Ring = 0.46f; // diamond edge radius
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = ((x + 0.5f) / S) * 2f - 1f;
                float v = ((y + 0.5f) / S) * 2f - 1f;
                float d = Mathf.Abs(u) + Mathf.Abs(v) - Ring; // signed distance to the diamond edge
                float line = Mathf.Clamp01(1f - Mathf.Abs(d) / 0.045f);
                Color c = color;
                c.a = color.a * line;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    public static Sprite CircleBadge(Color fill, Color border)
    {
        string key = $"circle:{Key(fill)}:{Key(border)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 128;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = ((x + 0.5f) / S) * 2f - 1f;
                float v = ((y + 0.5f) / S) * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v) - 0.80f;
                float inside = Mathf.Clamp01(0.5f - d * 80f);
                // ~3.5px stroke in the 128px source so it survives the downscale to ~62px as a
                // clean, crisp ~1.6px line (the old *74 gave a sub-pixel, near-invisible border).
                float stroke = Mathf.Clamp01(1.05f - Mathf.Abs(d) * 38f);
                Color c = Color.Lerp(fill, border, stroke);
                c.a = Mathf.Max(fill.a * inside, border.a * stroke);
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    public static Sprite TrianglePlay()
    {
        const string key = "play-triangle";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        Vector2 a = new Vector2(32f, 22f);
        Vector2 b = new Vector2(32f, 74f);
        Vector2 c = new Vector2(72f, 48f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float inside = PointInTriangle(p, a, b, c) ? 1f : 0f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, inside));
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A crisp tick mark drawn as the union of two anti-aliased strokes (the short down-stroke
    // and the long up-stroke). Tinted by `color`; the font's U+2713 glyph renders as tofu in our
    // SDF font, so the completed badge uses this sprite instead.
    /// <summary>Small goal-type glyphs for the level cards: "cube" (block count), "waves"
    /// (puzzle waves), "mountain" (height challenge), "timer" (timed goals), "flood" (a
    /// wave with an arrow climbing out of it - rising water, build upward), "airtight" (a
    /// sealed container with a trapped bubble), "void" (a forbidden rectangle, slashed).
    /// Drawn as thin strokes in the given colour, matching the concepts' inline marks.</summary>
    public static Sprite GoalGlyph(string kind, Color color)
    {
        string key = $"goal:{kind}:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        float half = 0.055f * S;

        System.Func<Vector2, float> dist;
        switch (kind)
        {
            case "waves":
            {
                dist = p2 =>
                {
                    float best = float.MaxValue;
                    for (int row = 0; row < 3; row++)
                    {
                        float baseY = (0.26f + 0.24f * row) * S;
                        float wave = baseY + Mathf.Sin(p2.x / S * Mathf.PI * 2.4f + row * 0.8f) * 0.055f * S;
                        if (p2.x < 0.12f * S || p2.x > 0.88f * S) continue;
                        best = Mathf.Min(best, Mathf.Abs(p2.y - wave));
                    }
                    return best;
                };
                break;
            }
            case "mountain":
            {
                Vector2 a = new Vector2(0.08f * S, 0.28f * S);
                Vector2 b = new Vector2(0.38f * S, 0.74f * S);
                Vector2 c = new Vector2(0.54f * S, 0.50f * S);
                Vector2 d = new Vector2(0.70f * S, 0.80f * S);
                Vector2 e = new Vector2(0.92f * S, 0.28f * S);
                dist = p2 => Mathf.Min(
                    Mathf.Min(DistToSegment(p2, a, b), DistToSegment(p2, b, c)),
                    Mathf.Min(DistToSegment(p2, c, d), DistToSegment(p2, d, e)));
                break;
            }
            case "flood":
            {
                // One rolling wave low in the frame, an arrow climbing out of it: the water
                // is rising - get above it. (The flood level type, RisingFloodModifier.)
                Vector2 tip = new Vector2(0.50f * S, 0.82f * S);
                Vector2 stemBase = new Vector2(0.50f * S, 0.36f * S);
                Vector2 headL = new Vector2(0.34f * S, 0.64f * S);
                Vector2 headR = new Vector2(0.66f * S, 0.64f * S);
                dist = p2 =>
                {
                    float best = float.MaxValue;
                    if (p2.x >= 0.08f * S && p2.x <= 0.92f * S)
                    {
                        float wave = 0.24f * S + Mathf.Sin(p2.x / S * Mathf.PI * 2.4f + 0.6f) * 0.06f * S;
                        best = Mathf.Abs(p2.y - wave);
                    }
                    best = Mathf.Min(best, DistToSegment(p2, stemBase, tip));
                    best = Mathf.Min(best, DistToSegment(p2, tip, headL));
                    best = Mathf.Min(best, DistToSegment(p2, tip, headR));
                    return best;
                };
                break;
            }
            case "airtight":
            {
                // A sealed container with one trapped bubble - the thing Airtight forbids.
                Vector2 bl = new Vector2(0.20f * S, 0.18f * S);
                Vector2 br = new Vector2(0.80f * S, 0.18f * S);
                Vector2 tr = new Vector2(0.80f * S, 0.78f * S);
                Vector2 tl = new Vector2(0.20f * S, 0.78f * S);
                // Small and off-centre: a centred circle read as a camera icon, not a bubble.
                Vector2 bub = new Vector2(0.42f * S, 0.40f * S);
                float bubR = 0.11f * S;
                dist = p2 => Mathf.Min(
                    Mathf.Min(Mathf.Min(DistToSegment(p2, bl, br), DistToSegment(p2, br, tr)),
                              Mathf.Min(DistToSegment(p2, tr, tl), DistToSegment(p2, tl, bl))),
                    Mathf.Abs(Vector2.Distance(p2, bub) - bubR));
                break;
            }
            case "void":
            {
                // A wide forbidden rectangle with a slash - the sky zone you must not touch.
                Vector2 bl = new Vector2(0.14f * S, 0.30f * S);
                Vector2 br = new Vector2(0.86f * S, 0.30f * S);
                Vector2 tr = new Vector2(0.86f * S, 0.70f * S);
                Vector2 tl = new Vector2(0.14f * S, 0.70f * S);
                dist = p2 => Mathf.Min(
                    Mathf.Min(Mathf.Min(DistToSegment(p2, bl, br), DistToSegment(p2, br, tr)),
                              Mathf.Min(DistToSegment(p2, tr, tl), DistToSegment(p2, tl, bl))),
                    DistToSegment(p2, new Vector2(0.24f * S, 0.38f * S), new Vector2(0.76f * S, 0.62f * S)));
                break;
            }
            case "timer":
            {
                Vector2 center = new Vector2(0.5f * S, 0.44f * S);
                float radius = 0.30f * S;
                Vector2 handEnd = center + new Vector2(0.16f * S, 0.16f * S);
                Vector2 stemA = new Vector2(0.5f * S, 0.80f * S);
                Vector2 stemB = new Vector2(0.5f * S, 0.90f * S);
                dist = p2 => Mathf.Min(
                    Mathf.Abs(Vector2.Distance(p2, center) - radius),
                    Mathf.Min(DistToSegment(p2, center, handEnd), DistToSegment(p2, stemA, stemB)));
                break;
            }
            default: // cube: isometric outline - hexagon silhouette + Y-shaped inner edges
            {
                Vector2 centerC = new Vector2(0.5f * S, 0.5f * S);
                float r = 0.36f * S;
                Vector2[] v = new Vector2[6];
                for (int k = 0; k < 6; k++)
                {
                    float ang = (30f + 60f * k) * Mathf.Deg2Rad;
                    v[k] = centerC + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                }
                dist = p2 =>
                {
                    float best = float.MaxValue;
                    for (int k = 0; k < 6; k++) best = Mathf.Min(best, DistToSegment(p2, v[k], v[(k + 1) % 6]));
                    best = Mathf.Min(best, DistToSegment(p2, centerC, v[1]));
                    best = Mathf.Min(best, DistToSegment(p2, centerC, v[3]));
                    best = Mathf.Min(best, DistToSegment(p2, centerC, v[5]));
                    return best;
                };
                break;
            }
        }

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p2 = new Vector2(x + 0.5f, y + 0.5f);
                float a = Mathf.Clamp01(half - dist(p2) + 0.5f);
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    /// <summary>A 9-sliced soft glow RING for highlighting a rounded card: alpha peaks at the
    /// rounded-rect edge and falls off smoothly outward (and quickly inward), so tinting it and
    /// stretching it slightly past a card yields a real halo - not a solid slab. White; tint via
    /// the Image colour.</summary>
    public static Sprite GlowFrame()
    {
        const string key = "glowframe";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 144;
        const float radius = 26f;
        Texture2D tex = NewTexture(S, S);
        Vector2 center = new Vector2(S * 0.5f, S * 0.5f);
        // The rect edge sits well inside the texture so the outward falloff has room to breathe.
        Vector2 half = new Vector2(S * 0.5f - 26f, S * 0.5f - 26f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = RoundedBoxDist(p, center, half, radius);
                // Outward: a tight bloom (~10 px) multiplied by a window that reaches EXACTLY
                // zero before the texture edge - an exponential alone still holds ~6% alpha at
                // the sprite rect, which renders as a hard cut-off rectangle around the halo.
                float a = d >= 0f
                    ? Mathf.Exp(-d / 5f) * Mathf.Clamp01((24f - d) / 10f)
                    : Mathf.Exp(d / 2.5f);        // hugs the edge inward, never floods the card
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply(false, true);
        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(60f, 60f, 60f, 60f));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return Cache[key] = sprite;
    }

    public static Sprite CheckMark(Color color)
    {
        string key = $"check:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        // Three points of the tick (pixel space, y up): left start, bottom vertex, top-right end.
        Vector2 p0 = new Vector2(0.22f * S, 0.50f * S);
        Vector2 p1 = new Vector2(0.42f * S, 0.30f * S);
        Vector2 p2 = new Vector2(0.78f * S, 0.68f * S);
        float half = 0.058f * S; // half stroke width
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(DistToSegment(p, p0, p1), DistToSegment(p, p1, p2));
                float a = Mathf.Clamp01(half - d + 0.5f);
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A right-pointing chevron drawn as two round-capped strokes meeting at the tip - a cleaner,
    // lighter ">" than the font glyph for the action badge.
    public static Sprite Chevron(Color color)
    {
        string key = $"chevron:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        // Nudged right of centre so the optical weight sits centred in a circular badge.
        Vector2 top = new Vector2(0.40f * S, 0.72f * S);
        Vector2 tip = new Vector2(0.64f * S, 0.50f * S);
        Vector2 bot = new Vector2(0.40f * S, 0.28f * S);
        float half = 0.050f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(DistToSegment(p, top, tip), DistToSegment(p, tip, bot));
                Color c = color;
                c.a = color.a * Mathf.Clamp01(half - d + 0.5f);
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A padlock: a filled rounded-rect body with a round keyhole carved out, capped by a hollow
    // semicircular shackle. Replaces the literal word "LOCK" on locked level/chapter badges.
    public static Sprite Lock(Color color)
    {
        string key = $"lock:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        Vector2 bodyCenter = new Vector2(0.5f * S, 0.40f * S);
        Vector2 bodyHalf = new Vector2(0.21f * S, 0.20f * S);
        float bodyRadius = 0.06f * S;
        Vector2 shackleCenter = new Vector2(0.5f * S, 0.60f * S);
        float shackleOuter = 0.17f * S;
        float shackleInner = 0.10f * S;
        Vector2 keyholeCenter = new Vector2(0.5f * S, 0.42f * S);
        float keyholeRadius = 0.05f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);

                float bodyD = RoundedBoxDist(p, bodyCenter, bodyHalf, bodyRadius);
                float bodyA = Mathf.Clamp01(0.5f - bodyD);

                // Hollow shackle: the band between the two radii, upper half only so it reads as
                // an inverted-U sitting on the body (the body hides the flat cut at its base).
                float r = Vector2.Distance(p, shackleCenter);
                float ringD = Mathf.Max(r - shackleOuter, shackleInner - r);
                float ringA = p.y >= shackleCenter.y ? Mathf.Clamp01(0.5f - ringD) : 0f;

                float a = Mathf.Max(bodyA, ringA);

                // Carve the keyhole back out of the body.
                float keyholeA = Mathf.Clamp01(0.5f - (Vector2.Distance(p, keyholeCenter) - keyholeRadius));
                a = Mathf.Min(a, 1f - keyholeA);

                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // ---- Bottom-nav glyphs ------------------------------------------------------------------
    // Clean line/solid icons for the menu's bottom navigation, drawn procedurally so they share
    // the menu's look and scale crisply. `color` tints the whole glyph (alpha respected).
    // Stroke icons use a ~4.3px source stroke that downsamples to a clean ~2px line at nav size.

    // Outline alpha for a signed distance `d` (negative inside) at half-stroke `half`.
    private static float StrokeAlpha(float d, float half) => Mathf.Clamp01(half - Mathf.Abs(d) + 0.5f);

    public static Sprite NavBag(Color color)
    {
        string key = $"navbag:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        Vector2 bodyCenter = new Vector2(0.5f * S, 0.42f * S);
        Vector2 bodyHalf = new Vector2(0.23f * S, 0.24f * S);
        float bodyRadius = 0.07f * S;
        Vector2 handleCenter = new Vector2(0.5f * S, 0.64f * S);
        float handleRadius = 0.135f * S;
        float half = 0.045f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float bodyA = StrokeAlpha(RoundedBoxDist(p, bodyCenter, bodyHalf, bodyRadius), half);
                float handleA = p.y >= handleCenter.y
                    ? StrokeAlpha(Vector2.Distance(p, handleCenter) - handleRadius, half)
                    : 0f;
                Color c = color;
                c.a = color.a * Mathf.Max(bodyA, handleA);
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // Two offset rounded-square outlines - a "layers / stack" glyph for Chapters.
    public static Sprite NavLayers(Color color)
    {
        string key = $"navlayers:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        Vector2 back = new Vector2(0.58f * S, 0.58f * S);
        Vector2 front = new Vector2(0.42f * S, 0.42f * S);
        Vector2 sq = new Vector2(0.22f * S, 0.22f * S);
        float radius = 0.05f * S;
        float half = 0.045f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = Mathf.Max(
                    StrokeAlpha(RoundedBoxDist(p, back, sq, radius), half),
                    StrokeAlpha(RoundedBoxDist(p, front, sq, radius), half));
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // Filled house silhouette (roof + body) with a carved door - the Home glyph.
    public static Sprite NavHouse(Color color)
    {
        string key = $"navhouse:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        Vector2 bodyCenter = new Vector2(0.5f * S, 0.34f * S);
        Vector2 bodyHalf = new Vector2(0.23f * S, 0.17f * S);
        float bodyRadius = 0.03f * S;
        Vector2 apex = new Vector2(0.5f * S, 0.82f * S);
        Vector2 eaveL = new Vector2(0.14f * S, 0.50f * S);
        Vector2 eaveR = new Vector2(0.86f * S, 0.50f * S);
        Vector2 doorCenter = new Vector2(0.5f * S, 0.26f * S);
        Vector2 doorHalf = new Vector2(0.075f * S, 0.135f * S);
        float doorRadius = 0.02f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float bodyA = Mathf.Clamp01(0.5f - RoundedBoxDist(p, bodyCenter, bodyHalf, bodyRadius));
                float roofA = PointInTriangle(p, apex, eaveL, eaveR) ? 1f : 0f;
                float a = Mathf.Max(bodyA, roofA);
                float doorA = Mathf.Clamp01(0.5f - RoundedBoxDist(p, doorCenter, doorHalf, doorRadius));
                a = Mathf.Min(a, 1f - doorA);
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // 2x2 rounded-square outlines - a grid/vault glyph.
    public static Sprite NavGrid(Color color)
    {
        string key = $"navgrid:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        Vector2 sq = new Vector2(0.135f * S, 0.135f * S);
        float radius = 0.035f * S;
        float half = 0.042f * S;
        Vector2[] centers =
        {
            new Vector2(0.33f * S, 0.67f * S), new Vector2(0.67f * S, 0.67f * S),
            new Vector2(0.33f * S, 0.33f * S), new Vector2(0.67f * S, 0.33f * S),
        };
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = 0f;
                for (int i = 0; i < centers.Length; i++)
                    a = Mathf.Max(a, StrokeAlpha(RoundedBoxDist(p, centers[i], sq, radius), half));
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A cog: filled toothed ring with a center hole - the Settings glyph.
    public static Sprite NavGear(Color color)
    {
        string key = $"navgear:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        Vector2 center = new Vector2(0.5f * S, 0.5f * S);
        float baseR = 0.27f * S;
        float toothH = 0.08f * S;
        float holeR = 0.13f * S;
        const int teeth = 8;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x + 0.5f - center.x;
                float dy = y + 0.5f - center.y;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float theta = Mathf.Atan2(dy, dx);
                // Square-ish radial tooth wave: full radius on the tooth, base in the valley.
                float t = Mathf.Clamp01(Mathf.Cos(theta * teeth) * 4f + 0.5f);
                float outerR = baseR + toothH * t;
                float outerA = Mathf.Clamp01(0.5f - (r - outerR));
                float innerA = Mathf.Clamp01(0.5f - (holeR - r));
                Color c = color;
                c.a = color.a * Mathf.Min(outerA, innerA);
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // ---- Settings-rail glyphs ---------------------------------------------------------------
    // Line/solid icons for the settings tab rail, same procedural look as the bottom-nav glyphs.
    // Placeholder set - final per-tab art is a design pass; see SETTINGS.md. `color` tints the
    // whole glyph (alpha respected).

    // Three horizontal track lines, each with a draggable knob - the UI / Controls glyph.
    public static Sprite Sliders(Color color)
    {
        string key = $"sliders:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        float half = 0.045f * S;
        float lx = 0.20f * S, rx = 0.80f * S, knobR = 0.085f * S;
        float[] rowsY = { 0.70f, 0.50f, 0.30f };
        float[] knobX = { 0.64f, 0.36f, 0.58f };
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = 0f;
                for (int i = 0; i < rowsY.Length; i++)
                {
                    float ly = rowsY[i] * S;
                    a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, new Vector2(lx, ly), new Vector2(rx, ly)), half));
                    float kd = Vector2.Distance(p, new Vector2(knobX[i] * S, ly)) - knobR;
                    a = Mathf.Max(a, Mathf.Clamp01(0.5f - kd));
                }
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A monitor: rounded-rect screen outline on a short stand - the Graphics glyph.
    public static Sprite Monitor(Color color)
    {
        string key = $"monitor:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        float half = 0.045f * S;
        Vector2 screenC = new Vector2(0.5f * S, 0.57f * S);
        Vector2 screenH = new Vector2(0.30f * S, 0.20f * S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = StrokeAlpha(RoundedBoxDist(p, screenC, screenH, 0.05f * S), half);
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, new Vector2(0.5f * S, 0.37f * S), new Vector2(0.5f * S, 0.27f * S)), half));
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, new Vector2(0.36f * S, 0.25f * S), new Vector2(0.64f * S, 0.25f * S)), half));
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // Four solid bars of varying height - the Sound & Haptics glyph (equalizer).
    public static Sprite Equalizer(Color color)
    {
        string key = $"equalizer:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        float[] xs = { 0.26f, 0.42f, 0.58f, 0.74f };
        float[] hs = { 0.34f, 0.56f, 0.42f, 0.24f };
        float barHalf = 0.05f * S, bottom = 0.26f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = 0f;
                for (int i = 0; i < xs.Length; i++)
                {
                    float h = hs[i] * S;
                    Vector2 center = new Vector2(xs[i] * S, bottom + h * 0.5f);
                    a = Mathf.Max(a, Mathf.Clamp01(0.5f - RoundedBoxDist(p, center, new Vector2(barHalf, h * 0.5f), 0.02f * S)));
                }
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A bell: domed body, flared rim and clapper - the Notifications glyph.
    public static Sprite Bell(Color color)
    {
        string key = $"bell:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        Vector2 domeC = new Vector2(0.5f * S, 0.52f * S);
        float domeR = 0.19f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = p.y >= domeC.y ? Mathf.Clamp01(0.5f - (Vector2.Distance(p, domeC) - domeR)) : 0f;
                a = Mathf.Max(a, Mathf.Clamp01(0.5f - RoundedBoxDist(p, new Vector2(0.5f * S, 0.44f * S), new Vector2(0.19f * S, 0.14f * S), 0.04f * S)));
                a = Mathf.Max(a, Mathf.Clamp01(0.5f - RoundedBoxDist(p, new Vector2(0.5f * S, 0.30f * S), new Vector2(0.24f * S, 0.028f * S), 0.02f * S)));
                a = Mathf.Max(a, Mathf.Clamp01(0.5f - (Vector2.Distance(p, new Vector2(0.5f * S, 0.72f * S)) - 0.04f * S)));
                a = Mathf.Max(a, Mathf.Clamp01(0.5f - (Vector2.Distance(p, new Vector2(0.5f * S, 0.235f * S)) - 0.045f * S)));
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A head over rounded shoulders - the Account glyph.
    public static Sprite Person(Color color)
    {
        string key = $"person:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        Vector2 headC = new Vector2(0.5f * S, 0.66f * S);
        float headR = 0.135f * S;
        Vector2 shoulderC = new Vector2(0.5f * S, 0.28f * S);
        float shoulderR = 0.26f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = Mathf.Clamp01(0.5f - (Vector2.Distance(p, headC) - headR));
                if (p.y >= shoulderC.y) a = Mathf.Max(a, Mathf.Clamp01(0.5f - (Vector2.Distance(p, shoulderC) - shoulderR)));
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A ringed "i" - the About / Legal glyph.
    public static Sprite Info(Color color)
    {
        string key = $"info:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        float half = 0.045f * S;
        Vector2 center = new Vector2(0.5f * S, 0.5f * S);
        float ringR = 0.34f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = StrokeAlpha(Vector2.Distance(p, center) - ringR, half);
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, new Vector2(0.5f * S, 0.57f * S), new Vector2(0.5f * S, 0.36f * S)), half));
                a = Mathf.Max(a, Mathf.Clamp01(0.5f - (Vector2.Distance(p, new Vector2(0.5f * S, 0.66f * S)) - 0.045f * S)));
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A single eighth note (head + stem + flag) - the Music Volume row glyph.
    public static Sprite Note(Color color)
    {
        string key = $"note:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        float half = 0.05f * S;
        Vector2 head = new Vector2(0.37f * S, 0.30f * S);
        float headR = 0.135f * S;
        Vector2 stemBot = new Vector2(0.50f * S, 0.30f * S);
        Vector2 stemTop = new Vector2(0.50f * S, 0.74f * S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = Mathf.Clamp01(0.5f - (Vector2.Distance(p, head) - headR));
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, stemBot, stemTop), half));
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, stemTop, new Vector2(0.68f * S, 0.66f * S)), half));
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A speaker with two sound waves - the Sound Effects row glyph.
    public static Sprite Speaker(Color color)
    {
        string key = $"speaker:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        float half = 0.045f * S;
        Vector2 center = new Vector2(0.50f * S, 0.50f * S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = Mathf.Clamp01(0.5f - RoundedBoxDist(p, new Vector2(0.32f * S, 0.50f * S), new Vector2(0.07f * S, 0.085f * S), 0.02f * S));
                if (PointInTriangle(p, new Vector2(0.24f * S, 0.50f * S), new Vector2(0.50f * S, 0.72f * S), new Vector2(0.50f * S, 0.28f * S))) a = 1f;
                if (p.x >= 0.54f * S)
                {
                    a = Mathf.Max(a, StrokeAlpha(Vector2.Distance(p, center) - 0.15f * S, half));
                    a = Mathf.Max(a, StrokeAlpha(Vector2.Distance(p, center) - 0.24f * S, half));
                }
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A speaker with an "x" instead of waves - the Mute All row glyph.
    public static Sprite SpeakerOff(Color color)
    {
        string key = $"speakeroff:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        float half = 0.045f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = Mathf.Clamp01(0.5f - RoundedBoxDist(p, new Vector2(0.32f * S, 0.50f * S), new Vector2(0.07f * S, 0.085f * S), 0.02f * S));
                if (PointInTriangle(p, new Vector2(0.24f * S, 0.50f * S), new Vector2(0.50f * S, 0.72f * S), new Vector2(0.50f * S, 0.28f * S))) a = 1f;
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, new Vector2(0.60f * S, 0.40f * S), new Vector2(0.80f * S, 0.60f * S)), half));
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, new Vector2(0.60f * S, 0.60f * S), new Vector2(0.80f * S, 0.40f * S)), half));
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A four-point sparkle (two crossed thin diamonds) - the Visual Effects row glyph.
    public static Sprite Sparkle(Color color)
    {
        string key = $"sparkle:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = ((x + 0.5f) / S) * 2f - 1f;
                float v = ((y + 0.5f) / S) * 2f - 1f;
                float tall = Mathf.Abs(u) / 0.22f + Mathf.Abs(v) / 0.62f;
                float wide = Mathf.Abs(u) / 0.62f + Mathf.Abs(v) / 0.22f;
                float a = Mathf.Max(Mathf.Clamp01((1f - tall) * 6f), Mathf.Clamp01((1f - wide) * 6f));
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    // A box with motion lines either side - the Screen Shake row glyph.
    public static Sprite Shake(Color color)
    {
        string key = $"shake:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        float half = 0.045f * S;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = StrokeAlpha(RoundedBoxDist(p, new Vector2(0.5f * S, 0.5f * S), new Vector2(0.15f * S, 0.15f * S), 0.04f * S), half);
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, new Vector2(0.24f * S, 0.41f * S), new Vector2(0.24f * S, 0.59f * S)), half));
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, new Vector2(0.14f * S, 0.45f * S), new Vector2(0.14f * S, 0.55f * S)), half));
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, new Vector2(0.76f * S, 0.41f * S), new Vector2(0.76f * S, 0.59f * S)), half));
                a = Mathf.Max(a, StrokeAlpha(DistToSegment(p, new Vector2(0.86f * S, 0.45f * S), new Vector2(0.86f * S, 0.55f * S)), half));
                Color c = color;
                c.a = color.a * a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(Vector2.Dot(ab, ab), 1e-5f));
        return Vector2.Distance(p, a + ab * t);
    }

    // Signed distance to a rounded rectangle (negative inside), for anti-aliased fills.
    private static float RoundedBoxDist(Vector2 p, Vector2 center, Vector2 halfSize, float radius)
    {
        Vector2 d = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y))
                    - (halfSize - new Vector2(radius, radius));
        float outside = new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - radius;
    }

    // A rounded-rect with a vertical gradient (bottom->top), 9-sliced. Every column carries the
    // same vertical gradient, so horizontal AND vertical slice-stretching stay consistent - it
    // tiles to any button size without distorting the gradient. Use with Image.Type.Sliced.
    public static Sprite RoundedGradient(Color top, Color bottom)
    {
        string key = $"roundgrad:{Key(top)}:{Key(bottom)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        const float radius = 30f;
        Texture2D tex = NewTexture(S, S);
        Vector2 center = new Vector2(S * 0.5f, S * 0.5f);
        Vector2 half = new Vector2(S * 0.5f - 1f, S * 0.5f - 1f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = Mathf.Clamp01(0.5f - RoundedBoxDist(p, center, half, radius));
                Color c = Color.Lerp(bottom, top, (y + 0.5f) / S);
                c.a *= a;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply(false, true);
        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return Cache[key] = sprite;
    }

    // A smooth vertical gradient (bottom->top), 1px wide, stretched by callers. Used as the
    // scrim over a thumbnail so its lower edge fades into the panel. Alpha is honoured, so pass
    // a transparent top and an opaque panel-coloured bottom.
    public static Sprite VerticalFade(Color top, Color bottom)
    {
        string key = $"vfade:{Key(top)}:{Key(bottom)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int W = 4;
        const int Hh = 128;
        Texture2D tex = NewTexture(W, Hh);
        for (int y = 0; y < Hh; y++)
        {
            Color c = Color.Lerp(bottom, top, (y + 0.5f) / Hh);
            for (int x = 0; x < W; x++) tex.SetPixel(x, y, c);
        }
        return Cache[key] = Finish(tex, 100f);
    }

    // A filled trophy (tapered cup + side handles + stem + base) for the Ranks button.
    public static Sprite Trophy(Color color)
    {
        string key = $"trophy:{Key(color)}";
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int S = 96;
        Texture2D tex = NewTexture(S, S);
        float cx = 0.5f * S;
        float rimY = 0.78f * S, bowlBot = 0.44f * S;
        float rimHalf = 0.21f * S, botHalf = 0.115f * S;
        float handleOuter = 0.115f * S, handleInner = 0.072f * S;
        Vector2 lh = new Vector2(cx - rimHalf, 0.68f * S);
        Vector2 rh = new Vector2(cx + rimHalf, 0.68f * S);
        Vector2 stemC = new Vector2(cx, 0.37f * S);
        Vector2 stemH = new Vector2(0.045f * S, 0.075f * S);
        Vector2 baseC = new Vector2(cx, 0.27f * S);
        Vector2 baseH = new Vector2(0.11f * S, 0.035f * S);
        Vector2 footC = new Vector2(cx, 0.215f * S);
        Vector2 footH = new Vector2(0.17f * S, 0.03f * S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float a = 0f;

                // Cup: tapers from the wide rim down to a rounded bottom (eased).
                if (p.y <= rimY && p.y >= bowlBot)
                {
                    float t = (rimY - p.y) / (rimY - bowlBot);
                    float halfw = Mathf.Lerp(rimHalf, botHalf, t * t);
                    if (Mathf.Abs(p.x - cx) <= halfw) a = 1f;
                }
                // Handles: a ring on each side, outer half only.
                float dl = Vector2.Distance(p, lh);
                if (p.x <= lh.x && dl <= handleOuter && dl >= handleInner) a = 1f;
                float dr = Vector2.Distance(p, rh);
                if (p.x >= rh.x && dr <= handleOuter && dr >= handleInner) a = 1f;
                // Stem + two base tiers.
                if (RoundedBoxDist(p, stemC, stemH, 0.01f * S) < 0f) a = 1f;
                if (RoundedBoxDist(p, baseC, baseH, 0.02f * S) < 0f) a = 1f;
                if (RoundedBoxDist(p, footC, footH, 0.02f * S) < 0f) a = 1f;

                Color c = color;
                c.a *= a;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
    }

    private static Texture2D NewTexture(int width, int height)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    private static Sprite Finish(Texture2D tex, float pixelsPerUnit)
    {
        tex.Apply(false, true);
        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), pixelsPerUnit);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static string Key(Color c)
    {
        Color32 c32 = c;
        return $"{c32.r:x2}{c32.g:x2}{c32.b:x2}{c32.a:x2}";
    }

    private static void DrawRect(Texture2D tex, int x, int y, int w, int h, Color color)
    {
        for (int py = y; py < y + h; py++)
        {
            if (py < 0 || py >= tex.height) continue;
            for (int px = x; px < x + w; px++)
            {
                if (px < 0 || px >= tex.width) continue;
                tex.SetPixel(px, py, color);
            }
        }
    }

    private static void DrawRectOutline(Texture2D tex, int x, int y, int w, int h, Color color)
    {
        DrawRect(tex, x, y, w, 2, color);
        DrawRect(tex, x, y + h - 2, w, 2, color);
        DrawRect(tex, x, y, 2, h, color);
        DrawRect(tex, x + w - 2, y, 2, h, color);
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s = a.y * c.x - a.x * c.y + (c.y - a.y) * p.x + (a.x - c.x) * p.y;
        float t = a.x * b.y - a.y * b.x + (a.y - b.y) * p.x + (b.x - a.x) * p.y;
        if ((s < 0f) != (t < 0f)) return false;

        float area = -b.y * c.x + a.y * (c.x - b.x) + a.x * (b.y - c.y) + b.x * c.y;
        return area < 0f ? s <= 0f && s + t >= area : s >= 0f && s + t <= area;
    }
}
