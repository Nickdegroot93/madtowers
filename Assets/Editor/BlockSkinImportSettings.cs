using UnityEditor;
using UnityEngine;

// Generated skin textures under any "Skins" folder are import-configured automatically,
// so regenerating art stays a pure file-drop with no manual setup:
// - piece_*.png  : whole-tetromino sprites (256 px/cell)
// - plateau*.png : the landable strip from Tools/generate_ground_sprite.py (128 px/unit)
// - laser*.png   : optional themed limit-line strip (128 px/unit)
public sealed class BlockSkinImportSettings : AssetPostprocessor
{
    // Bump whenever the import logic changes so already-imported skin textures reimport
    // with the new settings instead of keeping cached results.
    public override uint GetVersion() => 6;

    private void OnPreprocessTexture()
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Skins/")) return;

        string fileName = System.IO.Path.GetFileName(path);
        float pixelsPerUnit;
        if (fileName.StartsWith("piece_")) pixelsPerUnit = 256f;
        else if (fileName.StartsWith("plateau") || fileName.StartsWith("laser")) pixelsPerUnit = 128f;
        else return;

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        // Piece textures stay CPU-readable so the HUD can build the desaturated
        // "next up" ghost from them at runtime.
        importer.isReadable = fileName.StartsWith("piece_");

        // Tiled draw mode (the plateau strip) requires a full-rect sprite mesh.
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        // Plateau strips have outlined end caps baked into the texture; the border keeps
        // them at the strip's ends while the middle tiles.
        if (fileName.StartsWith("plateau"))
        {
            importer.spriteBorder = new Vector4(12f, 0f, 12f, 0f);
        }
    }
}
