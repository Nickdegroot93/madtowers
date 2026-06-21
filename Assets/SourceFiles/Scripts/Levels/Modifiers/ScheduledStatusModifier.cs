using UnityEngine;

/// <summary>
/// Reusable level/chapter pressure-event scheduler. Add this modifier to a LevelDefinition,
/// then author one or more entries that periodically apply StatusEffectDefinition assets.
/// The status owns the actual effect; this asset only decides when it starts.
/// </summary>
[CreateAssetMenu(fileName = "ScheduledStatus", menuName = "Stacking/Levels/Modifiers/Scheduled Status")]
public class ScheduledStatusModifier : LevelModifier
{
    private enum TriggerMode
    {
        Time,
        BlockCount
    }

    [System.Serializable]
    private sealed class ScheduledStatusEvent
    {
        [SerializeField] private string label = "Scheduled Status";
        [SerializeField] private StatusEffectDefinition status;
        [SerializeField] private TriggerMode triggerMode = TriggerMode.Time;

        [Header("Timing")]
        [Tooltip("For Time mode: seconds between activations after the first activation.")]
        [Min(0.1f)]
        [SerializeField] private float intervalSeconds = 40f;
        [Tooltip("For Block Count mode: physical placed pieces between activations. Score bonuses do not affect this.")]
        [Min(1)]
        [SerializeField] private int intervalBlocks = 30;
        [Tooltip("Optional first wait for Time mode. 0 = use Interval Seconds for the first activation.")]
        [Min(0f)]
        [SerializeField] private float firstDelaySeconds = 0f;
        [Tooltip("No activation until this many physical pieces have been placed.")]
        [Min(0)]
        [SerializeField] private int graceBlocks = 0;
        [Tooltip("Apply once as soon as the level starts and grace rules allow it, then continue on the interval.")]
        [SerializeField] private bool triggerAtLevelStart = false;

        [Header("Status Overrides")]
        [SerializeField] private bool overrideDuration = false;
        [Min(0.1f)]
        [SerializeField] private float durationSeconds = 10f;
        [SerializeField] private bool overrideMagnitude = false;
        [SerializeField] private float magnitude = 1f;
        [Tooltip("If off, this entry waits for the same status to end before applying it again.")]
        [SerializeField] private bool reapplyWhileActive = true;

        string Label => string.IsNullOrWhiteSpace(label) ? "Scheduled Status" : label;
        StatusEffectDefinition Status => status;
        TriggerMode Mode => triggerMode;
        float IntervalSeconds => Mathf.Max(0.1f, intervalSeconds);
        int IntervalBlocks => Mathf.Max(1, intervalBlocks);
        float FirstDelaySeconds => firstDelaySeconds > 0f ? firstDelaySeconds : IntervalSeconds;
        int GraceBlocks => Mathf.Max(0, graceBlocks);
        bool TriggerAtLevelStart => triggerAtLevelStart;
        bool ReapplyWhileActive => reapplyWhileActive;
        float DurationOverride => overrideDuration ? Mathf.Max(0.1f, durationSeconds) : -1f;
        float MagnitudeOverride => overrideMagnitude ? magnitude : float.NaN;
    }

    private sealed class RuntimeEntry
    {
        public ScheduledStatusEvent Definition;
        public bool TimeStarted;
        public bool InitialTriggerApplied;
        public float TimeRemaining;
        public int NextBlockThreshold;
        public bool WarnedMissingStatus;
    }

    [Tooltip("One level can run several pressure events. Different statuses naturally overlap.")]
    [SerializeField] private ScheduledStatusEvent[] events;

    private RuntimeEntry[] _runtime;
    private LevelModifierContext _context;
    private int _totalBlocksPlaced;

    public override void OnLevelStart(LevelModifierContext context)
    {
        _context = context;
        _totalBlocksPlaced = 0;

        if (events == null || events.Length == 0)
        {
            _runtime = System.Array.Empty<RuntimeEntry>();
            return;
        }

        _runtime = new RuntimeEntry[events.Length];
        for (int i = 0; i < events.Length; i++)
        {
            ScheduledStatusEvent definition = events[i];
            RuntimeEntry entry = new RuntimeEntry { Definition = definition };
            _runtime[i] = entry;

            if (definition == null) continue;

            entry.TimeRemaining = definition.FirstDelaySeconds;
            entry.NextBlockThreshold = definition.GraceBlocks + definition.IntervalBlocks;

            if (definition.TriggerAtLevelStart && definition.GraceBlocks == 0)
            {
                TryApply(entry);
                entry.InitialTriggerApplied = true;
                entry.TimeRemaining = definition.IntervalSeconds;
            }
        }
    }

    public override void OnUpdate(LevelModifierContext context, float deltaTime)
    {
        if (_runtime == null || _runtime.Length == 0) return;

        for (int i = 0; i < _runtime.Length; i++)
        {
            RuntimeEntry entry = _runtime[i];
            ScheduledStatusEvent definition = entry != null ? entry.Definition : null;
            if (definition == null || definition.Mode != TriggerMode.Time) continue;
            if (_totalBlocksPlaced < definition.GraceBlocks) continue;

            if (!entry.TimeStarted)
            {
                entry.TimeStarted = true;
                if (definition.TriggerAtLevelStart && !entry.InitialTriggerApplied)
                {
                    TryApply(entry);
                    entry.InitialTriggerApplied = true;
                    entry.TimeRemaining = definition.IntervalSeconds;
                    continue;
                }
            }

            entry.TimeRemaining -= deltaTime;
            while (entry.TimeRemaining <= 0f)
            {
                TryApply(entry);
                entry.TimeRemaining += definition.IntervalSeconds;
            }
        }
    }

    public override void OnBlockLocked(LevelModifierContext context, int totalBlocksPlaced)
    {
        _totalBlocksPlaced = totalBlocksPlaced;
        if (_runtime == null || _runtime.Length == 0) return;

        for (int i = 0; i < _runtime.Length; i++)
        {
            RuntimeEntry entry = _runtime[i];
            ScheduledStatusEvent definition = entry != null ? entry.Definition : null;
            if (definition == null || definition.Mode != TriggerMode.BlockCount) continue;

            if (definition.TriggerAtLevelStart &&
                !entry.InitialTriggerApplied &&
                totalBlocksPlaced >= definition.GraceBlocks)
            {
                TryApply(entry);
                entry.InitialTriggerApplied = true;
            }

            while (totalBlocksPlaced >= entry.NextBlockThreshold)
            {
                TryApply(entry);
                entry.NextBlockThreshold += definition.IntervalBlocks;
            }
        }
    }

    private void TryApply(RuntimeEntry entry)
    {
        if (entry == null || entry.Definition == null) return;

        ScheduledStatusEvent definition = entry.Definition;
        StatusEffects status = ResolveStatusRuntime();
        if (definition.Status == null || status == null)
        {
            WarnMissingStatus(entry, status);
            return;
        }

        if (!definition.ReapplyWhileActive && status.IsActive(definition.Status)) return;

        status.Apply(definition.Status, definition.DurationOverride, definition.MagnitudeOverride);
    }

    private StatusEffects ResolveStatusRuntime()
    {
        if (_context == null) return null;
        if (_context.Status != null) return _context.Status;
        return _context.GameManager != null ? _context.GameManager.GetComponent<StatusEffects>() : null;
    }

    private void WarnMissingStatus(RuntimeEntry entry, StatusEffects status)
    {
        if (entry.WarnedMissingStatus) return;
        entry.WarnedMissingStatus = true;

        string reason = entry.Definition.Status == null
            ? "has no StatusEffectDefinition assigned"
            : "could not find the StatusEffects runtime on the GameManager";
        Debug.LogWarning($"[ScheduledStatus] '{entry.Definition.Label}' {reason}.", this);
    }
}
