using UnityEngine;

/// <summary>
/// What a status effect DOES, for the core systems that consult it. Kinds with built-in
/// consult points: LifeLossImmunity (GameManager skips the life charge), FallSpeedMultiplier
/// (folds into the spawn-time speed multiplier), ScorePerBlockBonus (added to every score
/// grant). Custom = no built-in hook; abilities query IsActive(definition) themselves.
/// </summary>
public enum StatusEffectKind
{
    Custom,
    LifeLossImmunity,
    FallSpeedMultiplier,
    ScorePerBlockBonus
}

/// <summary>What re-applying an effect that is already active does.</summary>
public enum StatusStackPolicy
{
    /// <summary>Timer restarts at max(remaining, new duration). Magnitude unchanged.</summary>
    RefreshDuration,
    /// <summary>Durations add up. Magnitude unchanged.</summary>
    ExtendDuration,
    /// <summary>Magnitudes add up; timer refreshes like RefreshDuration.</summary>
    StackMagnitude
}

/// <summary>
/// A timed, reusable GAME STATE ("10 s of life-loss immunity", "15 s of +1 progression
/// per block", "10 s of half fall speed") as an asset. States are deliberately not glued
/// into any ability: any consumable, passive, one-shot or combo can Apply() the same
/// asset, and future abilities reuse it by referencing it - no new code unless a new
/// KIND needs a new consult point in a core system. See ABILITIES.md.
/// </summary>
[CreateAssetMenu(fileName = "StatusEffect", menuName = "Stacking/Abilities/Status Effect")]
public class StatusEffectDefinition : ScriptableObject
{
    [SerializeField] private string displayName = "Status";
    [SerializeField] private StatusEffectKind kind = StatusEffectKind.Custom;
    [Tooltip("Seconds of (scaled) game time the state lasts when applied without an override. Pauses freeze the timer.")]
    [Min(0.1f)]
    [SerializeField] private float defaultDuration = 10f;
    [Tooltip("Meaning depends on kind: FallSpeedMultiplier = the multiplier (0.5 = half speed); ScorePerBlockBonus = extra score per grant (1 = +1); others ignore it.")]
    [SerializeField] private float magnitude = 1f;
    [SerializeField] private StatusStackPolicy stackPolicy = StatusStackPolicy.RefreshDuration;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public StatusEffectKind Kind => kind;
    public float DefaultDuration => defaultDuration;
    public float Magnitude => magnitude;
    public StatusStackPolicy StackPolicy => stackPolicy;
}
