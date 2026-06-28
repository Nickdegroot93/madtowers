using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime half of BombBlockData: the authoritative fuse clock and the detonation. It owns the timer
/// and feeds normalised progress (0..1) to BombBlockSkin every frame, which renders the countdown
/// (heating seams, accelerating heartbeat, tremble, pre-flash). When the fuse ends it deletes this
/// block and every block touching it. Added to the block when it locks.
/// </summary>
public class BombBlockBehaviour : MonoBehaviour
{
    private readonly Collider2D[] _overlapBuffer = new Collider2D[24];

    // A near-black iron tint for the bomb's own shards, matching its casing.
    private static readonly Color IronTint = new Color(0.13f, 0.13f, 0.15f, 1f);

    private float _fuseSeconds;
    private float _touchRange;
    private float _elapsed;
    private BombBlockSkin _skin;
    private GameObject _explosionEffect;
    private float _explosionScale = 1f;
    private GameObject _breakPuffEffect;

    public void Arm(float fuseSeconds, float touchRange, GameObject explosionEffect, float explosionScale, GameObject breakPuffEffect)
    {
        _fuseSeconds = Mathf.Max(0.01f, fuseSeconds);
        _touchRange = touchRange;
        _explosionEffect = explosionEffect;
        _explosionScale = explosionScale;
        _breakPuffEffect = breakPuffEffect;
        TryGetComponent(out _skin); // the skin drives the visuals; may be absent in stripped tests
    }

    private void Update()
    {
        // Once the run is over the fuse freezes - no detonation behind the game-over wreckage screen
        // (it would destroy blocks in the final tower and play FX). Mirrors the shared impact guards.
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) return;

        _elapsed += Time.deltaTime;

        // The skin reads the countdown off this single clock - no second timer to drift.
        if (_skin != null) _skin.SetFuse(_elapsed / _fuseSeconds);

        if (_elapsed >= _fuseSeconds)
        {
            Detonate();
        }
    }

    private void Detonate()
    {
        BlockController self = GetComponent<BlockController>();
        var touching = new HashSet<Collider2D>();
        BlockTouchScanner.CollectTouchingColliders(gameObject, _touchRange, touching, _overlapBuffer);

        var victims = new HashSet<BlockController>();
        foreach (Collider2D hit in touching)
        {
            BlockController other = hit.GetComponentInParent<BlockController>();
            if (other == null || other == self || !other.HasLanded) continue;

            victims.Add(other);
        }

        // The blast: one big authored explosion at the bomb's centre + a camera punch. One boom for the
        // whole detonation (not one per victim) so a wide bomb doesn't stack a wall of sound.
        Vector3 center = self != null && self.TryGetWorldBounds(out Bounds selfBounds)
            ? selfBounds.center : transform.position;
        Vfx.Spawn(_explosionEffect, center, _explosionScale);
        ImpactFx.ImpactPunch();
        SfxPlayer.Play("impact_shatter_01", 0.9f, 0.06f);

        // Each destroyed block (and the bomb itself) leaves the board, so drop it from the live
        // placed-block total - same accounting as any block destruction or a fall-off (BLOCKS.md). Neighbours
        // break with the game-standard shard shatter plus a small smoke puff from every cell.
        foreach (BlockController victim in victims)
        {
            BreakVictim(victim);
        }

        // The bomb itself shatters in dark iron (its own boom already played above).
        if (self != null && self.TryGetWorldBounds(out Bounds bombBounds))
            BlockShatterFx.Spawn(bombBounds, IronTint);
        if (self != null) GameEvents.RaiseBlockDestroyed(self);
        Destroy(gameObject);
    }

    private void BreakVictim(BlockController victim)
    {
        if (victim == null) return;

        if (victim.TryGetWorldBounds(out Bounds bounds))
            BlockShatterFx.Spawn(bounds, VictimTint(victim));
        ImpactFx.BurstFromEveryCell(victim, _breakPuffEffect); // smoke puff per cell (no-op if unassigned)

        GameEvents.RaiseBlockDestroyed(victim);
        Destroy(victim.gameObject);
    }

    // The brick's own painted colour, so its shards read as that block crumbling.
    private static Color VictimTint(BlockController victim)
    {
        SpriteRenderer sr = victim.GetComponentInChildren<SpriteRenderer>();
        return sr != null ? sr.color : Color.gray;
    }
}
