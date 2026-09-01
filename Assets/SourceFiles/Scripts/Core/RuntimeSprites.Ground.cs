using UnityEngine;

/// <summary>
/// Ground-terrain sprites for FloorTerrain: an alpha ramp (white, opaque at the bottom fading to
/// clear at the top - tint + max alpha come from the SpriteRenderer colour, so one sprite serves
/// depth shading, bottom fades and fog bands alike) and a soft elliptical fog blob for drifting
/// wisps. Cached per session, HideAndDontSave, like the rest of the factory.
/// </summary>
public static partial class RuntimeSprites
{
    private static Sprite _alphaRamp;
    private static Sprite _softBlob;
    private static Sprite _groundMottle;

    /// <summary>Seamless 128 px value-noise tile (white, noise in ALPHA, mean ~0.5) at 128/3 ppu,
    /// so one tile spans 3 world units: tiled over the masonry in a chapter haze colour it breaks
    /// the fill's perfect repetition with large soft tonal patches - the painterly weathering the
    /// bought backdrops have and a generated tile can't. Tint + strength via renderer colour.</summary>
    public static Sprite GroundMottle()
    {
        if (_groundMottle != null) return _groundMottle;

        const int S = 128;
        Texture2D tex = NewTexture(S, S);
        tex.wrapMode = TextureWrapMode.Repeat;
        // Three octaves of periodic value noise (lattice sizes 4, 8, 16 - all divide S, so every
        // octave wraps and the tile is seamless), smoothstep-interpolated, contrast-stretched.
        float[] buf = new float[S * S];
        float min = float.MaxValue, max = float.MinValue;
        int[] lattices = { 4, 8, 16 };
        float[] weights = { 0.55f, 0.30f, 0.15f };
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float v = 0f;
                for (int o = 0; o < lattices.Length; o++)
                {
                    int n = lattices[o];
                    float fx = (float)x / S * n, fy = (float)y / S * n;
                    int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
                    float tx = Mathf.SmoothStep(0f, 1f, fx - ix), ty = Mathf.SmoothStep(0f, 1f, fy - iy);
                    float a = MottleHash(ix, iy, n, o), b = MottleHash(ix + 1, iy, n, o);
                    float c = MottleHash(ix, iy + 1, n, o), d = MottleHash(ix + 1, iy + 1, n, o);
                    v += weights[o] * Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                }
                buf[y * S + x] = v;
                min = Mathf.Min(min, v); max = Mathf.Max(max, v);
            }
        }
        float range = Mathf.Max(0.0001f, max - min);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, (buf[y * S + x] - min) / range));
        _groundMottle = Finish(tex, S / 3f);
        return _groundMottle;
    }

    // Lattice hash that wraps at n so the noise tiles seamlessly.
    private static float MottleHash(int x, int y, int n, int octave)
    {
        x = ((x % n) + n) % n; y = ((y % n) + n) % n;
        uint h = (uint)(x * 374761393 + y * 668265263 + octave * 1274126177 + 0x9E3779B9);
        h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    /// <summary>1x256 white ramp: alpha 1 at the BOTTOM easing to 0 at the top. World size
    /// (1/16 x 16) at 16 ppu - scale to fit. Tint via renderer colour (its alpha caps the ramp).</summary>
    public static Sprite AlphaRamp()
    {
        if (_alphaRamp != null) return _alphaRamp;

        const int H = 256;
        Texture2D tex = NewTexture(1, H);
        for (int y = 0; y < H; y++)
        {
            float t = (float)y / (H - 1);                 // 0 bottom -> 1 top
            float a = Mathf.Pow(1f - t, 1.4f);            // eased: fully opaque bottom, soft top
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
        }
        _alphaRamp = Finish(tex, 16f);
        return _alphaRamp;
    }

    /// <summary>Soft white fog blob: an ellipse with a smooth radial falloff, 2 x 1 world units
    /// at native scale. Tint + alpha via renderer colour; scale per wisp.</summary>
    public static Sprite SoftBlob()
    {
        if (_softBlob != null) return _softBlob;

        const int W = 128, H = 64;
        Texture2D tex = NewTexture(W, H);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float nx = (x + 0.5f) / W * 2f - 1f;
                float ny = (y + 0.5f) / H * 2f - 1f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float a = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.25f, 1f, r));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        _softBlob = Finish(tex, 64f);
        return _softBlob;
    }
}
