using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-run ability state and dispatch. Lives on the GameManager's object.
///
/// State rule (the LevelModifier pattern): definitions are immutable assets; acquiring
/// one clones an Instance whose plain fields are safe per-run state. Identity checks
/// (unique/stacks/bans/cards) compare Source; callbacks go to Instance. Stacking never
/// re-clones - it increments Stacks and calls OnStackAdded on the same Instance.
///
/// Ordering rules (full table in ABILITIES.md): one inventory list in acquisition
/// order. Intercepting hooks short-circuit at the highest-priority ability that handles
/// the event (ties stay acquisition-ordered); notification hooks fan out to everyone
/// (passives phase, then combos phase). A charge is consumed immediately after the
/// owning handler reports having triggered.
/// </summary>
public class AbilityRuntime : MonoBehaviour
{
    public const int ConsumableSlotCount = 2;

    public sealed class OwnedAbility
    {
        public AbilityDefinition Source;     // the asset (identity)
        public AbilityDefinition Instance;   // the per-run clone (state + callbacks)
        public int Stacks;
        public int ChargesLeft;              // 0 = infinite
    }

    private readonly List<OwnedAbility> _owned = new List<OwnedAbility>();
    private readonly ConsumableAbility[] _slots = new ConsumableAbility[ConsumableSlotCount]; // clones
    private readonly ConsumableAbility[] _slotSources = new ConsumableAbility[ConsumableSlotCount];
    private readonly List<ComboTriggerDefinition> _subscribedTriggers = new List<ComboTriggerDefinition>();
    private readonly List<OwnedAbility> _dispatchSnapshot = new List<OwnedAbility>();
    private int _dispatchDepth;

    private AbilityContext _context;
    private StatusEffects _status;

    // Block-count slow window shared by Recovery (on life loss) and Slo-Mo (on activate): N blocks
    // fall at _slowWindowFactor of base speed. Folded into the normal-descent factor and counted
    // down per block - never a timer (follows the player's pace). Run-local (fresh AbilityRuntime
    // per scene).
    //
    // The brick ALREADY IN THE AIR when the window is granted is the window's FIRST block - the
    // grant has to be felt on the piece you are steering, not on the one after it. So the two
    // fields split the bookkeeping: _slowWindowBlocks counts the future SPAWNS still owed, and
    // _slowWindowOnActivePiece says whether the live piece is inside the window. The composed
    // factor keys off the flag, so the window can still cover a live piece with nothing owed
    // (a 1-block window granted mid-flight).
    private int _slowWindowBlocks;
    private bool _slowWindowOnActivePiece;
    private float _slowWindowFactor = 1f;

    /// <summary>Slow <paramref name="blocks"/> blocks to <paramref name="factor"/> of base speed
    /// (normal descent only; fast drops are unaffected), starting with the brick currently falling
    /// if there is one. Overlapping grants take the stronger slow and the longer remaining
    /// window.</summary>
    public void GrantSlowWindow(int blocks, float factor)
    {
        if (blocks <= 0) return;

        factor = Mathf.Clamp(factor, 0.05f, 1f);
        bool windowRunning = _slowWindowBlocks > 0 || _slowWindowOnActivePiece;
        _slowWindowFactor = windowRunning ? Mathf.Min(_slowWindowFactor, factor) : factor;

        // The brick in the air spends the first of the N. Re-granting while that SAME piece is
        // still flying just re-takes the maximum, so it is never charged twice.
        bool coversLivePiece = BlockController.LiveActivePiece != null;
        if (coversLivePiece) _slowWindowOnActivePiece = true;
        _slowWindowBlocks = Mathf.Max(_slowWindowBlocks, coversLivePiece ? blocks - 1 : blocks);

        RecomputeFallSpeedMultiplier();
    }

    // Banked power-up rerolls (granted by RerollPowerUp). While > 0 the choice panel shows a
    // Reroll button; each click redraws the three cards and spends one. Banked across offers,
    // run-local (fresh AbilityRuntime per scene), like the slow window above.
    private int _rerollCharges;
    public int RerollCharges => _rerollCharges;
    public void GrantRerollCharges(int count) { if (count > 0) _rerollCharges += count; }
    public bool TryConsumeReroll()
    {
        if (_rerollCharges <= 0) return false;
        _rerollCharges--;
        return true;
    }

    /// <summary>Raised whenever owned abilities, their charges, or the slots change (HUD + picker
    /// cards listen).</summary>
    public event System.Action InventoryChanged;

    /// <summary>Owned abilities in acquisition order - a read-only view for UI (identity via
    /// Source, live charge count via ChargesLeft). Never mutate through this.</summary>
    public IReadOnlyList<OwnedAbility> Owned => _owned;

    /// <summary>Fill <paramref name="buffer"/> with the owned abilities still holding a charge -
    /// the ARMED set: one-shot passives waiting to fire (Ward, Sacrifice, Hardline). Acquisition
    /// order. Permanent passives (charges 0 = infinite) are not armed; they have nothing to spend.
    /// This is what the armed-ability rail shows.</summary>
    public void GetArmedAbilities(List<OwnedAbility> buffer)
    {
        buffer.Clear();
        for (int i = 0; i < _owned.Count; i++)
        {
            // Passives only: the rail's amber is AbilityTypeInfo's "one-time PASSIVE" language, so a
            // charged combo ability would be shown under a type badge that isn't its own.
            if (_owned[i].ChargesLeft > 0 && _owned[i].Instance is PassiveAbility) buffer.Add(_owned[i]);
        }
    }

    /// <summary>Spend one charge for an effect that resolves LATER than the handler which decided
    /// to fire it - Ward arms a strike on the hazard and the charge is paid when the brick actually
    /// converts, so the armed rail burns its icon at the moment the player sees the effect. Matched
    /// by Instance (the clone the handler ran on). False if it is already gone.</summary>
    public bool SpendCharge(AbilityDefinition instance)
    {
        if (instance == null) return false;

        for (int i = 0; i < _owned.Count; i++)
        {
            if (_owned[i].Instance != instance) continue;
            ConsumeCharge(_owned[i]);
            return true;
        }
        return false;
    }

    public AbilityContext Context => _context ?? BuildContext();
    public IReadOnlyList<ComboTriggerDefinition> SubscribedTriggers => _subscribedTriggers;

    private void Awake()
    {
        _status = GetComponent<StatusEffects>();
    }

    private void OnEnable()
    {
        GameEvents.LifeLost += HandleLifeLost;
        GameEvents.LivesChanged += HandleLivesChanged;
        GameEvents.BlockSpawned += HandleBlockSpawned;
        if (_status != null) _status.Changed += RecomputeFallSpeedMultiplier;
    }

    private void OnDisable()
    {
        GameEvents.LifeLost -= HandleLifeLost;
        GameEvents.LivesChanged -= HandleLivesChanged;
        GameEvents.BlockSpawned -= HandleBlockSpawned;
        if (_status != null) _status.Changed -= RecomputeFallSpeedMultiplier;
    }

    private void OnDestroy()
    {
        // Instantiate'd ScriptableObjects are not scene objects: without explicit
        // destruction they linger until Unity's next asset GC. Make ownership explicit.
        for (int i = 0; i < _owned.Count; i++)
        {
            if (_owned[i].Instance != null) Destroy(_owned[i].Instance);
        }
        _owned.Clear();
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null) Destroy(_slots[i]);
            _slots[i] = null;
            _slotSources[i] = null;
        }
    }

    private AbilityContext BuildContext()
    {
        _context = new AbilityContext
        {
            GameManager = GameManager.Instance,
            Spawner = FindAnyObjectByType<Spawner>(),
            Runtime = this,
            Status = _status,
            Config = GameManager.Instance != null ? GameManager.Instance.ActiveConfig : null,
            Level = LevelSelectionState.SelectedLevel,
            Hold = GetComponent<HoldCache>()
        };
        return _context;
    }

    // ---- Acquisition (routing by kind; called by the choice controller) --------------------

    public int GetOwnedStacks(AbilityDefinition source)
    {
        OwnedAbility owned = FindOwned(source);
        int stacks = owned != null ? owned.Stacks : 0;

        // Held consumables count as owned for uniqueness/caps.
        for (int i = 0; i < _slotSources.Length; i++)
        {
            if (_slotSources[i] == source) stacks++;
        }
        return stacks;
    }

    /// <summary>Acquire a passive or combo ability (instants and consumables route elsewhere).</summary>
    public void AcquirePassive(AbilityDefinition source)
    {
        int sourceCharges = source is PassiveAbility p ? p.Charges
                          : source is ComboAbility c ? c.Charges : 0;

        OwnedAbility owned = FindOwned(source);
        if (owned != null)
        {
            owned.Stacks++;
            // Stacking a CHARGED ability adds its charges (two non-unique one-shot
            // passives = two saves). Infinite (0) stays infinite. Without this, re-picking a
            // charged ability would consume the offer and change nothing.
            if (owned.ChargesLeft > 0) owned.ChargesLeft += sourceCharges;
            if (owned.Instance is PassiveAbility passive) passive.OnStackAdded(Context, owned.Stacks);
        }
        else
        {
            owned = new OwnedAbility
            {
                Source = source,
                Instance = Instantiate(source),
                Stacks = 1,
                ChargesLeft = sourceCharges
            };
            _owned.Add(owned);
            if (owned.Instance is PassiveAbility passive) passive.OnAcquired(Context, 1);

            // FIRST acquisition only. A stack re-delivering the spawn hook would run it twice on a
            // piece that already got it - Slowburn would restart a window that brick already spent,
            // and a charged spawn-triggered passive would eat the charge the stack just added.
            CatchUpOnLivePiece(owned);
        }

        RefreshSubscribedTriggers();
        RecomputeFallSpeedMultiplier();
        InventoryChanged?.Invoke();
    }

    // ---- Consumable slots -------------------------------------------------------------------

    public bool HasFreeConsumableSlot => FindFreeSlot() >= 0;

    public ConsumableAbility GetSlotSource(int slot)
    {
        return slot >= 0 && slot < _slotSources.Length ? _slotSources[slot] : null;
    }

    /// <summary>Add to the first free slot; false when both are full (caller shows the swap dialog).</summary>
    public bool TryAddConsumable(ConsumableAbility source)
    {
        int slot = FindFreeSlot();
        if (slot < 0) return false;

        SetSlot(slot, source);
        return true;
    }

    /// <summary>Swap dialog resolution: replace whatever is in the slot.</summary>
    public void ReplaceConsumable(int slot, ConsumableAbility source)
    {
        if (slot < 0 || slot >= _slots.Length) return;
        SetSlot(slot, source);
    }

    private void SetSlot(int slot, ConsumableAbility source)
    {
        if (_slots[slot] != null) Destroy(_slots[slot]); // discard the old clone
        _slots[slot] = Instantiate(source);
        _slotSources[slot] = source;
        InventoryChanged?.Invoke();
    }

    /// <summary>True while the blanket activation gates allow consumable use at all. Choice
    /// sessions lock consumables out for their duration: the "active piece" is owned by a
    /// sequence driver, and another active-piece consumable fired into it would corrupt that
    /// sequence.</summary>
    public bool ConsumablesUsable
    {
        get
        {
            GameManager gm = GameManager.Instance;
            return gm != null && gm.CurrentPhase == GamePhase.Playing && !gm.IsGamePaused
                   && !ActivePieceSession.AnyActive; // any active-piece session owns the field
        }
    }

    public bool CanActivateSlot(int slot)
    {
        return ConsumablesUsable &&
               slot >= 0 && slot < _slots.Length && _slots[slot] != null &&
               _slots[slot].CanActivate(Context);
    }

    public bool TryActivateSlot(int slot)
    {
        if (!CanActivateSlot(slot)) return false;

        // Slot cleared BEFORE Activate: double-taps and re-entrant activations find it empty.
        ConsumableAbility instance = _slots[slot];
        _slots[slot] = null;
        _slotSources[slot] = null;
        InventoryChanged?.Invoke();

        instance.Activate(Context);
        Destroy(instance);
        return true;
    }

    // ---- Loss interception (called by LossZone for LANDED blocks only) ----------------------

    public float LossInterceptLineOffset
    {
        get
        {
            float offset = 0f;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i].Instance is not PassiveAbility passive) continue;
                offset = Mathf.Max(offset, passive.LossInterceptLineOffset);
            }

            return offset;
        }
    }

    /// <summary>True while any armed passive renders a visible loss-line beam - LossZone then
    /// triggers landed-block interception at the on-screen InterceptLineY (see PassiveAbility.
    /// ShowsLossInterceptLine).</summary>
    public bool HasLossInterceptLine
    {
        get
        {
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i].Instance is PassiveAbility passive && passive.ShowsLossInterceptLine) return true;
            }

            return false;
        }
    }

    /// <summary>Highest-priority armed ability gets first refusal; ties use acquisition order.</summary>
    public bool TryInterceptLoss(BlockController block)
    {
        int previousPriority = int.MaxValue;
        while (true)
        {
            int activePriority = int.MinValue;
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i].Instance is not PassiveAbility passive) continue;
                int priority = passive.LossInterceptPriority;
                if (priority < previousPriority && priority > activePriority)
                {
                    activePriority = priority;
                }
            }

            if (activePriority == int.MinValue) return false;

            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i].Instance is not PassiveAbility passive) continue;
                if (passive.LossInterceptPriority != activePriority) continue;
                if (!passive.TryInterceptLoss(Context, block)) continue;

                ConsumeCharge(_owned[i]);
                return true;
            }

            previousPriority = activePriority;
        }
    }

    // ---- Event fan-out ------------------------------------------------------------------------

    private void HandleLifeLost()
    {
        FanOutToPassives(passive => passive.OnLifeLost(Context));
        RecomputeFallSpeedMultiplier(); // last-life style factors depend on lives
    }

    // Any lives change (gaining a life via Extra Life/Recovery, not just losing one) must
    // refresh last-life factors like LastStand - otherwise its slow lingers on the in-flight
    // piece until the next spawn recomputes. (Loss also recomputes via HandleLifeLost above.)
    private void HandleLivesChanged(int lives)
    {
        RecomputeFallSpeedMultiplier();
    }

    private void HandleBlockSpawned(BlockController block, BlockData data)
    {
        FanOutToPassives(passive => passive.OnBlockSpawned(Context, block, data));

        // The new piece takes the next block of the window, and the previous piece's coverage ends
        // with it. The recompute below re-stamps this very piece with the resulting factor, so the
        // stamp WireBlock applied a moment ago (from the pre-spawn state) is corrected in the same
        // call - the window covers exactly N pieces however it was granted.
        _slowWindowOnActivePiece = _slowWindowBlocks > 0;
        if (_slowWindowBlocks > 0) _slowWindowBlocks--;
        if (!_slowWindowOnActivePiece) _slowWindowFactor = 1f;

        RecomputeFallSpeedMultiplier(); // per-block windows count down on spawn
    }

    // Fan-outs iterate a SNAPSHOT in ACQUISITION ORDER (the documented rule). The snapshot is a
    // per-call local (not a shared field) so the dispatch is re-entrant-safe: a handler that
    // synchronously re-raises a dispatched event (e.g. a future combo/passive that spawns a piece
    // -> BlockSpawned -> another fan-out) gets its own snapshot instead of clearing the list the
    // outer loop is still walking. The only mid-event mutation is an ability consuming itself,
    // which happens after its own handler ran, so snapshot entries are never stale for anyone else.

    /// <summary>Called by the ComboDetector after a trigger match survives revalidation.</summary>
    public void HandleComboFired(ComboTriggerDefinition trigger, ComboMatch match)
    {
        List<OwnedAbility> snapshot = BeginDispatchSnapshot();
        try
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                OwnedAbility owned = snapshot[i];
                if (owned.Instance is not ComboAbility combo || combo.Trigger != trigger) continue;

                combo.OnComboFired(Context, match);
                ConsumeCharge(owned);
            }
        }
        finally
        {
            EndDispatchSnapshot(snapshot);
        }
        InventoryChanged?.Invoke();
    }

    /// <summary>Deliver OnBlockSpawned once for the brick ALREADY falling when a passive is picked.
    /// A pick has to change the piece the player is looking at, not sit idle until the next spawn -
    /// so Slowburn opens its slow window on this piece and Ward defuses a hazard already in the air
    /// (spending its charge, exactly as a spawn-time defuse would). Passives with no spawn handler
    /// are unaffected; the piece must be a live in-air one (LiveActivePiece).</summary>
    private void CatchUpOnLivePiece(OwnedAbility owned)
    {
        if (owned.Instance is not PassiveAbility passive) return;

        BlockController live = BlockController.LiveActivePiece;
        if (live == null) return;

        BlockData data = live.TryGetComponent(out BlockIdentity identity) ? identity.Variant : null;
        if (passive.OnBlockSpawned(Context, live, data)) ConsumeCharge(owned);
    }

    private void FanOutToPassives(System.Func<PassiveAbility, bool> handler)
    {
        List<OwnedAbility> snapshot = BeginDispatchSnapshot();
        try
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i].Instance is not PassiveAbility passive) continue;
                if (handler(passive)) ConsumeCharge(snapshot[i]);
            }
        }
        finally
        {
            EndDispatchSnapshot(snapshot);
        }
    }

    private List<OwnedAbility> BeginDispatchSnapshot()
    {
        _dispatchDepth++;
        if (_dispatchDepth > 1) return new List<OwnedAbility>(_owned);

        _dispatchSnapshot.Clear();
        _dispatchSnapshot.AddRange(_owned);
        return _dispatchSnapshot;
    }

    private void EndDispatchSnapshot(List<OwnedAbility> snapshot)
    {
        if (ReferenceEquals(snapshot, _dispatchSnapshot)) _dispatchSnapshot.Clear();
        _dispatchDepth = Mathf.Max(0, _dispatchDepth - 1);
    }

    private void ConsumeCharge(OwnedAbility owned)
    {
        if (owned.ChargesLeft <= 0) return; // 0 = infinite

        owned.ChargesLeft--;
        if (owned.ChargesLeft > 0)
        {
            InventoryChanged?.Invoke(); // still armed, one charge lighter - the rail shows the count
            return;
        }

        if (owned.Instance is PassiveAbility passive) passive.OnRemoved(Context);
        _owned.Remove(owned);
        Destroy(owned.Instance);
        RefreshSubscribedTriggers();
        RecomputeFallSpeedMultiplier();
        InventoryChanged?.Invoke();
    }

    // ---- Stat composition -----------------------------------------------------------------

    // One push point for the spawn-time fall speed: passive factors x active status
    // factors. Recomputed on inventory/lives/spawn/status changes - never per frame.
    private void RecomputeFallSpeedMultiplier()
    {
        float factor = 1f;
        for (int i = 0; i < _owned.Count; i++)
        {
            if (_owned[i].Instance is PassiveAbility passive)
            {
                factor *= passive.GetFallSpeedFactor(Context, _owned[i].Stacks);
            }
        }
        if (_status != null) factor *= _status.GetFallSpeedFactor();
        if (_slowWindowOnActivePiece) factor *= _slowWindowFactor;

        if (GameManager.Instance != null) GameManager.Instance.SetAbilityFallSpeedMultiplier(factor);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private OwnedAbility FindOwned(AbilityDefinition source)
    {
        for (int i = 0; i < _owned.Count; i++)
        {
            if (_owned[i].Source == source) return _owned[i];
        }
        return null;
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null) return i;
        }
        return -1;
    }

    private void RefreshSubscribedTriggers()
    {
        _subscribedTriggers.Clear();
        for (int i = 0; i < _owned.Count; i++)
        {
            if (_owned[i].Instance is ComboAbility combo && combo.Trigger != null &&
                !_subscribedTriggers.Contains(combo.Trigger))
            {
                _subscribedTriggers.Add(combo.Trigger);
            }
        }
    }
}
