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
