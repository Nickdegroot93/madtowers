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
                float d = Mathf.Max(av, au * 0.8660254f + av * 0.5f) - 0.76f;
                float edge = Mathf.Clamp01(0.5f - d * 55f);
                float border = Mathf.Clamp01(1.3f - Mathf.Abs(d) * 85f);
                Color c = Color.Lerp(bottom, top, (v + 1f) * 0.5f);
                c = Color.Lerp(c, Color.white, border * 0.16f);
                c.a = edge;
                tex.SetPixel(x, y, c);
            }
        }
        return Cache[key] = Finish(tex, S);
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
                float d = Mathf.Abs(u) + Mathf.Abs(v) - 0.78f;
                float inside = Mathf.Clamp01(0.5f - d * 80f);
                float stroke = Mathf.Clamp01(1.1f - Mathf.Abs(d) * 105f);
                Color c = Color.Lerp(fill, border, stroke * 0.82f);
                c.a = Mathf.Max(fill.a * inside, border.a * stroke) * inside;
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
                float d = Mathf.Sqrt(u * u + v * v) - 0.78f;
                float inside = Mathf.Clamp01(0.5f - d * 80f);
                float stroke = Mathf.Clamp01(1.1f - Mathf.Abs(d) * 105f);
                Color c = Color.Lerp(fill, border, stroke * 0.82f);
                c.a = Mathf.Max(fill.a * inside, border.a * stroke) * inside;
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
