using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the Vault grid's brick posters to PNGs under <c>Resources/VaultPosters</c>, so the Vault
/// loads finished images instead of rendering 14 live dioramas the moment the tab opens
/// (BLOCKPREVIEWS.md "baked posters"). Same pipeline shape as the ability icons: an editor tool
/// produces committed art, the runtime just loads it.
///
/// WHY this exists. <see cref="VaultPosterService"/> used to be the only path: one
/// <see cref="BlockDemoStage"/> per discovered variant, each with its own camera and 360x360
/// RenderTexture, all built at once, all captured after a 0.65 s scaled-time warm-up (the menu
/// holds timeScale = 0 and skins animate on scaled time, so a same-frame capture shows Vine
/// half-grown and the Maw mid-blink). That is ~14 cameras and ~7 MB of render textures for a
/// stall the player reads as a broken image, repeated after every single run because the cache is
/// released with the menu (Nick, 2026-07-29: "it takes quite a while... it seems like it's
/// generated on the spot"). Baking removes the cameras, the VRAM, the stall AND the timeScale
/// flip - which was itself a hazard, since any scaled-time menu animation (unlock reveals, the
/// chapter cross-fade) would lurch forward during the warm-up window.
///
/// PLAY MODE IS REQUIRED. The poses are only correct once they have ANIMATED: the Maw's grin, the
/// Vine's grow-in and the time-driven Magma/Maw shaders all need real frames, and MonoBehaviour
/// Update does not tick in edit mode. So: press Play (the menu scene is fine), run
/// Tools > MadTowers > Bake Vault Posters, wait for the log, exit Play. Nothing about the bake
/// touches the save file or the live simulation - the diorama puppets are the usual isolated
/// BlockDemoPuppet sandbox, built 1000 units away from anything.
///
/// RE-RUN IT when a variant's skin, shader or poster pose changes, or when a variant is added -
/// the same discipline the icon set has (ICONS.md). A variant with no baked poster still works:
/// <see cref="VaultPosterService"/> falls back to the live render and logs which ids are missing.
/// </summary>
public static class VaultPosterBaker
{
    // The folder/prefix contract is owned by the RUNTIME side (VaultPosterService), which is the
    // half that has to find these files in a player - so there is exactly one definition and a
    // rename cannot leave the loader looking in the old place.
    private const string AssetFolder = "Assets/Resources/" + VaultPosterService.BakedResourceFolder;

    // Matches VaultPosterService's live-render size, so a baked poster and a fallback render are
    // visually interchangeable.
    private const int PosterPixels = 360;

    // Longer than the runtime warm-up: a bake happens once, offline, and the only thing worse than
    // a slow bake is a poster caught mid-animation.
    private const float WarmUpSeconds = 1.25f;

    private static readonly List<(string id, BlockDemoStage stage)> _batch =
        new List<(string, BlockDemoStage)>();
    private static float _startRealtime;
    private static float _previousTimeScale;

    [MenuItem("Tools/MadTowers/Bake Vault Posters")]
    public static void Bake()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Bake Vault Posters",
                "Enter Play mode first.\n\nThe poses have to animate before they can be captured " +
                "(the Maw's grin, the Vine's grow-in, the time-driven shaders), and Update does " +
                "not tick in edit mode.\n\nPress Play - the menu scene is fine - then run this " +
                "again.", "OK");
            return;
        }
        if (_batch.Count > 0)
        {
            Debug.LogWarning("[VaultPosterBaker] A bake is already running.");
            return;
        }

        Directory.CreateDirectory(AssetFolder);

        // Every entry the Vault can show, discovered or not - the bake is authoring, not progress:
        // a poster must exist before the player discovers the brick. Mirrors the Vault's own
        // "Normal first, then the catalog" order (MainMenuRuntime.Vault.BrickEntries).
        var entries = new List<BlockData> { ContentCatalog.NormalVariant() };
        entries.AddRange(ContentCatalog.AllVariants());

        var seen = new HashSet<string>();
        foreach (BlockData variant in entries)
        {
            string id = ProgressStore.BlockId(variant);
            if (!seen.Add(id)) continue; // NormalVariant() can also appear in AllVariants()
            // Chapter is deliberately null: posters are neutral studio shots, chapter-independent
            // by design (BlockDemoStage.OpenPose), which is what lets ONE bake serve 15 chapters.
            _batch.Add((id, BlockDemoStage.OpenPose(variant, null, PosterPixels)));
        }

        if (_batch.Count == 0)
        {
            Debug.LogWarning("[VaultPosterBaker] No brick variants found - nothing to bake.");
            return;
        }

        // All dioramas warm up together in ONE window (they hold separate stage slots, so their
        // physics can never meet) - the bake costs one wait, not one per brick.
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 1f;
        _startRealtime = Time.realtimeSinceStartup;
        EditorApplication.update += Tick;
    }

    // Real-time sampler rather than a coroutine: the tool lives in an editor static class, and
    // EditorApplication.update ticks while play mode runs the frames the poses need.
    private static void Tick()
    {
        float elapsed = Time.realtimeSinceStartup - _startRealtime;
        if (elapsed < WarmUpSeconds && EditorApplication.isPlaying)
        {
            EditorUtility.DisplayProgressBar("Baking Vault posters",
                $"Warming up {_batch.Count} poses...", elapsed / WarmUpSeconds);
            return;
        }

        EditorApplication.update -= Tick;
        EditorUtility.ClearProgressBar();

        // Leaving play mode mid-bake is not an error worth shouting about, but the half-built
        // dioramas die with the scene and nothing was written - say so and stop.
        if (!EditorApplication.isPlaying)
        {
            _batch.Clear();
            Debug.LogWarning("[VaultPosterBaker] Play mode ended during the warm-up - nothing written.");
            return;
        }

        var written = new List<string>();
        foreach ((string id, BlockDemoStage stage) in _batch)
        {
            if (stage == null) continue;
            RenderTexture poster = stage.DetachTexture(); // guarantees one final fresh frame
            if (poster != null)
            {
                string path = $"{AssetFolder}/{VaultPosterService.BakedFilePrefix}{id}.png";
                File.WriteAllBytes(path, Encode(poster));
                written.Add(path);
                poster.Release();
                Object.DestroyImmediate(poster);
            }
            stage.Close();
        }
        _batch.Clear();
        if (Application.isPlaying) Time.timeScale = _previousTimeScale;

        AssetDatabase.Refresh();
        foreach (string path in written) ApplyImportSettings(path);
        AssetDatabase.SaveAssets();

        ReportOrphans(written);
        Debug.Log($"[VaultPosterBaker] Baked {written.Count} poster(s) to {AssetFolder}. " +
                  "Exit Play mode and open the Vault - the grid should fill instantly.");
    }

    private static byte[] Encode(RenderTexture source)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = source;
        var readback = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readback.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
        readback.Apply();
        RenderTexture.active = previous;

        byte[] png = readback.EncodeToPNG();
        Object.DestroyImmediate(readback);
        return png;
    }

    // UI-facing single texture: no mipmaps (never minified below its cell), clamped (a rounded
    // mask samples the edge), alpha preserved so the studio backdrop's soft falloff survives.
    private static void ApplyImportSettings(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 512;
        importer.SaveAndReimport();
    }

    // A renamed or deleted variant leaves a poster behind that nothing will ever load. Report,
    // never auto-delete: art in the repo is Nick's to remove.
    private static void ReportOrphans(List<string> written)
    {
        var kept = new HashSet<string>(written);
        var orphans = new List<string>();
        foreach (string path in Directory.GetFiles(AssetFolder, VaultPosterService.BakedFilePrefix + "*.png"))
        {
            string normalized = path.Replace('\\', '/');
            if (!kept.Contains(normalized)) orphans.Add(Path.GetFileName(normalized));
        }
        if (orphans.Count > 0)
        {
            Debug.LogWarning($"[VaultPosterBaker] {orphans.Count} poster(s) match no current " +
                             $"variant and can be deleted: {string.Join(", ", orphans)}");
        }
    }
}
