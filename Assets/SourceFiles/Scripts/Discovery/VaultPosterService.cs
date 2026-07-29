using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Supplies the Vault grid's static brick showcases ("posters"). TWO paths, in this order:
///
/// 1. BAKED (the shipping path): a committed PNG per variant under Resources/VaultPosters,
///    produced by Tools > MadTowers > Bake Vault Posters (VaultPosterBaker). Assigning one is a
///    Resources.Load - the grid fills in the same frame it is built, with no cameras, no render
///    textures and no timeScale games.
/// 2. LIVE (the fallback): render the pose on the spot from a BlockDemoStage, as this service
///    always used to. Kept so a newly added variant is never a blank card before someone
///    re-bakes - it just costs what it always cost, and says so in the editor log.
///
/// The live path is what made the Vault feel broken (Nick, 2026-07-29: "it takes quite a while...
/// it seems like it's generated on the spot"). It is genuinely expensive: one diorama per
/// discovered variant, each with its own camera and 360x360 RenderTexture, ~14 of them at once on
/// a full save, and a hard warm-up window before ANY of them can be captured - the menu holds
/// Time.timeScale = 0 while skins animate on SCALED time (BLOCKVARIANTS.md), so a same-frame
/// capture renders Vine half-grown and the Maw mid-blink. Worse, the cache is released with the
/// menu, so every run paid it again. The warm-up also has to flip timeScale to 1, which lurches
/// any scaled-time menu animation (unlock reveals, the chapter cross-fade) that happens to be
/// mid-flight. Baking exists to keep all of that off the player's path.
/// </summary>
public sealed class VaultPosterService : MonoBehaviour
{
    private const int PosterPixels = 360;
    private const float WarmUpSeconds = 0.65f;

    /// <summary>Where baked posters live, Resources-relative. Declared HERE, not on the editor-only
    /// baker: runtime code cannot see the Editor assembly, so the runtime side has to own the
    /// contract and VaultPosterBaker reads it from us.</summary>
    public const string BakedResourceFolder = "VaultPosters";
    public const string BakedFilePrefix = "poster_";

    // Keyed by variant id alone: posters are chapter-INDEPENDENT studio shots (neutral backdrop,
    // Classic brick skin - see BlockDemoStage.OpenPose), so one render serves every chapter.
    private static readonly Dictionary<string, RenderTexture> Cache = new Dictionary<string, RenderTexture>();

    // Baked lookups, including the misses (a null value means "asked Resources, not there") so a
    // grid rebuild cannot re-probe the same absent poster on every card.
    private static readonly Dictionary<string, Texture2D> Baked = new Dictionary<string, Texture2D>();

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

        Texture2D baked = BakedPoster(key);
        if (baked != null)
        {
            target.texture = baked;
            target.color = Color.white;
            return;
        }

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

    /// <summary>The baked PNG for a variant id, or null when none is committed yet. Resources
    /// caches the load itself; the local dictionary only exists to remember the MISSES.</summary>
    private static Texture2D BakedPoster(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (Baked.TryGetValue(key, out Texture2D known)) return known;

        var loaded = Resources.Load<Texture2D>($"{BakedResourceFolder}/{BakedFilePrefix}{key}");
        Baked[key] = loaded;
#if UNITY_EDITOR
        if (loaded == null)
        {
            Debug.LogWarning($"[VaultPoster] No baked poster for '{key}' - falling back to a live " +
                             "render (slow, and it flips timeScale). Run Tools > MadTowers > " +
                             "Bake Vault Posters in Play mode.");
        }
#endif
        return loaded;
    }

    /// <summary>Free every LIVE-RENDERED poster (menu teardown - mirrors the menu's video
    /// texture). Baked posters are project assets: they are never released here, only forgotten,
    /// because releasing one would destroy the asset itself.</summary>
    public static void ReleaseAll()
    {
        foreach (RenderTexture rt in Cache.Values)
        {
            if (rt == null) continue;
            rt.Release();
            Destroy(rt);
        }
        Cache.Clear();
        Baked.Clear();
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
