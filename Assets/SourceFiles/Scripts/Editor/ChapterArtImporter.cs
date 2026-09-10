#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Shipping cutouts stay small, alpha-safe and GPU compressed on mobile.</summary>
public sealed class ChapterArtImporter : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/ChapterArt/", System.StringComparison.Ordinal)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 256f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.maxTextureSize = 512;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        foreach (string platform in new[] { "Android", "iPhone" })
        {
            var mobile = importer.GetPlatformTextureSettings(platform);
            mobile.name = platform;
            mobile.overridden = true;
            mobile.maxTextureSize = 512;
            mobile.format = TextureImporterFormat.ASTC_6x6;
            mobile.compressionQuality = 100;
            importer.SetPlatformTextureSettings(mobile);
        }
    }
}
#endif
