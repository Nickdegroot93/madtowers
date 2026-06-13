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

    public override bool CanActivate(AbilityContext context)
    {
        // The slot is consumed BEFORE Activate, so every way the transform could fail
        // must be refused here or the tap silently eats the charge:
        // - misconfigured asset (no projectile wired, or its prefab lacks a
        //   BlockController so ReplaceActivePiece would bail after the slot is gone)
        // - no piece in the air, or a landed piece mid-lock
        // - the piece is already a Bullet (double-tap with Bullet in both slots)
        // - the piece has fallen past the loss line (doomed; the sweep owns it now)
        if (context.Spawner == null || bulletBlock == null || bulletBlock.Prefab == null) return false;
        if (bulletBlock.Prefab.GetComponent<BlockController>() == null) return false;

        BlockController active = BlockController.ActiveControlled;
        if (active == null || active.HasLanded) return false;
        if (active.TryGetComponent(out BlockIdentity identity) && identity.Definition == bulletBlock) return false;

        Camera camera = Camera.main;
        if (camera != null && camera.orthographic && active.transform.position.y < LossZone.CullY(camera)) return false;

        return true;
    }

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
