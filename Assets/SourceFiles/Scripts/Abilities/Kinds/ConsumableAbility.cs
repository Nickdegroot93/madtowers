using UnityEngine;

/// <summary>
/// Held in one of the two HUD slots and fired manually by the player. Picking one
/// while both slots are full opens the swap dialog. AbilityRuntime applies blanket
/// activation gates (not paused, not game over, not during win verification) BEFORE
/// CanActivate - override CanActivate only for ability-specific requirements
/// (e.g. "needs at least one landed block").
/// </summary>
public abstract class ConsumableAbility : AbilityDefinition
{
    public virtual bool CanActivate(AbilityContext context) => true;

    public abstract void Activate(AbilityContext context);
}
