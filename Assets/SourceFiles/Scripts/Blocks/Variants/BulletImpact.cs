using UnityEngine;

/// <summary>
/// Runs the Bullet's impact the moment it locks: find the block it landed on, destroy
/// it (dynamic tower blocks only - statics and frozen blocks are bulletproof), and
/// always shatter the bullet itself. The bullet locked normally first, so the
/// lock->spawn chain has already queued the next piece; this only cleans up.
///
/// Deliberately thin: the reusable mechanics live in shared helpers
/// (BlockQuery.SupportBlockBelow for the victim search, AbilityEffects.BurstFromEveryCell
/// + ImpactPunch for the juice). What stays here is purely Bullet's decisions - the
/// guards and which sounds play.
/// </summary>
public static class BulletImpact
{
    public static void Run(BlockController bullet, BulletBlockData data)
    {
        if (bullet == null) return;

        // Locks during the game-over wreckage settle (the bullet was mid-air when the
        // last life went) must not detonate behind the game-over screen.
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;

        // A bullet that slid off the tower and locked below the screen (the loss-zone
        // cleanup path also locks): no invisible detonation, no off-screen victim.
        Camera camera = Camera.main;
        if (camera != null && camera.orthographic &&
            bullet.transform.position.y < LossZone.CullY(camera))
        {
            Object.Destroy(bullet.gameObject);
            return;
        }

        BlockController victim = BlockQuery.SupportBlockBelow(bullet);
        float tuning = data != null ? data.EffectScale : 1f;

        if (victim != null)
        {
            // Real kill: break from every cell, punch the game, drop the block from the
            // live total (a destroyed block is one fewer placed block), destroy it.
            AbilityEffects.BurstFromEveryCell(victim, data != null ? data.ImpactEffect : null, tuning);
            AbilityEffects.ImpactPunch();
            SfxPlayer.Play("impact_shatter_01", 0.85f, 0.06f);
            if (GameManager.Instance != null) GameManager.Instance.RemovePlacedBlock(victim);
            Object.Destroy(victim.gameObject);
        }
        else
        {
            // Wasted shot (floor/island/frozen block) - read "no effect" instantly.
            AbilityEffects.BurstFromEveryCell(bullet, data != null ? data.WastedEffect : null, tuning);
            SfxPlayer.Play("impact_soft_01", 0.6f, 0.06f);
        }

        Object.Destroy(bullet.gameObject);
    }
}
