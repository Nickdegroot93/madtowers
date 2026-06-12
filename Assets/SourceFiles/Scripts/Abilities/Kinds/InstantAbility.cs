using UnityEngine;

/// <summary>
/// Apply-on-pick: the effect happens once, immediately, when the card is chosen
/// (extra life, slow motion, next-block variant). Nothing is held or tracked
/// afterwards - the simplest kind, and the shape of the original power-up system.
/// </summary>
public abstract class InstantAbility : AbilityDefinition
{
    public abstract void Apply(AbilityContext context);
}
