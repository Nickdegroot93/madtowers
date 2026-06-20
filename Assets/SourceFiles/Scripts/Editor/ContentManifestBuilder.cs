using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Bakes the build-safe <see cref="ContentManifest"/> before every player build (and on demand via
/// Tools ▸ MadTowers ▸ Rebuild Content Manifest), running the same editor-side AssetDatabase
/// discovery <see cref="ContentCatalog"/> uses in the editor. This keeps the Custom Game testing
/// screen working in DEVELOPMENT builds — which have no AssetDatabase — with no hand-maintained
/// list (the CUSTOMGAME.md "new ability/block → nothing to do" rule still holds).
/// </summary>
public class ContentManifestBuilder : IPreprocessBuildWithReport
{
    private const string ResourcesDir = "Assets/Resources";
    private const string ManifestPath = ResourcesDir + "/ContentManifest.asset";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => Rebuild();

    [MenuItem("Tools/MadTowers/Rebuild Content Manifest")]
    public static void Rebuild()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesDir))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        ContentManifest manifest = AssetDatabase.LoadAssetAtPath<ContentManifest>(ManifestPath);
        if (manifest == null)
        {
            manifest = ScriptableObject.CreateInstance<ContentManifest>();
            AssetDatabase.CreateAsset(manifest, ManifestPath);
        }

        manifest.EditorPopulate(
            ContentCatalog.AllAbilities().ToArray(),
            ContentCatalog.AllBlocks().ToArray(),
            ContentCatalog.EqualRarityProfile());

        EditorUtility.SetDirty(manifest);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ContentManifest] Baked {manifest.Abilities.Length} abilities, {manifest.Blocks.Length} blocks.");
    }
}
