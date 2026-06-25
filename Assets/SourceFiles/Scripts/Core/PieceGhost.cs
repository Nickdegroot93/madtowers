using UnityEngine;

/// <summary>
/// Builds a recoloured copy of a chapter piece sprite (the "next" preview's desaturated ghost, the
/// Hold bubble's white silhouette, ...). The load + readable-check + RGBA32 HideAndDontSave texture +
/// 256-PPU sprite scaffold is identical for every variant; only the recolour differs, so callers pass
/// a <paramref name="recolour"/> that mutates the pixel buffer in place (it may do a whole-image pass
/// first, e.g. normalise to the brightest pixel).
///
/// Callers keep their OWN per-instance cache and free results in OnDestroy: only generated textures
/// carry HideAndDontSave, and this returns the SOURCE sprite unchanged when its texture isn't readable,
/// so a teardown that guards on HideAndDontSave never destroys a shared source sprite.
/// </summary>
public static class PieceGhost
{
    public static Sprite Generate(string shape, System.Action<Color[]> recolour)
    {
        Sprite source = ChapterSkins.LoadPiece(shape);
        if (source == null || source.texture == null || !source.texture.isReadable) return source;

        Texture2D src = source.texture;
        Texture2D tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        Color[] pixels = src.GetPixels();
        recolour(pixels);
        tex.SetPixels(pixels);
        tex.Apply();

        Sprite ghost = Sprite.Create(tex, new Rect(0, 0, src.width, src.height), new Vector2(0.5f, 0.5f), 256f);
        ghost.hideFlags = HideFlags.HideAndDontSave;
        return ghost;
    }
}
