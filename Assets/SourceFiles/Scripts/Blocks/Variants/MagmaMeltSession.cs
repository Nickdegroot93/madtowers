using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime-only driver for a Magma Block's melt. The magma has already locked and been
/// removed; this drops one stone Pip per captured cell, straight down into the gap beneath
/// it, ONE AT A TIME - the next is fed when the current one lands. Gravity and the normal
/// auto-drop landing path do all the gap-finding and in-column stacking; the session only
/// sequences the drops and gates bag pieces out for its duration.
///
/// It rides <see cref="GameEvents.BlockLocked"/> exactly like FissionSession (raised before
/// the Spawner's SpawnNextBlock, so clearing the auto-spawn lock on the final cell lets the
/// very next spawn resume normal play). The magma's OWN lock is the first BlockLocked the
/// session hears, and it uses that as the trigger to drop cell #1 - so every drop, including
/// the first, is driven by the same "a piece just landed" event. No Time.timeScale pause:
/// the "kinda paused" feel comes purely from withholding bag pieces (SetAutoSpawnSuspended).
///
/// Shares AbilitySessionBase with the other active-piece sessions so the "one instance",
/// ActivePieceSession enter/exit, and teardown rules are inherited, not hand-rolled.
/// </summary>
public sealed class MagmaMeltSession : AbilitySessionBase
{
    private Spawner _spawner;
    private MagmaBlockData _data;
    private List<Vector3> _positions;
    private int _index;        // next cell to drop

    protected override bool SeizesActivePiece => true;

    public static bool IsActive => IsSessionActive<MagmaMeltSession>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() => ResetSessionState<MagmaMeltSession>();

    /// <summary>Begin a melt: one stone cell per entry in <paramref name="positions"/>
    /// (already sorted bottom-up by the caller). The magma has been removed already.</summary>
    public static void Begin(Spawner spawner, MagmaBlockData data, List<Vector3> positions)
    {
        if (IsActive || spawner == null || data == null || data.StoneCell == null) return;
        if (positions == null || positions.Count == 0) return;

        GameObject go = new GameObject("MagmaMeltSession");
        go.AddComponent<MagmaMeltSession>().StartSession(spawner, data, positions);
    }

    private void StartSession(Spawner spawner, MagmaBlockData data, List<Vector3> positions)
    {
        if (!BeginSessionLifecycle())
        {
            Destroy(gameObject);
            return;
        }
        _spawner = spawner;
        _data = data;
        _positions = positions;
        _index = 0;

        // Withhold bag pieces for the whole melt - the session feeds its own cells. Set BEFORE
        // the first BlockLocked (the magma's own lock) so no bag piece slips in mid-melt.
        _spawner.SetAutoSpawnSuspended(true);

        // The magma's own lock event (raised right after this returns) drops cell #1; each
        // cell's lock then drops the next. One uniform handler covers them all.
        GameEvents.BlockLocked += HandleBlockLocked;
    }

    private void OnDisable() => GameEvents.BlockLocked -= HandleBlockLocked;

    // Safety net: a scene reload / level restart mid-melt would otherwise leave the session
    // active (gating consumables) and the spawn lock stuck. The base OnDestroy routes here.
    public override void CancelSession() => Finish(destroySelf: !IsDestroying);

    private void Update()
    {
        // Game over can destroy the in-flight cell without a lock event - tear down so the
        // spawn lock and active-piece gate never strand (Finish is null-safe and idempotent).
        if (!IsFinishing && GameManager.Instance != null && GameManager.Instance.isGameOver)
        {
            Finish();
        }
    }

    // A piece just landed (the magma first, then each stone cell). Drop the next cell, or, when
    // they are all placed, drop the auto-spawn lock so the Spawner's own lock->spawn chain
    // (SpawnNextBlock, called right after this event) resumes normal play.
    private void HandleBlockLocked(BlockController block)
    {
        if (IsFinishing) return;
        if (GameManager.Instance != null && GameManager.Instance.isGameOver) { Finish(); return; }

        if (_index >= _positions.Count)
        {
            _spawner.SetAutoSpawnSuspended(false);
            Finish();
            return;
        }

        Vector3 spawnPos = _positions[_index];
        BlockController cell = _spawner.SpawnControlledPieceAt(_data.StoneCell, spawnPos, suspended: false);
        if (cell == null)
        {
            // Spawn refused (misconfig) - bail cleanly rather than strand the run.
            _spawner.SetAutoSpawnSuspended(false);
            Finish();
            return;
        }

        // ONE magma placement is ONE block. The cells are debris of that single placement, so
        // all but the first are excluded from the placement count - otherwise a 2x2 magma
        // paid four blocks of score, four toward a PlaceBlocks goal, and four toward a puzzle
        // wave quota, letting a 7-block wave clear in two placements (Nick 2026-08-09).
        // The FIRST cell is left counting rather than crediting a phantom +1, so the ordinary
        // destroy path still decrements exactly once if that block later leaves the tower.
        // Per-instance, never on the variant: the Pip is a normal playable block elsewhere.
        if (_index > 0 && cell.TryGetComponent<BlockIdentity>(out BlockIdentity cellIdentity))
        {
            cellIdentity.SuppressPlacementCount();
        }

        // Dress the cell as flowing magma that fuses to stone on landing, then commit the plunge:
        // a hard auto-drop sends it straight down its column into the gap (horizontal steps are
        // gated during auto-drop, so the player can't divert the melt).
        MagmaBlobVisual visual = cell.gameObject.AddComponent<MagmaBlobVisual>();
        visual.InitMeltCell(_data.MoltenColor, _data.SolidifyEffect, _data.SolidifyEffectScale);
        cell.ForceAutoDrop(); // system plunge: bypasses the gesture gate, raises no gesture event

        SfxPlayer.Play("impact_soft_01", 0.4f, 0.1f);
        _index++;
    }

    private void Finish(bool destroySelf = true)
    {
        if (!BeginFinish()) return;

        if (_spawner != null) _spawner.SetAutoSpawnSuspended(false);
        CompleteSessionLifecycle(destroySelf);
    }
}
