using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs the moment a Magma Block locks: capture where each of its cells sits, neutralise
/// and remove the magma, then hand off to a <see cref="MagmaMeltSession"/> that drops one
/// stone Pip per cell straight down into the gap beneath it (the normal auto-drop landing
/// path does the gap-finding and stacking for free).
///
/// Deliberately thin: it owns only the magma's decisions - the guards,
/// the cell capture, and the hand-off. The flow, counting and FX live in the session and
/// the per-cell visual.
/// </summary>
public static class MagmaMelt
{
    public static void Run(BlockController magma, MagmaBlockData data)
    {
        if (magma == null) return;

        // A magma caught by the game-over wreckage settle, or one that slid off and locked
        // below the screen, must not melt (mirrors the shared impact guards). Just clean up.
        if (GameManager.Instance != null && GameManager.Instance.isGameOver)
        {
            Object.Destroy(magma.gameObject);
            return;
        }
        Camera camera = Camera.main;
        if (camera != null && camera.orthographic &&
            magma.transform.position.y < LossZone.CullY(camera))
        {
            Object.Destroy(magma.gameObject);
            return;
        }

        if (data == null || data.StoneCell == null)
        {
            // Misconfigured asset: leave the magma as a plain block rather than vanish a piece.
            Debug.LogWarning("[Magma] No stone cell wired - the magma stays as a normal block.", magma);
            return;
        }

        // Capture each cell centre BEFORE the magma is touched (BurstFromEveryCell reads the
        // same colliders). Sort bottom-up so the lowest gaps fill first and upper cells stack
        // onto what landed below them; left-to-right is a tiebreak for a tidy cascade.
        List<Vector3> cellPositions = CaptureCellCentres(magma);
        if (cellPositions.Count == 0)
        {
            Object.Destroy(magma.gameObject);
            return;
        }

        Spawner spawner = Object.FindFirstObjectByType<Spawner>();
        if (spawner == null)
        {
            Object.Destroy(magma.gameObject);
            return;
        }

        // The magma liquefies: drop it out of physics and hide it THIS frame (Destroy is
        // deferred to end-of-frame, and the first stone cell is spawned during the magma's own
        // lock event - so its colliders must not still be there for that cell's descent cast).
        NeutraliseAndHide(magma);

        // A small splash as the magma breaks apart, then the session takes over.
        AbilityEffects.BurstFromEveryCell(magma, data.SolidifyEffect, data.SolidifyEffectScale);
        SfxPlayer.Play("impact_soft_01", 0.55f, 0.08f);

        MagmaMeltSession.Begin(spawner, data, cellPositions);

        Object.Destroy(magma.gameObject);
    }

    private static List<Vector3> CaptureCellCentres(BlockController magma)
    {
        var positions = new List<Vector3>();
        BoxCollider2D[] cells = magma.GetComponentsInChildren<BoxCollider2D>();
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null || cells[i].isTrigger) continue;
            positions.Add(cells[i].bounds.center);
        }

        positions.Sort((a, b) =>
        {
            int byY = a.y.CompareTo(b.y);
            return byY != 0 ? byY : a.x.CompareTo(b.x);
        });
        return positions;
    }

    private static void NeutraliseAndHide(BlockController magma)
    {
        Rigidbody2D rb = magma.GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false; // out of physics so the stone cell's cast can't hit it

        SpriteRenderer[] renderers = magma.GetComponentsInChildren<SpriteRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = false;
        }
    }
}
