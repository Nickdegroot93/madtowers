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
/// order. Intercepting hooks short-circuit at the first ability that handles the event;
/// notification hooks fan out to everyone (passives phase, then combos phase). A charge
/// is consumed immediately after the owning handler reports having triggered.
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

    private AbilityContext _context;
    private StatusEffects _status;

    /// <summary>Raised whenever owned abilities or slots change (HUD + picker cards listen).</summary>
    public event System.Action InventoryChanged;

    public AbilityContext Context => _context ?? BuildContext();
    public IReadOnlyList<ComboTriggerDefinition> SubscribedTriggers => _subscribedTriggers;

    private void Awake()
    {
        _status = GetComponent<StatusEffects>();
    }

    private void OnEnable()
    {
        GameEvents.LifeLost += HandleLifeLost;
        GameEvents.BlockSpawned += HandleBlockSpawned;
        if (_status != null) _status.Changed += RecomputeFallSpeedMultiplier;
    }

    private void OnDisable()
    {
        GameEvents.LifeLost -= HandleLifeLost;
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
            Level = LevelSelectionState.SelectedLevel
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
            // Stacking a CHARGED ability adds its charges (two Sacrificial Safeties =
            // two saves). Infinite (0) stays infinite. Without this, re-picking a
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

    /// <summary>True while the blanket activation gates allow consumable use at all.</summary>
    public bool ConsumablesUsable
    {
        get
        {
            GameManager gm = GameManager.Instance;
            return gm != null && !gm.isGameOver && !gm.IsGamePaused && !LevelRuntimeController.IsVerifyingWin;
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

    /// <summary>First armed ability to handle the loss wins; the block must end non-lost.</summary>
    public bool TryInterceptLoss(BlockController block)
    {
        for (int i = 0; i < _owned.Count; i++)
        {
            if (_owned[i].Instance is not PassiveAbility passive) continue;
            if (!passive.TryInterceptLoss(Context, block)) continue;

            ConsumeCharge(_owned[i]);
            return true;
        }
        return false;
    }

    // ---- Event fan-out ------------------------------------------------------------------------

    private void HandleLifeLost()
    {
        FanOutToPassives(passive => passive.OnLifeLost(Context));
        RecomputeFallSpeedMultiplier(); // last-life style factors depend on lives
    }

    private void HandleBlockSpawned(BlockController block, BlockData data)
    {
        FanOutToPassives(passive => passive.OnBlockSpawned(Context, block, data));
        RecomputeFallSpeedMultiplier(); // per-block windows count down on spawn
    }

    // Fan-outs iterate a snapshot in ACQUISITION ORDER (the documented rule); the only
    // mid-event mutation is an ability consuming itself, which happens after its own
    // handler ran, so snapshot entries are never stale for anyone else.
    private readonly List<OwnedAbility> _fanOutScratch = new List<OwnedAbility>();

    /// <summary>Called by the ComboDetector after a trigger match survives revalidation.</summary>
    public void HandleComboFired(ComboTriggerDefinition trigger, ComboMatch match)
    {
        _fanOutScratch.Clear();
        _fanOutScratch.AddRange(_owned);
        for (int i = 0; i < _fanOutScratch.Count; i++)
        {
            OwnedAbility owned = _fanOutScratch[i];
            if (owned.Instance is not ComboAbility combo || combo.Trigger != trigger) continue;

            combo.OnComboFired(Context, match);
            ConsumeCharge(owned);
        }
        InventoryChanged?.Invoke();
    }

    private void FanOutToPassives(System.Func<PassiveAbility, bool> handler)
    {
        _fanOutScratch.Clear();
        _fanOutScratch.AddRange(_owned);
        for (int i = 0; i < _fanOutScratch.Count; i++)
        {
            if (_fanOutScratch[i].Instance is not PassiveAbility passive) continue;
            if (handler(passive)) ConsumeCharge(_fanOutScratch[i]);
        }
    }

    private void ConsumeCharge(OwnedAbility owned)
    {
        if (owned.ChargesLeft <= 0) return; // 0 = infinite

        owned.ChargesLeft--;
        if (owned.ChargesLeft > 0) return;

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
