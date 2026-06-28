using System;
using UnityEngine;

/// <summary>
/// The "Hold" cache behind the Pocket Cache ability: a Tetris-style stash that holds one block
/// SHAPE and swaps it with the active piece. Run-local, enabled by the ability (off until then).
///
/// The piece lifecycle reuses the Spawner's transmute primitive (ReplaceActivePiece), so this owns
/// only three things: the cached shape, the bank-vs-swap decision, and the one-hold-per-piece
/// lockout. Shape-only - the variant re-rolls when the shape respawns (consistent with the queue
/// and Rebound). Events drive the HUD; the ability and the button stay thin.
///
///   - EMPTY  -> bank:  the active shape flies into the cache; the next queued piece falls from the top.
///   - HELD   -> swap:  the cached shape comes back in-place (lifted a touch = a beat more time),
///                      and the shape you were holding takes its place in the cache.
///
/// Lockout: one hold per piece. After a hold you must LAND a piece (which spawns the next one and
/// re-raises BlockSpawned) before holding again - so a just-swapped-in piece can't be swapped back
/// out for a free time-buy.
/// </summary>
public class HoldCache : MonoBehaviour
{
    private const float SwapLiftCells = 0.9f; // the swapped-in piece spawns this much higher -> "rises, then falls again"

    private Spawner _spawner;
    private bool _enabled;
    private bool _usedThisPiece; // reset when the next piece spawns (i.e. after you land one)
    private BlockDefinition _held;

    public bool IsEnabled => _enabled;
    public BlockDefinition Held => _held;

    /// <summary>The cached shape changed (HUD updates the bubble; null = empty).</summary>
    public event Action<BlockDefinition> HeldChanged;
    /// <summary>The ability was unlocked (HUD reveals the button).</summary>
    public event Action EnabledChanged;
    /// <summary>A bank just happened: this shape left the field at worldPos and flew into the cache.</summary>
    public event Action<Vector3, BlockDefinition> Banked;

    private void Awake() => _spawner = FindAnyObjectByType<Spawner>();

    private void OnEnable() => GameEvents.BlockLocked += HandleBlockLocked;
    private void OnDisable() => GameEvents.BlockLocked -= HandleBlockLocked;

    // The lockout is one hold per piece, so it clears when a piece LANDS - not when one spawns.
    // (Keying off BlockSpawned would let the bank, which raises a fresh spawn, reset its own
    // lockout and re-hold for free; a lock only happens when the player actually places a piece.)
    private void HandleBlockLocked() => _usedThisPiece = false;

    public void Enable()
    {
        if (_enabled) return;
        _enabled = true;
        EnabledChanged?.Invoke();
    }

    /// <summary>Can the player hold right now? Requires the ability, an unused lockout, and a live
    /// controllable piece in the air - ReplaceActivePiece needs one, and paused / win-verifying /
    /// game-over states have no piece to swap.</summary>
    public bool CanHold
    {
        get
        {
            if (!_enabled || _usedThisPiece || _spawner == null) return false;
            // While any active-piece session owns the field (Fission shard, Magma cell, ...) the
            // "live piece" belongs to that session - holding it would corrupt the sequence.
            if (ActivePieceSession.AnyActive) return false;

            GameManager gm = GameManager.Instance;
            if (gm == null || gm.CurrentPhase != GamePhase.Playing || gm.IsGamePaused) return false;

            BlockController active = BlockController.ActiveControlled;
            return active != null && !active.HasLanded && _spawner.ActiveDefinition != null;
        }
    }

    /// <summary>Bank (cache empty) or swap (cache occupied). Returns true if a hold happened.</summary>
    public bool TryHold()
    {
        if (!CanHold) return false;

        BlockController active = BlockController.ActiveControlled;
        BlockDefinition current = _spawner.ActiveDefinition;
        if (active == null || current == null) return false;

        if (_held == null)
        {
            // Bank: pull the next piece in BEFORE we let go of the current one, so a depleted queue
            // (no replacement available) leaves the field untouched rather than stranding it. The
            // banked-in piece is a genuine fresh spawn (asNewSpawn) - it falls from the top and joins
            // combos / slow windows / on-spawn passives like any normal piece.
            BlockDefinition next = _spawner.TakeNextQueued();
            if (next == null) return false;

            Vector3 fromWorld = active.transform.position;
            if (!_spawner.ReplaceActivePiece(next, _spawner.SpawnPosition, asNewSpawn: true))
            {
                _spawner.RequeueDefinition(next); // spawn refused - put the shape back, nothing happened
                return false;
            }
            _held = current;
            Banked?.Invoke(fromWorld, current); // HUD flies a ghost field -> bubble
        }
        else
        {
            // Swap in place (same turn, like a transmute): the cached shape returns lifted slightly
            // (the extra beat), and the shape you were driving takes its spot. Button snaps instantly.
            BlockDefinition incoming = _held;
            if (!_spawner.ReplaceActivePiece(incoming, active.transform.position + Vector3.up * SwapLiftCells))
            {
                return false;
            }
            _held = current;
        }

        _usedThisPiece = true;
        HeldChanged?.Invoke(_held);
        SfxPlayer.Play("swoosh_01", 0.7f, 0.06f);
        return true;
    }
}
