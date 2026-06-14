using UnityEngine;

/// <summary>
/// Unique passive that widens how many upcoming shapes the player can see. On acquire it
/// raises the Spawner's visible look-ahead depth; the queue is stable, so the extra
/// preview is exactly the shape that will spawn. The HUD grows its NEXT card to match.
/// Resets naturally per run (the Spawner is a fresh scene object on restart).
/// </summary>
[CreateAssetMenu(fileName = "QueueVisibility", menuName = "Stacking/Abilities/Queue Visibility")]
public class QueueVisibilityPowerUp : PassiveAbility
{
    [Tooltip("How many upcoming shapes become visible (and prepared) while owned. 2 = next + the one after.")]
    [Min(1)]
    [SerializeField] private int visibleDepth = 2;

    public override void OnAcquired(AbilityContext context, int stacks)
    {
        if (context.Spawner != null) context.Spawner.SetVisibleQueueDepth(visibleDepth);
    }
}
