using UnityEngine;

/// <summary>
/// Backdrop half of the RuntimeSprites factory: clouds (soft / blocky / streak), hill and
/// mesa silhouettes, cacti, soft dots (sun, ambient particles). Same caching and
/// ownership rules as the core file - see RuntimeSprites.cs.
/// </summary>
public static partial class RuntimeSprites
{
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

    private static readonly Sprite[] _hills = new Sprite[3];
    private static readonly float[][] HillWaves =
    {
        // per variant: f1, p1, f2, p2, f3, p3 (three layered waveforms = organic profiles)
        new[] {0.011f, 1.3f, 0.027f, 5.2f, 0.061f, 2.4f},
        new[] {0.017f, 4.1f, 0.035f, 0.7f, 0.052f, 3.8f},
        new[] {0.009f, 2.6f, 0.031f, 4.4f, 0.070f, 0.9f},
    };

    public static Sprite HillSilhouette(int variant)
    {
        int index = Mathf.Abs(variant) % _hills.Length;
        if (_hills[index] != null) return _hills[index];

        const int W = 512, H = 192;
        float[] wave = HillWaves[index];
        Texture2D tex = NewTexture(W, H);
        for (int x = 0; x < W; x++)
        {
            float crest = H * (0.42f + 0.20f * Mathf.Sin(x * wave[0] + wave[1])
                               + 0.09f * Mathf.Sin(x * wave[2] + wave[3])
                               + 0.05f * Mathf.Sin(x * wave[4] + wave[5]));
            for (int y = 0; y < H; y++)
            {
                // Texture row 0 IS the bottom in Unity - paint directly (a flip here once
                // rendered the silhouettes as floating ribbons with wavy undersides).
                float alpha = Mathf.Clamp01((crest - y) / 3f); // soft crest edge, solid below
                // Rim light: a slightly translucent band just below the crest lets the sky
                // glow through, reading as a lighter sunlit edge on each hill.
                if (crest - y > 0f && crest - y < 8f) alpha *= 0.78f;
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
        // Low and unobtrusive: mesas should hug the horizon - at full height they read
        // as strange static clouds.
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

    // Long, thin streak clouds (sunset cirrus bands): soft vertical falloff, fading ends.
    private static readonly Sprite[] _streakClouds = new Sprite[3];
    private static readonly float[][][] StreakBands =
    {
        // per variant: bands as (centerY, halfHeight, startX, endX) in 0..1 sprite space
        new[] { new[] {0.35f, 0.13f, 0.05f, 0.95f}, new[] {0.68f, 0.10f, 0.30f, 1.00f} },
        new[] { new[] {0.50f, 0.15f, 0.00f, 0.80f} },
        new[] { new[] {0.30f, 0.10f, 0.15f, 1.00f}, new[] {0.62f, 0.12f, 0.00f, 0.55f} },
    };

    public static Sprite StreakCloud(int variant)
    {
        int index = Mathf.Abs(variant) % _streakClouds.Length;
        if (_streakClouds[index] != null) return _streakClouds[index];

        const int W = 256, H = 48;
        float[][] bands = StreakBands[index];
        Texture2D tex = NewTexture(W, H);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W;
                float v = (y + 0.5f) / H;
                float alpha = 0f;
                for (int b = 0; b < bands.Length; b++)
                {
                    float vert = 1f - Mathf.Abs((v - bands[b][0]) / bands[b][1]);
                    if (vert <= 0f || u < bands[b][2] || u > bands[b][3]) continue;
                    float span = bands[b][3] - bands[b][2];
                    float t = (u - bands[b][2]) / span;
                    float ends = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) / 0.18f);
                    alpha = Mathf.Max(alpha, Mathf.SmoothStep(0f, 1f, vert) * Mathf.Clamp01(ends));
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        return _streakClouds[index] = Finish(tex, 64f); // 4 x 0.75 world units at scale 1
    }

    // Blocky saguaro cactus (trunk + two offset arms), crisp edges. White; tint via color.
    private static readonly Sprite[] _cacti = new Sprite[2];
    private static readonly float[][][] CactusRects =
    {
        // per variant: rects (x, y, w, h) in 0..1 sprite space (y up)
        new[] { new[] {0.40f, 0.00f, 0.20f, 0.95f},                        // trunk
                new[] {0.10f, 0.50f, 0.30f, 0.10f}, new[] {0.10f, 0.50f, 0.12f, 0.32f},   // left arm
                new[] {0.60f, 0.32f, 0.30f, 0.10f}, new[] {0.78f, 0.32f, 0.12f, 0.36f} }, // right arm
        new[] { new[] {0.42f, 0.00f, 0.18f, 0.88f},
                new[] {0.62f, 0.46f, 0.26f, 0.10f}, new[] {0.76f, 0.46f, 0.12f, 0.30f} },
    };

    public static Sprite Cactus(int variant)
    {
        int index = Mathf.Abs(variant) % _cacti.Length;
        if (_cacti[index] != null) return _cacti[index];

        const int W = 64, H = 96;
        float[][] rects = CactusRects[index];
        Texture2D tex = NewTexture(W, H);
        tex.filterMode = FilterMode.Point;
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
        return _cacti[index] = Finish(tex, 32f); // 2 x 3 world units at scale 1
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
}
