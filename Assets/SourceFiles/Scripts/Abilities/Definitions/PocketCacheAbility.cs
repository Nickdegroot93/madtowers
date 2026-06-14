using UnityEngine;

/// <summary>
/// Rare unique passive: unlocks a Tetris-style "Hold" cache. A circular button appears on the
/// left; tapping it stores the current block's SHAPE and swaps it with whatever is cached,
/// letting you filter out bad randomness. All of the behaviour lives in <see cref="HoldCache"/>
/// (the system) and the HoldButton HUD - this just switches it on. Unique, permanent (charges 0).
/// </summary>
[CreateAssetMenu(fileName = "PocketCache", menuName = "Stacking/Abilities/Pocket Cache")]
public class PocketCacheAbility : PassiveAbility
{
    public override void OnAcquired(AbilityContext context, int stacks)
    {
        if (context.Hold != null) context.Hold.Enable();
    }
}
