using UnityEngine;

/// <summary>
/// Bullet (Common consumable): transforms the ACTIVE falling piece into a 1x1
/// projectile that keeps all normal piece controls. On first contact it destroys the
/// dynamic tower block it landed on, and itself - statics and frozen blocks are
/// bulletproof. The transformation goes through Spawner.ReplaceActivePiece so the
/// projectile rejoins the normal lock->spawn chain.
/// </summary>
[CreateAssetMenu(fileName = "Bullet", menuName = "Stacking/Abilities/Bullet")]
public class BulletAbility : ConsumableAbility
{
    [Tooltip("The 1x1 projectile piece (Block_Bullet definition with BulletBlockData).")]
    [SerializeField] private BlockDefinition bulletBlock;

    [Header("Transform FX (swappable)")]
    [Tooltip("Plays on the piece as it warps into the bullet (a CFXR transform/charge effect).")]
    [SerializeField] private GameObject transformEffect;
    [Tooltip("Scale for the transform effect - CFXR effects are character-sized, a block usually wants < 1.")]
    [SerializeField] private float transformScale = 0.6f;

    // Shared transform-consumable guard: refuses (before the slot is consumed) a missing/
    // unwired projectile, no piece in the air or one mid-lock, a piece that is already a
    // Bullet, or one past the loss line. See AbilityEffects.CanTransmuteActivePiece.
    public override bool CanActivate(AbilityContext context)
        => AbilityEffects.CanTransmuteActivePiece(context, bulletBlock);

    public override void Activate(AbilityContext context)
    {
        if (context.Spawner.ReplaceActivePiece(bulletBlock))
        {
            BlockController bullet = BlockController.ActiveControlled;
            if (bullet != null) Vfx.Spawn(transformEffect, bullet.transform.position, transformScale);
            SfxPlayer.Play("gun_cock_01", 0.75f, 0.03f);
        }
    }
}
