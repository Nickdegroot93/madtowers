using UnityEngine;

/// <summary>
/// The one home for procedural sprites used by runtime visuals: placement beam, HUD heart
/// and panel, the height-limit laser bar, background gradients. Keeps primitive shapes
/// asset-free and stops every feature from growing its own texture boilerplate.
///
/// Fixed shapes are built once and cached for the session (statics reset on domain
/// reload); parameterized builders (gradient) return a fresh sprite the CALLER owns and
/// must destroy when replacing. Everything is HideAndDontSave so nothing leaks into
/// saved scenes.
/// </summary>
public static partial class RuntimeSprites
{
    // ---- placement beam -----------------------------------------------------------------
    // Subtle guide column: a faint borderless wash fading out toward the top, so the
    // landing end (texture bottom) reads strongest. Stretch via SpriteRenderer.size.
    private static Sprite _placementBeam;

    public static Sprite PlacementBeam()
    {
        if (_placementBeam != null) return _placementBeam;

        const int W = 8, H = 256;
        Texture2D tex = NewTexture(W, H);
        for (int y = 0; y < H; y++)
        {
            float fade = Mathf.Lerp(1f, 0.25f, (float)y / (H - 1));
            for (int x = 0; x < W; x++)
            {
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0.05f * fade));
            }
        }
        return _placementBeam = Finish(tex, 64f);
    }

    // ---- HUD heart ----------------------------------------------------------------------
    // Classic implicit heart curve, supersampled for smooth edges. White; tint via color.
    private static Sprite _heart;

    public static Sprite Heart()
    {
        if (_heart != null) return _heart;

        const int S = 64;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float coverage = 0f;
                for (int sy = 0; sy < 3; sy++)
                {
                    for (int sx = 0; sx < 3; sx++)
                    {
                        float u = ((x + (sx + 0.5f) / 3f) / S) * 2.6f - 1.3f;
                        float v = ((y + (sy + 0.5f) / 3f) / S) * 2.6f - 1.5f;
                        float f = u * u + v * v - 1f;
                        if (f * f * f - u * u * v * v * v <= 0f) coverage += 1f;
                    }
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, coverage / 9f));
            }
        }
        return _heart = Finish(tex, S);
    }

    // ---- rounded UI panel ---------------------------------------------------------------
    // 9-sliceable rounded rect (border 24 vs radius 14, so corners stay crisp at any size).
    // Use with Image.type = Sliced. White; tint via color.
    private static Sprite _roundedPanel;

    public static Sprite RoundedPanel()
    {
        if (_roundedPanel != null) return _roundedPanel;

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
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d)));
            }
        }
        return _roundedPanel = Finish(tex, 100f, new Vector4(24f, 24f, 24f, 24f));
    }

    // ---- bubble (circular HUD button: Pocket Cache hold, any future round button) ----------
    // A glassy disc: translucent fill + a very fine soft rim, soft-AA outer edge.
    // White; tint via color. Supersampled for clean curves; keep the Image square for a true circle.
    private static Sprite _bubble;

    public static Sprite Bubble()
    {
        if (_bubble != null) return _bubble;

        const int S = 256;
        const float Fill = 0.12f;   // translucent interior alpha
        const float Ring = 0.93f;   // rim sits near the edge
        const float RingW = 0.016f; // hairline, mostly a highlight rather than a border
        const float RingBoost = 0.13f;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float inside = 0f, ring = 0f;
                for (int sy = 0; sy < 2; sy++)
                {
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float u = ((x + (sx + 0.5f) / 2f) / S) * 2f - 1f;
                        float v = ((y + (sy + 0.5f) / 2f) / S) * 2f - 1f;
                        float d = Mathf.Sqrt(u * u + v * v);              // 0 centre, 1 at the edge
                        inside += Mathf.Clamp01((1f - d) * S / 3f);       // AA disc mask
                        ring += Mathf.Exp(-((d - Ring) * (d - Ring)) / (2f * RingW * RingW));
                    }
                }
                inside *= 0.25f; ring *= 0.25f;
                float a = inside * Mathf.Clamp01(Fill + ring * RingBoost);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        return _bubble = Finish(tex, S);
    }

    // ---- soft horizontal bar (laser line, HUD flourishes, card shine) ----------------------
    // Thin full-width bar, soft-edged vertically. PPU encodes the requested world
    // thickness; scale X to the desired length. Cached PER THICKNESS: multiple live
    // callers use different values (laser 0.12 vs UI 0.1) - a single-slot cache
    // thrashed between them and leaked each replaced HideAndDontSave texture.
    private static readonly System.Collections.Generic.Dictionary<int, Sprite> _softBars =
        new System.Collections.Generic.Dictionary<int, Sprite>();

    public static Sprite SoftHorizontalBar(float worldThickness)
    {
        int key = Mathf.RoundToInt(worldThickness * 1000f);
        if (_softBars.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int W = 4, H = 16;
        Texture2D tex = NewTexture(W, H);
        for (int y = 0; y < H; y++)
        {
            float edge = 1f - Mathf.Abs((y + 0.5f) / H * 2f - 1f); // 0 at edges, 1 in middle
            float a = Mathf.SmoothStep(0f, 1f, edge * 1.6f);
            for (int x = 0; x < W; x++)
            {
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        Sprite sprite = Finish(tex, H / Mathf.Max(0.01f, worldThickness));
        _softBars[key] = sprite;
        return sprite;
    }

    private static readonly System.Collections.Generic.Dictionary<int, Sprite> _softVBars =
        new System.Collections.Generic.Dictionary<int, Sprite>();

    // Vertical twin of SoftHorizontalBar: soft-edged across X (the width), tall on Y (the length,
    // stretched via localScale.y). PPU is set so the sprite's world width == worldThickness, so the
    // Zap beam can drive width by scale alone. Cached per thickness like the horizontal bars.
    public static Sprite SoftVerticalBar(float worldThickness)
    {
        int key = Mathf.RoundToInt(worldThickness * 1000f);
        if (_softVBars.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        const int W = 16, H = 4;
        Texture2D tex = NewTexture(W, H);
        for (int x = 0; x < W; x++)
        {
            float edge = 1f - Mathf.Abs((x + 0.5f) / W * 2f - 1f); // 0 at edges, 1 in middle
            float a = Mathf.SmoothStep(0f, 1f, edge * 1.6f);
            for (int y = 0; y < H; y++)
            {
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        Sprite sprite = Finish(tex, W / Mathf.Max(0.01f, worldThickness));
        _softVBars[key] = sprite;
        return sprite;
    }

    // ---- chevron (nudge ghost-button glyph) ------------------------------------------------
    // Left-pointing "<" drawn as two soft-edged strokes. White; tint via color; rotate the
    // Image 180 degrees for the right-pointing twin (the shape is vertically symmetric).
    private static Sprite _chevron;

    public static Sprite Chevron()
    {
        if (_chevron != null) return _chevron;

        const int S = 64;
        const float HalfStroke = 3.2f;
        Vector2 top = new Vector2(40f, 50f), mid = new Vector2(25f, 32f), bottom = new Vector2(40f, 14f);

        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Mathf.Min(DistanceToSegment(p, top, mid), DistanceToSegment(p, mid, bottom));
                float a = Mathf.Clamp01((HalfStroke - d + 0.75f) / 1.5f); // 1.5px soft edge
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        return _chevron = Finish(tex, S);
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    // ---- wind streak (nudge dash air) -----------------------------------------------------
    // Soft-ended horizontal motion line: alpha peaks in the middle and fades to nothing at
    // both tips and the long edges. 1 world unit long x 0.125 tall at scale 1.
    private static Sprite _windStreak;

    public static Sprite WindStreak()
    {
        if (_windStreak != null) return _windStreak;

        const int W = 64, H = 8;
        Texture2D tex = NewTexture(W, H);
        for (int y = 0; y < H; y++)
        {
            float v = 1f - Mathf.Abs((y + 0.5f) / H * 2f - 1f); // 0 at edges, 1 in middle
            for (int x = 0; x < W; x++)
            {
                float u = 1f - Mathf.Abs((x + 0.5f) / W * 2f - 1f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f,
                    Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, u * 1.8f)) * v * v));
            }
        }
        return _windStreak = Finish(tex, W);
    }

    // ---- plain white square (shard particles etc.; tint via color) ------------------------
    private static Sprite _square;

    public static Sprite Square()
    {
        if (_square != null) return _square;

        const int S = 4;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                tex.SetPixel(x, y, Color.white);
            }
        }
        return _square = Finish(tex, S); // 1x1 world unit; scale to size
    }

    // ---- vector guide ghost --------------------------------------------------------------
    // Soft white primitives for the landing preview. The fill stays barely visible while
    // the line strip carries the silhouette; tint alpha controls final intensity.
    private static Sprite _vectorGuideGhostFill;
    private static Sprite _vectorGuideGhostLine;

    public static Sprite VectorGuideGhostFill()
    {
        if (_vectorGuideGhostFill != null) return _vectorGuideGhostFill;

        const int S = 64;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = RoundedBoxDistance(x + 0.5f, y + 0.5f, S * 0.5f, S * 0.5f, 27f, 27f, 7f);
                float coverage = Mathf.Clamp01(0.5f - d / 2.5f);
                float u = Mathf.Abs((x + 0.5f) / S * 2f - 1f);
                float v = Mathf.Abs((y + 0.5f) / S * 2f - 1f);
                float centerFade = 1f - Mathf.Clamp01(Mathf.Max(u, v));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, coverage * (0.35f + centerFade * 0.25f)));
            }
        }
        return _vectorGuideGhostFill = Finish(tex, S);
    }

    public static Sprite VectorGuideGhostLine()
    {
        if (_vectorGuideGhostLine != null) return _vectorGuideGhostLine;

        const int S = 32;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = RoundedBoxDistance(x + 0.5f, y + 0.5f, S * 0.5f, S * 0.5f, 14f, 14f, 5f);
                float coverage = Mathf.Clamp01(0.5f - d / 2f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, coverage));
            }
        }
        return _vectorGuideGhostLine = Finish(tex, S);
    }

    // ---- tutorial ghost hand --------------------------------------------------------------
    // A stylised pointing hand (fist + index finger + thumb), fingertip at the TOP so a tap
    // ripple reads as coming from the fingertip. White; tint via color. Placeholder art for the
    // first-run tutorial (TUTORIAL.md) - a real sprite can drop in behind the same animation.
    private static Sprite _hand;

    public static Sprite Hand()
    {
        if (_hand != null) return _hand;

        const int S = 128;
        Vector2 fingerBase = new Vector2(60f, 58f), fingerTip = new Vector2(60f, 112f);
        Vector2 thumbBase = new Vector2(45f, 56f), thumbTip = new Vector2(32f, 74f);
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float dFist = RoundedBoxDistance(p.x, p.y, 62f, 42f, 27f, 24f, 16f);
                float dFinger = DistanceToSegment(p, fingerBase, fingerTip) - 13f;
                float dThumb = DistanceToSegment(p, thumbBase, thumbTip) - 8f;
                float d = Mathf.Min(dFist, Mathf.Min(dFinger, dThumb));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d))); // 1px soft edge
            }
        }
        return _hand = Finish(tex, S);
    }

    // ---- vertical gradient ---------------------------------------------------------------
    // NOT cached: returns a fresh sprite the caller owns (and should DestroyImmediate,
    // texture included, when replacing - see LevelPresentationController).
    // topAt: fraction of the height at which the top color is fully reached (everything
    // above stays solid top color). curve shapes the blend inside that band (<1 = faster
    // departure from the bottom color).
    public static Sprite VerticalGradient(Color top, Color bottom, float curve = 1f, float topAt = 1f)
    {
        const int H = 256;
        Texture2D tex = NewTexture(1, H);
        top.a = 1f;
        bottom.a = 1f;
        for (int y = 0; y < H; y++)
        {
            float t = Mathf.Clamp01((float)y / (H - 1) / Mathf.Max(0.05f, topAt));
            t = Mathf.Pow(t, Mathf.Max(0.05f, curve));
            tex.SetPixel(0, y, Color.Lerp(bottom, top, t));
        }
        return Finish(tex, 16f);
    }

    // ---- shared plumbing -----------------------------------------------------------------

    private static Texture2D NewTexture(int width, int height)
    {
        return new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private static float RoundedBoxDistance(float x, float y, float cx, float cy,
        float halfWidth, float halfHeight, float radius)
    {
        float qx = Mathf.Abs(x - cx) - (halfWidth - radius);
        float qy = Mathf.Abs(y - cy) - (halfHeight - radius);
        return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
               + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    private static Sprite Finish(Texture2D tex, float pixelsPerUnit, Vector4 border = default)
    {
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
