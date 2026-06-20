using UnityEngine;

/// <summary>
/// Consumable that transmutes the ACTIVE falling piece into another shape (Shrink: the
/// current piece becomes a 1x1 Pip). The replacement is a full BlockDefinition swapped in
/// via Spawner.ReplaceActivePiece, so it rejoins the normal lock->spawn chain and counts /
/// costs a life exactly as that definition's data dictates (a Pip is a normal brick).
/// Generic: a 1x2 "Shrink" or any other shape-swap is just another asset, no new code.
/// </summary>
[CreateAssetMenu(fileName = "Transmute", menuName = "Stacking/Abilities/Transmute")]
public class TransmuteAbility : ConsumableAbility
{
    [Tooltip("The shape the active piece becomes (its BlockDefinition + default data).")]
    [SerializeField] private BlockDefinition targetShape;

    [Header("Transform FX (swappable)")]
    [Tooltip("Plays on the piece as it transmutes (a CFXR transform/poof effect).")]
    [SerializeField] private GameObject transformEffect;
    [Tooltip("Scale for the transform effect - CFXR effects are character-sized, a block usually wants < 1.")]
    [SerializeField] private float transformScale = 0.6f;

    // The slot is consumed BEFORE Activate, so the shared guard refuses every way the swap
    // could fail (no target/prefab/BlockController, no piece in the air or one mid-lock,
    // already this shape, or fallen past the loss line) - same guard the Bullet uses.
    public override bool CanActivate(AbilityContext context)
        => AbilityEffects.CanTransmuteActivePiece(context, targetShape);

    public override void Activate(AbilityContext context)
    {
        if (context.Spawner.ReplaceActivePiece(targetShape))
        {
            BlockController piece = BlockController.ActiveControlled;
            if (piece != null) Vfx.Spawn(transformEffect, piece.transform.position, transformScale);
            SfxPlayer.Play("transmute", 0.9f);
        }
    }
}
