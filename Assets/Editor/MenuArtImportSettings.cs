using UnityEditor;
using UnityEngine;

// Auto-configures menu/chapter art as UI sprites, so adding new chapter/level imagery is a pure
// file-drop with no manual import setup and no hand-authored .meta. Covers two folders:
//   - Assets/Art/Chapters/**   chapter backgrounds + per-level thumbnails (opaque JPGs)
//   - Assets/Resources/Menu/**  currency/HUD icons loaded by Resources path (transparent PNGs)
// Sibling of BlockSkinImportSettings (block art) and AudioImportSettings. The folder layout and
// naming conventions live in images.md.
public sealed class MenuArtImportSettings : AssetPostprocessor
{
    // Bump whenever the import logic changes so already-imported art reimports with the new
    // settings instead of keeping cached results.
    public override uint GetVersion() => 1;

    private void OnPreprocessTexture()
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Art/Chapters/") && !path.Contains("/Resources/Menu/")) return;

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;   // harmless for opaque art, correct for the icons
        importer.mipmapEnabled = false;         // UI sprites are never minified

        // Full-rect mesh + centred pivot: the menu clips these to rounded masks and screen-locks
        // the frosted-glass copies, both of which assume the whole rect is present.
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
    }
}
