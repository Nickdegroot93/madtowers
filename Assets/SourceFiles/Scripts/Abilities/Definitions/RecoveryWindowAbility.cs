using UnityEngine;

/// <summary>
/// Stackable passive: after losing a life, the next N spawned blocks fall slower,
/// giving a clean window to recover rhythm. Stacks multiply the window length.
/// Count-based (not a timed status): the window is measured in blocks, so it follows
/// the player's pace. Instance fields are per-run state (the clone-on-acquire rule).
/// </summary>
[CreateAssetMenu(fileName = "RecoveryWindow", menuName = "Stacking/Abilities/Recovery Window")]
public class RecoveryWindowAbility : PassiveAbility
{
    [Range(0.1f, 1f)]
    [SerializeField] private float slowFactor = 0.5f;
    [Min(1)]
    [SerializeField] private int blocksPerTrigger = 2;

    private int _stacks;
    private int _remainingSlowBlocks;

    public override void OnAcquired(AbilityContext context, int stacks) => _stacks = stacks;
    public override void OnStackAdded(AbilityContext context, int stacks) => _stacks = stacks;

    public override bool OnLifeLost(AbilityContext context)
    {
        _remainingSlowBlocks = blocksPerTrigger * _stacks;
        return false; // permanent passive: triggering never consumes it
    }

    public override bool OnBlockSpawned(AbilityContext context, BlockController block, BlockData data)
    {
        if (_remainingSlowBlocks > 0) _remainingSlowBlocks--;
        return false;
    }

    public override float GetFallSpeedFactor(AbilityContext context, int stacks)
    {
        return _remainingSlowBlocks > 0 ? slowFactor : 1f;
    }
}
