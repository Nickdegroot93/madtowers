using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders and caches the Vault grid's static brick showcases ("posters"): one small
/// RenderTexture per discovered variant, captured from a BlockDemoStage pose. BLOCKPREVIEWS.md's
/// codex-performance answer - pre-baked first frames instead of live demos per card.
///
/// The wrinkle this service exists for: the menu holds Time.timeScale = 0, and skins animate on
/// SCALED time (BLOCKVARIANTS.md rule), so time-driven looks (Vine's grow-in, the Maw's waking
/// grin) would render half-born in a same-frame capture. Each capture therefore gets a short
/// scaled-time warm-up window (timeScale briefly 1 - safe: the menu only exists while level
/// selection is pending, and it covers the whole screen), after which the frame is detached and
/// the diorama destroyed. Requesting RawImages pop in as their posters land.
/// </summary>
public sealed class VaultPosterService : MonoBehaviour
{
    private const int PosterPixels = 360;
    private const float WarmUpSeconds = 0.65f;

    // Keyed by variant id alone: posters are chapter-INDEPENDENT studio shots (neutral backdrop,
    // Classic brick skin - see BlockDemoStage.OpenPose), so one render serves every chapter.
    private static readonly Dictionary<string, RenderTexture> Cache = new Dictionary<string, RenderTexture>();

    private static VaultPosterService _instance;

    private readonly Queue<(BlockData variant, ChapterDefinition chapter, string key)> _queue =
        new Queue<(BlockData, ChapterDefinition, string)>();
    private readonly Dictionary<string, List<RawImage>> _waiting = new Dictionary<string, List<RawImage>>();
    private bool _working;

    /// <summary>Give <paramref name="target"/> the variant's poster: instantly when cached,
    /// otherwise queued for capture (the image is filled in when it lands).</summary>
    public static void Assign(BlockData variant, ChapterDefinition chapter, RawImage target)
    {
        if (target == null) return;
        string key = ProgressStore.BlockId(variant);

        if (Cache.TryGetValue(key, out RenderTexture cached) && cached != null)
        {
            target.texture = cached;
            target.color = Color.white;
            return;
        }

        if (_instance == null)
        {
            var go = new GameObject("VaultPosterService");
            _instance = go.AddComponent<VaultPosterService>();
        }

        if (!_instance._waiting.TryGetValue(key, out List<RawImage> list))
        {
            _instance._waiting[key] = list = new List<RawImage>();
            _instance._queue.Enqueue((variant, chapter, key));
        }
        list.Add(target);
        if (!_instance._working) _instance.StartCoroutine(_instance.Work());
    }

    /// <summary>Free every cached poster (menu teardown - mirrors the menu's video texture).</summary>
    public static void ReleaseAll()
    {
        foreach (RenderTexture rt in Cache.Values)
        {
            if (rt == null) continue;
            rt.Release();
            Destroy(rt);
        }
        Cache.Clear();
        if (_instance != null)
        {
            Destroy(_instance.gameObject);
            _instance = null;
        }
    }

    private IEnumerator Work()
    {
        _working = true;
        // One frame so every Assign from the same grid-build lands in the first batch - the
        // grid requests all its posters in one loop, and they should cost ONE warm-up.
        yield return null;

        while (_queue.Count > 0)
        {
            // Drain everything queued so far and warm ALL the dioramas together in a single
            // scaled-time window (stage slots keep their physics apart). This is what stops
            // the Vault grid loading like a slow web page: N posters, one 0.65s wait.
            var batch = new List<(string key, BlockDemoStage stage)>();
            while (_queue.Count > 0)
            {
                (BlockData variant, ChapterDefinition chapter, string key) = _queue.Dequeue();
                batch.Add((key, BlockDemoStage.OpenPose(variant, chapter, PosterPixels)));
            }

            // The warm-up window. Restore whatever the menu had (0 while it is up; if a level
            // got selected mid-capture the game already runs at 1 and we leave it).
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            yield return new WaitForSeconds(WarmUpSeconds);
            if (LevelSelectionState.IsSelectionPending) Time.timeScale = previousTimeScale;

            foreach ((string key, BlockDemoStage stage) in batch)
            {
                RenderTexture poster = stage.DetachTexture();
                stage.Close();

                Cache[key] = poster;
                if (_waiting.TryGetValue(key, out List<RawImage> targets))
                {
                    foreach (RawImage target in targets)
                    {
                        if (target == null) continue;
                        target.texture = poster;
                        target.color = Color.white;
                    }
                    _waiting.Remove(key);
                }
            }
        }
        _working = false;
    }
}
