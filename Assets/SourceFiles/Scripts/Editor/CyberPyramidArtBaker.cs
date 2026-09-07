using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Rebake the static NEXT sprite and Vault poster from the runtime shader.
/// Preserves their existing GUIDs, sprite pivot, dimensions and import settings.</summary>
public static class CyberPyramidArtBaker
{
    [MenuItem("Tools/MadTowers/Bake Cyber Pyramid Art")]
    public static void Bake()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[CyberPyramidArtBaker] Enter play mode before baking.");
            return;
        }
        var root = new GameObject("Cyber pyramid sprite bake");
        var material = new Material(Resources.Load<Shader>("CyberPyramid"));
        material.SetTexture("_HazardSurface", Resources.Load<Texture2D>("HazardSurface"));
        var target = new RenderTexture(832, 1088, 24, RenderTextureFormat.ARGB32);
        BlockDemoStage poster = null;
        try
        {
            root.transform.position = new Vector3(20000, 20000, 0);
            var camera = root.AddComponent<Camera>();
            camera.enabled = false; // render only the explicit bake, never an extra game frame
            camera.orthographic = true;
            camera.orthographicSize = 1088f / 512f; // original 256 pixels per world unit
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << BlockDemoStage.DemoLayer;
            camera.targetTexture = target;
            var face = new GameObject("Face");
            face.layer = BlockDemoStage.DemoLayer;
            face.transform.SetParent(root.transform, false);
            face.transform.localPosition = new Vector3(0, .35f, 10);
            face.transform.localScale = new Vector3(3.3f, 3.5f, 1);
            var sprite = face.AddComponent<SpriteRenderer>();
            sprite.sprite = RuntimeSprites.Square();
            sprite.sharedMaterial = material;
            camera.Render();
            Write(target, "Assets/Resources/Skins/Classic/piece_Pyramid.png");

            var data = AssetDatabase.LoadAssetAtPath<BlockData>("Assets/Data/Blocks/Pyramid.asset");
            poster = BlockDemoStage.OpenPose(data, null, 360);
            var image = poster.DetachTexture();
            try { Write(image, "Assets/Resources/VaultPosters/poster_Pyramid.png"); }
            finally { image.Release(); Object.Destroy(image); }
        }
        finally
        {
            if (poster != null) poster.Close();
            target.Release(); Object.Destroy(target);
            Object.Destroy(root); Object.Destroy(material);
        }
        AssetDatabase.ImportAsset("Assets/Resources/Skins/Classic/piece_Pyramid.png");
        AssetDatabase.ImportAsset("Assets/Resources/VaultPosters/poster_Pyramid.png");
        Debug.Log("[CyberPyramidArtBaker] Updated NEXT sprite and Vault poster.");
    }

    private static void Write(RenderTexture source, string path)
    {
        var previous = RenderTexture.active;
        var pixels = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = source;
            pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; Object.Destroy(pixels); }
    }
}
