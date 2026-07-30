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
    // Subtle guide column: a faint white wash fading out toward the top, so the landing
    // end (texture bottom) reads strongest, plus a hairline near-black rail down each
    // long edge. The wash carries the beam on dark backdrops where the rails vanish;
    // on light backdrops (ch2/4/6 skies) the wash washes out and the rails carry it.
    // Stretch via SpriteRenderer.size: the renderer draws Sliced, and the sprite border
    // keeps the rails at RailPx/PPU world width regardless of brick width.
    private static Sprite _placementBeam;

    public static Sprite PlacementBeam()
    {
        if (_placementBeam != null) return _placementBeam;

        const int W = 32, H = 256;
        const int RailPx = 2;
        const float FillAlpha = 0.05f;
        const float RailAlpha = 0.20f;
        Texture2D tex = NewTexture(W, H);
        for (int y = 0; y < H; y++)
        {
            float fade = Mathf.Lerp(1f, 0.25f, (float)y / (H - 1));
            for (int x = 0; x < W; x++)
            {
                bool rail = x < RailPx || x >= W - RailPx;
                tex.SetPixel(x, y, rail
                    ? new Color(0f, 0f, 0f, RailAlpha * fade)
                    : new Color(1f, 1f, 1f, FillAlpha * fade));
            }
        }
        return _placementBeam = Finish(tex, 64f, new Vector4(RailPx, 0f, RailPx, 0f));
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
                        // -1.182 vertically centers the curve's v extent of [-1, 1.236] in the
                        // texture, so a centered Image rect shows a centered heart.
                        float v = ((y + (sy + 0.5f) / 3f) / S) * 2.6f - 1.182f;
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

    // ---- soft round puff (landing dust etc.; tint via color) ------------------------------
    // Radial falloff with a dense core: reads as a dust cloudlet, not a glow. 1 world unit
    // in diameter at scale 1.
    private static Sprite _softPuff;

    public static Sprite SoftPuff()
    {
        if (_softPuff != null) return _softPuff;

        const int S = 48;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f;
                float v = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (0.4f + 0.6f * a)));
            }
        }
        return _softPuff = Finish(tex, S);
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

    // ---- the run-life heart, two states (SHOP.md §2) --------------------------------------
    // Baked in the game's own bevel language (the brick shaders' rounded-shape + bevel +
    // near-black outline), not a flat glyph: a signed distance field over the classic heart
    // curve drives an outline band, a lit bevel (normal from the field's nearest-edge
    // direction) and a body ramp. FULL = deep ruby gem with a top-left sheen; EMPTY = the
    // same silhouette as a dark recessed socket (inverted bevel light = reads as a hole).
    // Procedural on purpose: it matches the bricks, scales crisp, and the shatter/crack
    // animations own the shape (Nick, 2026-07-20 - replaced the emoji-style heart.png).

    private static Sprite _heartFull;
    private static Sprite _heartEmpty;

    public static Sprite HeartFull()
    {
        if (_heartFull == null) _heartFull = BakeHeart(full: true);
        return _heartFull;
    }

    public static Sprite HeartEmpty()
    {
        if (_heartEmpty == null) _heartEmpty = BakeHeart(full: false);
        return _heartEmpty;
    }

    private static Sprite BakeHeart(bool full)
    {
        const int S = 192;

        // 1. Coverage mask (3x3 supersampled) - also the alpha edge.
        float[] mask = new float[S * S];
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
                        // -1.182 vertically centers the curve's v extent of [-1, 1.236] in the
                        // texture (the old -1.5 clipped the lobes and left the heart riding high).
                        float v = ((y + (sy + 0.5f) / 3f) / S) * 2.6f - 1.182f;
                        float f = u * u + v * v - 1f;
                        if (f * f * f - u * u * v * v * v <= 0f) coverage += 1f;
                    }
                }
                mask[y * S + x] = coverage / 9f;
            }
        }

        // 2. Vector distance field (8SSEDT): per inside pixel, offset to the nearest edge.
        //    Gives both depth (outline/bevel bands) and the bevel normal for lighting.
        Vector2[] toEdge = new Vector2[S * S];
        const float far = 1e6f;
        for (int i = 0; i < toEdge.Length; i++)
        {
            toEdge[i] = mask[i] < 0.5f ? Vector2.zero : new Vector2(far, far);
        }
        void Relax(int x, int y, int dx, int dy)
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= S || ny >= S) return;
            Vector2 candidate = toEdge[ny * S + nx] + new Vector2(dx, dy);
            if (candidate.sqrMagnitude < toEdge[y * S + x].sqrMagnitude) toEdge[y * S + x] = candidate;
        }
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            { Relax(x, y, -1, 0); Relax(x, y, 0, -1); Relax(x, y, -1, -1); Relax(x, y, 1, -1); }
        for (int y = S - 1; y >= 0; y--)
            for (int x = S - 1; x >= 0; x--)
            { Relax(x, y, 1, 0); Relax(x, y, 0, 1); Relax(x, y, 1, 1); Relax(x, y, -1, 1); }

        // 3. Shade. Bands are in pixels at S=192.
        const float outlineW = 7f;
        const float bevelW = 22f;
        Color outlineColor = new Color(0.13f, 0.03f, 0.06f, 1f); // near-black plum, the tower's line weight
        Vector2 lightDir = new Vector2(-0.55f, 0.84f).normalized; // top-left, matching the bricks

        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float a = mask[y * S + x];
                if (a <= 0f) { tex.SetPixel(x, y, Color.clear); continue; }

                float d = toEdge[y * S + x].magnitude;
                Color color;
                if (d <= outlineW)
                {
                    color = outlineColor;
                }
                else
                {
                    // Normalized coords for ramps/sheen: u right, v up, 0..1 across the heart.
                    float u01 = x / (float)S;
                    float v01 = y / (float)S;
                    // Bevel light: normal points from the pixel toward the edge (outward);
                    // SetPixel space is already y-up, so it dots with the light directly.
                    Vector2 normal = toEdge[y * S + x];
                    normal = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector2.up;
                    float lit = Vector2.Dot(normal, lightDir);
                    float band = Mathf.Clamp01((outlineW + bevelW - d) / bevelW);        // 1 at outline, 0 at body

                    if (full)
                    {
                        // Ruby body: darker low, richer high, faceted feel from the bevel.
                        Color deep = new Color(0.42f, 0.05f, 0.12f, 1f);
                        Color body = new Color(0.72f, 0.11f, 0.20f, 1f);
                        Color glow = new Color(0.98f, 0.42f, 0.42f, 1f);
                        color = Color.Lerp(deep, body, Mathf.Clamp01(v01 * 1.5f - 0.1f));
                        if (band > 0f)
                        {
                            color = lit > 0f
                                ? Color.Lerp(color, glow, band * lit * 0.85f)
                                : Color.Lerp(color, deep, band * -lit * 0.9f);
                        }
                        // Top-left sheen blob - the gem's one highlight, tight and bright.
                        float sx = (u01 - 0.34f) / 0.11f;
                        float sy2 = (v01 - 0.72f) / 0.075f;
                        float sheen = Mathf.Clamp01(1f - (sx * sx + sy2 * sy2));
                        color = Color.Lerp(color, new Color(1f, 0.93f, 0.92f, 1f), sheen * sheen * 0.8f);
                    }
                    else
                    {
                        // Socket: dark recess. Inverted bevel (lit from BELOW) reads as a hole;
                        // a whisper of dried red keeps it the heart's ghost, not a generic pit.
                        // Contrast is the whole trick: pit near-black, walls clearly lighter,
                        // hole-shadow hugging the top edge, a bright lip catching the bottom.
                        Color pit = new Color(0.045f, 0.030f, 0.036f, 1f);
                        Color wall = new Color(0.22f, 0.145f, 0.15f, 1f);
                        float depth = Mathf.Clamp01(d / (outlineW + bevelW + 26f)); // 0 at rim, 1 deep inside
                        color = Color.Lerp(wall, pit, depth);
                        if (band > 0f)
                        {
                            color = lit > 0f
                                ? Color.Lerp(color, pit, band * lit)                                          // top edge: hole shadow
                                : Color.Lerp(color, new Color(0.44f, 0.26f, 0.26f, 1f), band * -lit * 0.85f); // bottom lip catches light
                        }
                    }
                }
                color.a = a;
                tex.SetPixel(x, y, color);
            }
        }
        return Finish(tex, S);
    }

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
