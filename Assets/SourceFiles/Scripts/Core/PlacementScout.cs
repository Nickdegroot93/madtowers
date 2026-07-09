using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SILENT skill detector (JUICE.md Phase 3). Watches every locked piece and mints coins for
/// a deliberately TINY set of events. Its only feedback is the coin flight (CoinHud) plus the
/// RewardSheenFx reflection across the earning bricks - no sound, no flash, no shake:
///
///   PERFECT STACK - same shape, same orientation, placed exactly on its twin: +5.
///   GOLDEN BRICK  - the scheduled golden piece (GoldenBlockDirector) landed without
///                   toppling: +10; landed as a perfect stack: +40 total.
///
/// Economy history (July 2026, keep this): earlier versions also paid perfect fits, pair
/// interlocks and completed rows. Playtest verdict - gold appeared constantly (rows alone
/// paid every ~5 bricks on a wide tower) and the per-100-bricks earn rate swung wildly with
/// tower shape. All three were CUT so the rate is controlled by the golden-brick scheduler
/// (~3 per 100 bricks) plus rare exact stacks. Do not re-add earn events without Nick, and
/// keep anything new rate-bounded, not geometry-emergent. An even earlier celebration system
/// (chimes/flashes/hit-stops) was rejected wholesale - never attach audiovisual fanfare here.
///
/// Runs on BlockLocked with the ComboDetector revalidate-after-settle pattern (PHYSICS.md I5):
/// a piece that shifts or tilts while settling earns nothing.
/// </summary>
public class PlacementScout : MonoBehaviour
{
    // Exactness gate for the stack reward: the piece must settle where it locked, dead
    // straight, on the column grid. Stricter than any gameplay tolerance on purpose.
    private const float MaxSettleDriftCells = 0.35f;
    private const float MaxTiltDegrees = 3f;
    private const float MaxColumnOffsetCells = 0.12f;

    // The golden brick only needs to LAND, not land perfectly - but a toppled/slid golden
    // brick still pays nothing (these mirror the old sloppy thresholds).
    private const float GoldenMaxDriftCells = 0.6f;
    private const float GoldenMaxTiltDegrees = 8f;

    // Perfect-stack geometry (ComboDetector family): the 0.94 collider footprint leaves a
    // ~0.06-cell gap between visually touching blocks, so tolerances must clear it.
    private const float StackMaxCenterOffsetCells = 0.15f;
    private const float StackContactToleranceCells = 0.2f;
    private const float StackSizeToleranceCells = 0.2f;

    private const float SettleRevalidationMargin = 0.1f;

    private readonly List<Vector2> _cells = new List<Vector2>(8);
    private readonly List<BlockController> _sheenBlocks = new List<BlockController>(2);

    private float SettleRevalidationDelay
    {
        get
        {
            GameModeConfig config = GameManager.Instance != null ? GameManager.Instance.ActiveConfig : null;
            return (config != null ? config.SettleTime : 0.35f) + SettleRevalidationMargin;
        }
    }

    private void OnEnable()
    {
        GameEvents.BlockLocked += HandleBlockLocked;
    }

    private void OnDisable()
    {
        GameEvents.BlockLocked -= HandleBlockLocked;
    }

    private void HandleBlockLocked(BlockController block)
    {
        if (block == null) return;
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        StartCoroutine(ScoutAfterSettle(block, block.transform.position));
    }

    private IEnumerator ScoutAfterSettle(BlockController block, Vector2 lockPosition)
    {
        yield return new WaitForSeconds(SettleRevalidationDelay); // scaled: pauses defer it

        if (GameManager.Instance == null || GameManager.Instance.isGameOver) yield break;
        if (block == null || !block.HasLanded) yield break; // destroyed while settling

        float grid = Mathf.Max(0.01f, block.GridSpacing);
        float drift = (((Vector2)block.transform.position) - lockPosition).magnitude / grid;
        float angle = block.transform.eulerAngles.z;
        float tilt = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 90f) * 90f));

        bool golden = GoldenBlockDirector.TryConsume(block);

        // Stack reward needs full exactness (drift, tilt, column-true).
        bool exact = drift <= MaxSettleDriftCells && tilt <= MaxTiltDegrees;
        if (exact)
        {
            block.GetWorldCellCenters(_cells);
            if (_cells.Count == 0) exact = false;
            else
            {
                float columnOffset = Mathf.Abs(_cells[0].x - Mathf.Round(_cells[0].x / grid) * grid) / grid;
                exact = columnOffset <= MaxColumnOffsetCells;
            }
        }

        BlockController partner = null;
        bool stacked = exact && IsPerfectStack(block, grid, out partner);

        int coins = 0;
        if (golden && drift <= GoldenMaxDriftCells && tilt <= GoldenMaxTiltDegrees)
        {
            // The golden brick is the scheduled earner: it pays for simply surviving its
            // landing, and skill multiplies it rather than gating it.
            coins = stacked ? CoinLedger.GoldenPerfectCoins : CoinLedger.GoldenCleanCoins;
        }
        else if (stacked)
        {
            coins = CoinLedger.PerfectStackCoins;
        }
        if (coins == 0) yield break;

        // The sheen is the wordless "this is what paid" explanation: one reflection sweeping
        // everything that earned as a single shape - golden earns sweep in gold.
        _sheenBlocks.Clear();
        _sheenBlocks.Add(block);
        if (stacked && partner != null) _sheenBlocks.Add(partner);
        RewardSheenFx.Play(_sheenBlocks, golden ? GoldenBlockDirector.GoldTint : (Color?)null);

        Vector3 burstFrom = block.TryGetWorldBounds(out Bounds bounds) ? bounds.center : block.transform.position;
        CoinLedger.Earn(coins, burstFrom);
    }

    // ---- perfect stack: same definition, same footprint, exactly on top -------------------

    private bool IsPerfectStack(BlockController block, float grid, out BlockController partner)
    {
        partner = null;
        BlockIdentity identity = block.GetComponent<BlockIdentity>();
        if (identity == null || identity.Definition == null) return false;
        if (!block.TryGetWorldBounds(out Bounds top)) return false;

        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController candidate = blocks[i];
            if (candidate == null || candidate == block || !candidate.HasLanded) continue;

            BlockIdentity candidateIdentity = candidate.GetComponent<BlockIdentity>();
            if (candidateIdentity == null || candidateIdentity.Definition != identity.Definition) continue;
            if (!candidate.TryGetWorldBounds(out Bounds bottom)) continue;

            // Same orientation = same bounds footprint; "exactly on" = near-exact X and
            // resting contact. Reward exactness, not mere overlap.
            if (Mathf.Abs(top.size.x - bottom.size.x) > StackSizeToleranceCells * grid) continue;
            if (Mathf.Abs(top.size.y - bottom.size.y) > StackSizeToleranceCells * grid) continue;
            if (Mathf.Abs(top.center.x - bottom.center.x) > StackMaxCenterOffsetCells * grid) continue;

            float verticalGap = top.min.y - bottom.max.y;
            if (verticalGap > -StackContactToleranceCells * grid &&
                verticalGap < StackContactToleranceCells * grid)
            {
                partner = candidate;
                return true;
            }
        }
        return false;
    }
}
