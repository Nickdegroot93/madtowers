using UnityEngine;

/// <summary>
/// Consumable that shatters the active falling piece into one independent 1x1 shard per cell
/// (a tetromino -> 4, a domino -> 2). The shards lift to the spawn line and HOVER; the player
/// aims one left/right and commits it with a downward flick, dropping them one at a time while
/// the rest float in a queue (FissionSession owns that aftermath). Each shard is a real Pip
/// brick, so it counts and risks a life like any placed block - a tetromino becomes four
/// counting placements, letting the player pepper several tight gaps instead of fighting one
/// awkward shape.
/// </summary>
[CreateAssetMenu(fileName = "Fission", menuName = "Stacking/Abilities/Fission")]
public class FissionAbility : ConsumableAbility
{
    [Tooltip("The 1x1 brick each cell becomes (Block_Pip's BlockDefinition).")]
    [SerializeField] private BlockDefinition pipDefinition;

    [Header("Shatter FX (swappable)")]
    [Tooltip("Bursts from every cell of the piece as it shatters (a base CFXR prefab - never a variant).")]
    [SerializeField] private GameObject splitEffect;
    [Tooltip("Per-cell scale for the shatter effect - CFXR effects are character-sized, a cell wants < 1.")]
    [SerializeField] private float splitScale = 0.6f;

    // The slot is consumed BEFORE Activate, so refuse every way the session could fail here:
    // no live active piece in the air / one mid-lock / past the loss line / Pip unwired / a piece
    // that is already a Pip (CanTransmuteActivePiece covers all of those), plus the Fission-specific
    // gates: at least two cells to split, and no session already running.
    public override bool CanActivate(AbilityContext context)
    {
        if (FissionSession.IsActive) return false;
        if (!AbilityEffects.CanTransmuteActivePiece(context, pipDefinition)) return false;
        return CountCells(BlockController.ActiveControlled) >= 2;
    }

    public override void Activate(AbilityContext context)
    {
        BlockController active = BlockController.ActiveControlled;
        if (active == null) return;

        int cells = CountCells(active);
        if (cells < 2) return;

        // Shatter the original from every cell BEFORE it is replaced (reads its colliders), then
        // hand off to the session which spawns shard #1 in its place and floats the rest.
        AbilityEffects.BurstFromEveryCell(active, splitEffect, splitScale);
        AbilityEffects.ImpactPunch();
        SfxPlayer.Play("impact_shatter_01", 0.85f, 0.06f);

        FissionSession.Begin(context.Spawner, pipDefinition, cells);
    }

    private static int CountCells(BlockController block)
    {
        if (block == null) return 0;
        return block.GetComponentsInChildren<BoxCollider2D>().Length;
    }
}
