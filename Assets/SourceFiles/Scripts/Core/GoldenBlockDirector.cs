using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The golden brick scheduler (JUICE.md Phase 3) - the economy's metronome. Every 25-40
/// locked bricks (randomized inside the window, so the RATE is fixed but the MOMENT is not),
/// the next standard piece spawns golden: tinted gold and glinting with the reward sheen
/// while it falls. Land it upright for CoinLedger.GoldenCleanCoins; land it as a perfect
/// stack for GoldenPerfectCoins. Topple it and it pays nothing - one chance per brick.
///
/// Special variants (Magma etc.) are never goldified - golden is a plain brick with a fixed
/// look, same chapter-independence rule as every unique block.
/// </summary>
public class GoldenBlockDirector : MonoBehaviour
{
    public static readonly Color GoldTint = new Color(1f, 0.84f, 0.35f, 1f);

    private const int MinBricksBetween = 25;
    private const int MaxBricksBetween = 40;
    private const float FallingGlintInterval = 0.9f;

    private static GoldenBlockDirector _instance;

    private BlockController _golden; // the live golden piece; cleared when judged
    private int _dueIn;              // locked bricks remaining until the next golden arms
    private bool _armed;             // countdown done: goldify the next standard spawn
    private float _nextGlintTime;
    private readonly List<BlockController> _glintList = new List<BlockController>(1);

    /// <summary>Called once per judged placement (PlacementScout): true when this block is
    /// the live golden brick. Consumes it - a golden brick gets exactly one verdict.</summary>
    public static bool TryConsume(BlockController block)
    {
        if (_instance == null || block == null || _instance._golden != block) return false;
        _instance._golden = null;
        return true;
    }

    private void OnEnable()
    {
        _instance = this;
        _golden = null;
        _armed = false;
        _dueIn = Random.Range(MinBricksBetween, MaxBricksBetween + 1);
        GameEvents.BlockLocked += HandleBlockLocked;
        GameEvents.BlockSpawned += HandleBlockSpawned;
    }

    private void OnDisable()
    {
        GameEvents.BlockLocked -= HandleBlockLocked;
        GameEvents.BlockSpawned -= HandleBlockSpawned;
        if (_instance == this) _instance = null;
    }

    private void HandleBlockLocked(BlockController block)
    {
        _dueIn--;
        if (_dueIn <= 0 && _golden == null) _armed = true;
    }

    private void HandleBlockSpawned(BlockController block, BlockData variant)
    {
        if (!_armed || block == null) return;
        // Never goldify a SPECIAL brick - wait for a plain one. "Special" = a behaviour
        // subclass (Magma, Maw, ...) or any custom look/accounting. Ordinary spawns arrive
        // with the plain base "Normal" BlockData, NOT null.
        if (variant != null && (variant.GetType() != typeof(BlockData) ||
            variant.SpriteOverride != null || variant.MaterialOverride != null ||
            variant.IsHazard || !variant.CountsAsPlacedBlock)) return;

        _armed = false;
        _dueIn = Random.Range(MinBricksBetween, MaxBricksBetween + 1);
        _golden = block;
        _nextGlintTime = Time.time; // first glint immediately: the reveal

        // A gold OVERLAY, not a tint: tinting multiplies the chapter's colored piece art
        // (a green S-piece times gold = still green). The overlay is the same sprite drawn
        // gold on top, so every shape in every chapter reads unmistakably golden - and a
        // landed golden brick stays golden in the tower.
        Transform skin = block.PieceSkinTransform;
        SpriteRenderer skinRenderer = skin != null ? skin.GetComponent<SpriteRenderer>() : null;
        if (skinRenderer != null && skinRenderer.sprite != null)
        {
            GameObject overlayGo = new GameObject("GoldenOverlay");
            overlayGo.transform.SetParent(skin, false);
            SpriteRenderer overlay = overlayGo.AddComponent<SpriteRenderer>();
            overlay.sprite = skinRenderer.sprite;
            overlay.sortingLayerID = skinRenderer.sortingLayerID;
            overlay.sortingOrder = skinRenderer.sortingOrder + 1;
            overlay.color = new Color(GoldTint.r, GoldTint.g, GoldTint.b, 0.8f);
        }
    }

    private void Update()
    {
        // Recurring glints while the golden piece falls - the anticipation cue. Stops at
        // landing; the judge's own gold sheen takes over if it pays.
        if (_golden == null || _golden.HasLanded) return;
        if (Time.time < _nextGlintTime) return;

        _nextGlintTime = Time.time + FallingGlintInterval;
        _glintList.Clear();
        _glintList.Add(_golden);
        RewardSheenFx.Play(_glintList, GoldTint);
    }
}
