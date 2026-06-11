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
public static class RuntimeSprites
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

    // ---- soft horizontal bar (laser line etc.) -------------------------------------------
    // Thin full-width bar, soft-edged vertically. PPU encodes the requested world
    // thickness; scale X to the desired length. Cached per thickness bucket would be
    // overkill - cached for the single thickness in use, rebuilt if it changes.
    private static Sprite _softBar;
    private static float _softBarThickness;

    public static Sprite SoftHorizontalBar(float worldThickness)
    {
        if (_softBar != null && Mathf.Approximately(_softBarThickness, worldThickness)) return _softBar;

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
        _softBarThickness = worldThickness;
        return _softBar = Finish(tex, H / Mathf.Max(0.01f, worldThickness));
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

    // ---- backdrop elements (clouds, hills, ambient dots) ----------------------------------
    // White shapes, tinted via renderer color. Variants use fixed blob tables, not Random,
    // so they're deterministic and cache-stable.

    private static readonly Sprite[] _clouds = new Sprite[3];
    private static readonly float[][][] CloudBlobs =
    {
        // per variant: (centerX, centerY, radiusX, radiusY) in 0..1 sprite space
        new[] { new[] {0.30f, 0.45f, 0.22f, 0.30f}, new[] {0.52f, 0.55f, 0.26f, 0.40f}, new[] {0.72f, 0.45f, 0.20f, 0.28f} },
        new[] { new[] {0.25f, 0.40f, 0.18f, 0.26f}, new[] {0.45f, 0.55f, 0.22f, 0.38f}, new[] {0.65f, 0.50f, 0.24f, 0.34f}, new[] {0.82f, 0.40f, 0.14f, 0.22f} },
        new[] { new[] {0.35f, 0.50f, 0.28f, 0.36f}, new[] {0.62f, 0.48f, 0.26f, 0.32f} },
    };

    public static Sprite Cloud(int variant)
    {
        int index = Mathf.Abs(variant) % _clouds.Length;
        if (_clouds[index] != null) return _clouds[index];

        const int W = 160, H = 80;
        float[][] blobs = CloudBlobs[index];
        Texture2D tex = NewTexture(W, H);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float v = (y + 0.5f) / H;
                float alpha = 0f;
                for (int b = 0; b < blobs.Length; b++)
                {
                    float dx = (u - blobs[b][0]) / blobs[b][2];
                    float dy = (v - blobs[b][1]) / blobs[b][3];
                    float d = dx * dx + dy * dy;
                    alpha = Mathf.Max(alpha, Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(d)));
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        return _clouds[index] = Finish(tex, 64f); // 2.5 x 1.25 world units at scale 1
    }

    private static readonly Sprite[] _hills = new Sprite[2];

    public static Sprite HillSilhouette(int variant)
    {
        int index = Mathf.Abs(variant) % _hills.Length;
        if (_hills[index] != null) return _hills[index];

        const int W = 512, H = 192;
        float f1 = index == 0 ? 0.013f : 0.021f;
        float f2 = index == 0 ? 0.041f : 0.033f;
        float p1 = index == 0 ? 1.3f : 4.1f;
        float p2 = index == 0 ? 5.2f : 0.7f;
        Texture2D tex = NewTexture(W, H);
        for (int x = 0; x < W; x++)
        {
            float crest = H * (0.45f + 0.22f * Mathf.Sin(x * f1 + p1) + 0.10f * Mathf.Sin(x * f2 + p2));
            for (int y = 0; y < H; y++)
            {
                // Texture row 0 IS the bottom in Unity - paint directly (a flip here once
                // rendered the silhouettes as floating ribbons with wavy undersides).
                float alpha = Mathf.Clamp01((crest - y) / 3f); // soft crest edge, solid below
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        return _hills[index] = Finish(tex, 64f); // 8 x 3 world units at scale 1
    }

    // Flat, stepped desert mesas (Monument-Valley style silhouettes).
    private static readonly Sprite[] _mesas = new Sprite[2];
    private static readonly float[][][] MesaShapes =
    {
        // per variant: pyramids as (centerX 0..1, halfWidth 0..1, tierCount)
        new[] { new[] {0.30f, 0.42f, 4f}, new[] {0.78f, 0.28f, 3f} },
        new[] { new[] {0.18f, 0.26f, 3f}, new[] {0.58f, 0.44f, 5f}, new[] {0.92f, 0.20f, 2f} },
    };

    public static Sprite SteppedMesa(int variant)
    {
        int index = Mathf.Abs(variant) % _mesas.Length;
        if (_mesas[index] != null) return _mesas[index];

        const int W = 512, H = 192;
        // Low and unobtrusive: mesas should peek behind the ground building, not tower
        // over it - at full height they read as strange static clouds.
        const float TierH = 0.085f;    // tier height as fraction of texture height
        const float TierShrink = 0.24f; // each tier narrows by this fraction of the base half-width
        float[][] pyramids = MesaShapes[index];

        Texture2D tex = NewTexture(W, H);
        for (int x = 0; x < W; x++)
        {
            float u = (x + 0.5f) / W;
            float crest = H * 0.12f; // low solid base everywhere so layers never gap
            for (int p = 0; p < pyramids.Length; p++)
            {
                float half0 = pyramids[p][1];
                int tiers = (int)pyramids[p][2];
                for (int t = 0; t < tiers; t++)
                {
                    float half = half0 * (1f - TierShrink * t);
                    if (half <= 0f || Mathf.Abs(u - pyramids[p][0]) > half) break;
                    crest = Mathf.Max(crest, H * (0.12f + TierH * (t + 1)));
                }
            }
            for (int y = 0; y < H; y++)
            {
                float alpha = Mathf.Clamp01(crest - y); // crisp stepped edge, solid below
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        return _mesas[index] = Finish(tex, 64f);
    }

    // Flat, blocky clouds (stacked crisp rectangles) for stylized skies.
    private static readonly Sprite[] _blockyClouds = new Sprite[3];
    private static readonly float[][][] BlockyCloudRects =
    {
        // per variant: rects as (x, y, w, h) in 0..1 sprite space (y up from bottom)
        new[] { new[] {0.12f, 0.20f, 0.66f, 0.30f}, new[] {0.28f, 0.50f, 0.34f, 0.26f} },
        new[] { new[] {0.06f, 0.22f, 0.80f, 0.26f}, new[] {0.20f, 0.48f, 0.42f, 0.22f}, new[] {0.34f, 0.70f, 0.20f, 0.16f} },
        new[] { new[] {0.20f, 0.30f, 0.55f, 0.32f}, new[] {0.40f, 0.62f, 0.26f, 0.20f} },
    };

    public static Sprite BlockyCloud(int variant)
    {
        int index = Mathf.Abs(variant) % _blockyClouds.Length;
        if (_blockyClouds[index] != null) return _blockyClouds[index];

        const int W = 160, H = 80;
        float[][] rects = BlockyCloudRects[index];
        Texture2D tex = NewTexture(W, H);
        tex.filterMode = FilterMode.Point; // keep the edges crisp
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float v = (y + 0.5f) / H;
                bool inside = false;
                for (int r = 0; r < rects.Length && !inside; r++)
                {
                    inside = u >= rects[r][0] && u <= rects[r][0] + rects[r][2] &&
                             v >= rects[r][1] && v <= rects[r][1] + rects[r][3];
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, inside ? 1f : 0f));
            }
        }
        return _blockyClouds[index] = Finish(tex, 64f);
    }

    private static Sprite _softDot;

    public static Sprite SoftDot()
    {
        if (_softDot != null) return _softDot;

        const int S = 32;
        Texture2D tex = NewTexture(S, S);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f;
                float dy = (y + 0.5f) / S * 2f - 1f;
                float alpha = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy)));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        return _softDot = Finish(tex, S); // 1x1 world unit; scale to size
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

    private static Sprite Finish(Texture2D tex, float pixelsPerUnit, Vector4 border = default)
    {
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
