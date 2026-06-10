using UnityEditor;

// Generated skin textures under any "Skins" folder are import-configured automatically,
// so regenerating art stays a pure file-drop with no manual setup:
// - piece_*.png : whole-tetromino sprites from Tools/generate_piece_sprites.py (256 px/cell)
// - ground*.png : floor/mountain sprites from Tools/generate_ground_sprite.py (128 px/unit)
public sealed class BlockSkinImportSettings : AssetPostprocessor
{
    // Bump whenever the import logic changes so already-imported skin textures reimport
    // with the new settings instead of keeping cached results.
    public override uint GetVersion() => 2;

    private void OnPreprocessTexture()
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Skins/")) return;

        string fileName = System.IO.Path.GetFileName(path);
        float pixelsPerUnit;
        if (fileName.StartsWith("piece_")) pixelsPerUnit = 256f;
        else if (fileName.StartsWith("ground")) pixelsPerUnit = 128f;
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
    }
}
