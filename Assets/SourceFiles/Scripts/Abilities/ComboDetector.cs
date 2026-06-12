using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Detects combo triggers on the tower. Runs ONLY when a block locks (locality: the
/// just-locked block against existing landed blocks), and only for triggers some owned
/// ComboAbility actually subscribes to - zero cost while no combo ability is owned.
///
/// Lock is not settled (PHYSICS.md I5 separates them): a candidate match is recorded at
/// lock and revalidated after the settle window before firing, so a pair that topples
/// straight away never rewards. Participating blocks are consumed per trigger (a
/// 3-stack fires once: the third block can pair with neither consumed one).
/// </summary>
public class ComboDetector : MonoBehaviour
{
    // Collider footprints are 0.94 of the cell (PHYSICS.md I4), so visually-touching
    // stacked blocks have a real ~0.06-cell gap; the contact tolerance must clear it.
    private const float StackContactTolerance = 0.2f;
    private const float MinHorizontalOverlap = 0.3f;     // cells of shared footprint
    private const float OrientationAspectThreshold = 1.5f; // bounds h/w beyond this = vertical
    // Margin past the mode's settle window; the delay derives from config (settleTime is
    // a per-mode tunable - a hardcoded delay would silently undershoot harder modes).
    private const float SettleRevalidationMargin = 0.1f;

    private float SettleRevalidationDelay
    {
        get
        {
            GameModeConfig config = GameManager.Instance != null ? GameManager.Instance.ActiveConfig : null;
            return (config != null ? config.SettleTime : 0.35f) + SettleRevalidationMargin;
        }
    }

    private AbilityRuntime _runtime;
    private readonly Dictionary<ComboTriggerDefinition, HashSet<EntityId>> _consumedBlocks =
        new Dictionary<ComboTriggerDefinition, HashSet<EntityId>>();

    private void Awake()
    {
        _runtime = GetComponent<AbilityRuntime>();
    }

    private void OnEnable()
    {
        GameEvents.BlockSpawned += HandleBlockSpawned;
    }

    private void OnDisable()
    {
        GameEvents.BlockSpawned -= HandleBlockSpawned;
    }

    // Each spawned piece reports its own lock; the handler captures the controller so
    // the detector knows WHICH block locked (the global score event does not carry it).
    private void HandleBlockSpawned(BlockController block, BlockData variant)
    {
        if (block == null) return;
        block.OnBlockLocked += () => HandleBlockLocked(block);
    }

    private void HandleBlockLocked(BlockController block)
    {
        if (_runtime == null || block == null) return;

        IReadOnlyList<ComboTriggerDefinition> triggers = _runtime.SubscribedTriggers;
        for (int i = 0; i < triggers.Count; i++)
        {
            ComboTriggerDefinition trigger = triggers[i];
            if (trigger == null || IsConsumed(trigger, block)) continue;

            BlockController baseBlock = FindMatch(trigger, block);
            if (baseBlock != null)
            {
                StartCoroutine(RevalidateAndFire(trigger, block, baseBlock));
            }
        }
    }

    private IEnumerator RevalidateAndFire(ComboTriggerDefinition trigger, BlockController newBlock, BlockController baseBlock)
    {
        yield return new WaitForSeconds(SettleRevalidationDelay); // scaled: pauses defer it

        // A combo locked just before the final loss must not fire into the game-over screen.
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) yield break;
        if (newBlock == null || baseBlock == null) yield break;
        if (IsConsumed(trigger, newBlock) || IsConsumed(trigger, baseBlock)) yield break;
        if (!Matches(trigger, newBlock, baseBlock)) yield break; // toppled during the wait

        // Everything the match needs is computed BEFORE consumption: a failed bounds
        // read must not burn the blocks for this trigger with no ability having fired.
        if (!newBlock.TryGetWorldBounds(out Bounds bounds)) yield break;
        if (baseBlock.TryGetWorldBounds(out Bounds baseBounds)) bounds.Encapsulate(baseBounds);
        float topY = Mathf.Max(newBlock.GetHighestCellY(), baseBlock.GetHighestCellY());

        Consume(trigger, newBlock);
        Consume(trigger, baseBlock);

        _runtime.HandleComboFired(trigger, new ComboMatch(newBlock, baseBlock, bounds, topY));
    }

    // The just-locked block against every landed candidate of the right shape. Linear
    // scan is fine: locks are seconds apart and the bounds reads are the same class of
    // work the loss sweep already does at 10 Hz.
    private BlockController FindMatch(ComboTriggerDefinition trigger, BlockController newBlock)
    {
        if (!BlockMatchesShape(trigger, newBlock)) return null;

        IReadOnlyList<BlockController> blocks = BlockController.AllBlocks;
        for (int i = 0; i < blocks.Count; i++)
        {
            BlockController candidate = blocks[i];
            if (candidate == null || candidate == newBlock || !candidate.HasLanded) continue;
            if (IsConsumed(trigger, candidate)) continue;
            if (!BlockMatchesShape(trigger, candidate)) continue;
            if (Matches(trigger, newBlock, candidate)) return candidate;
        }

        return null;
    }

    private static bool BlockMatchesShape(ComboTriggerDefinition trigger, BlockController block)
    {
        if (trigger.RequiredDefinition == null) return false;

        BlockIdentity identity = block.GetComponent<BlockIdentity>();
        if (identity == null || identity.Definition != trigger.RequiredDefinition) return false;

        if (trigger.Orientation == ComboOrientation.Any) return true;
        if (!block.TryGetWorldBounds(out Bounds bounds)) return false;

        bool vertical = bounds.size.y > bounds.size.x * OrientationAspectThreshold;
        bool horizontal = bounds.size.x > bounds.size.y * OrientationAspectThreshold;
        return trigger.Orientation == ComboOrientation.Vertical ? vertical : horizontal;
    }

    private bool Matches(ComboTriggerDefinition trigger, BlockController newBlock, BlockController baseBlock)
    {
        if (!BlockMatchesShape(trigger, newBlock) || !BlockMatchesShape(trigger, baseBlock)) return false;
        if (!newBlock.TryGetWorldBounds(out Bounds top) || !baseBlock.TryGetWorldBounds(out Bounds bottom)) return false;

        switch (trigger.Relation)
        {
            case ComboRelation.StackedDirectlyOn:
                float grid = newBlock.GridSpacing;
                float horizontalOverlap = Mathf.Min(top.max.x, bottom.max.x) - Mathf.Max(top.min.x, bottom.min.x);
                if (horizontalOverlap < MinHorizontalOverlap * grid) return false;

                float verticalGap = top.min.y - bottom.max.y;
                return verticalGap > -StackContactTolerance * grid &&
                       verticalGap < StackContactTolerance * grid;
            default:
                return false;
        }
    }

    private bool IsConsumed(ComboTriggerDefinition trigger, BlockController block)
    {
        return _consumedBlocks.TryGetValue(trigger, out HashSet<EntityId> set) &&
               set.Contains(block.GetEntityId());
    }

    private void Consume(ComboTriggerDefinition trigger, BlockController block)
    {
        if (!_consumedBlocks.TryGetValue(trigger, out HashSet<EntityId> set))
        {
            set = new HashSet<EntityId>();
            _consumedBlocks[trigger] = set;
        }
        set.Add(block.GetEntityId());
    }
}
