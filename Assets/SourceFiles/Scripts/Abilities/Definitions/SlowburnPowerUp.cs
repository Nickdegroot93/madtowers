using UnityEngine;

/// <summary>
/// Epic passive: every new piece falls slow for its first <see cref="slowSeconds"/>, then resumes
/// full ramped speed - a thinking/positioning beat on EVERY piece (it neutralises the speed ramp
/// only for that opening moment, unlike Updraft's permanent slow). Applies a per-piece initial-slow
/// window on spawn; a flick/fast-drop bypasses it, so the player can still commit early.
/// </summary>
[CreateAssetMenu(fileName = "Slowburn", menuName = "Stacking/Abilities/Slowburn")]
public class SlowburnPowerUp : PassiveAbility
{
    [Tooltip("How long each new piece stays slowed before resuming full speed.")]
    [Min(0.1f)]
    [SerializeField] private float slowSeconds = 1f;
    [Tooltip("Fraction of normal descent speed during the window. 0.3 = 30% speed.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float slowFactor = 0.3f;

    public override bool OnBlockSpawned(AbilityContext context, BlockController block, BlockData data)
    {
        if (block != null) block.BeginInitialSlow(slowSeconds, slowFactor);
        return false; // notification hook - never consumes a charge
    }
}
