using UnityEngine;

/// <summary>
/// Everything the win system needs to read about the live run. Extend this (not the method
/// signatures) when a new win condition needs more of the game - the AbilityContext pattern.
/// </summary>
public readonly struct WinContext
{
    public readonly GameManager GameManager;
    private readonly System.Func<float> _liveTowerHeight;

    public WinContext(GameManager gameManager, System.Func<float> liveTowerHeight)
    {
        GameManager = gameManager;
        _liveTowerHeight = liveTowerHeight;
    }

    /// <summary>Height (m) of the blocks STANDING right now (not the monotonic record). Lazily
    /// computed, so a condition that doesn't care (PlaceBlocks) never pays for the per-block walk.</summary>
    public float LiveTowerHeight => _liveTowerHeight != null ? _liveTowerHeight() : 0f;
}

/// <summary>
/// The single stat an end-of-run screen leads with: the run's value and the stored personal
/// best in the SAME unit, plus how to print them. Produced by the level's WinCondition - or by
/// a modifier that owns the level's progress presentation (puzzle waves), via
/// <see cref="ILevelMenuProgressProvider"/> - so the results card never switches on the goal enum.
/// </summary>
public readonly struct ResultMetric
{
    public ResultMetric(string label, float value, float previousBest, bool isMeters, string targetText)
    {
        Label = label;
        Value = value;
        PreviousBest = previousBest;
        IsMeters = isMeters;
        TargetText = targetText;
    }

    /// <summary>Uppercase metric name for the card ("BLOCKS", "HEIGHT", "WAVES CLEARED").</summary>
    public string Label { get; }
    /// <summary>The run's result in the metric's own unit.</summary>
    public float Value { get; }
    /// <summary>The stored personal best in the same unit; 0 when none is recorded yet.</summary>
    public float PreviousBest { get; }
    /// <summary>Format values as meters ("12.4m") instead of a whole number.</summary>
    public bool IsMeters { get; }
    /// <summary>The goal in display form ("100", "12M", "4 WAVES"); null when there is no goal.</summary>
    public string TargetText { get; }

    public string Format(float value) => IsMeters ? $"{value:F1}m" : Mathf.RoundToInt(value).ToString();

    /// <summary>Did this run beat a GENUINE stored best? A first attempt (no previous best) is
    /// not a record - there was nothing to beat, and gold on every first run would cheapen it.
    /// Meters carry a small epsilon so a run that merely re-renders the same displayed height
    /// never claims a record.</summary>
    public bool IsNewRecord => PreviousBest > 0f && Value > PreviousBest + (IsMeters ? 0.049f : 0f);
}

/// <summary>
/// A level's victory rule. Polymorphic so a NEW game type is ONE self-contained subclass: the
/// gameplay (arming + hold-steady verification), the rarity-escalation progress, and the menu
/// presentation all live on the condition - instead of being scattered across LevelRuntimeController,
/// AbilityChoiceController and the menu as parallel switches on a LevelTargetType enum.
///
/// The built-ins (see Levels/WinConditions/) map from the level's authored LevelTargetType in
/// LevelDefinition.WinCondition - the single place that enum is translated. To add a brand-new game
/// type: subclass this in Levels/WinConditions/ and add it to that one factory (or, later, reference
/// it directly so even the factory edit goes away). Stateless apart from the immutable target, so the
/// per-run cloning the modifiers need isn't required here.
/// </summary>
public abstract class WinCondition
{
    /// <summary>False = no victory rule (Endless free-play): never arms a win, shows no goal text.</summary>
    public virtual bool HasGoal => true;

    /// <summary>Live: is the goal achieved by the standing tower right now? (arming + re-arm poll)</summary>
    public abstract bool IsMet(in WinContext ctx);

    /// <summary>During the hold-steady countdown: does the goal STILL hold? Defaults to IsMet; override
    /// to add hysteresis slack (ReachHeight tolerates a wobbling peak block so the countdown can't flicker).</summary>
    public virtual bool IsStillHeld(in WinContext ctx) => IsMet(in ctx);

    /// <summary>Goals that arm from a MONOTONIC signal (the height record only rises) can't be re-armed by
    /// that signal after a collapse, so they re-arm by polling IsMet. Event-driven goals (PlaceBlocks,
    /// whose live count re-crosses the target) don't need it. Default off.</summary>
    public virtual bool ReArmsByPolling => false;

    /// <summary>True when the run has a main clock that fails the level on expiry.</summary>
    public virtual bool HasTimeLimit => false;

    /// <summary>Main-clock duration in seconds. Ignored when <see cref="HasTimeLimit"/> is false.</summary>
    public virtual float TimeLimitSeconds => 0f;

    /// <summary>0..1 progress toward the goal, read live for ability rarity escalation (offers near the
    /// goal are spicier than offers at the start).</summary>
    public abstract float RunProgress01(GameManager gameManager);

    /// <summary>The ONE stat the end-of-run card leads with, in this goal's own unit - the run's
    /// value plus the stored best for the record comparison. Callers must resolve this BEFORE
    /// reporting the run to ProgressStore (reporting overwrites the best being compared against).</summary>
    public abstract ResultMetric EndOfRunMetric(RunResult result, ProgressStore.LevelBest best);

    // ---- Menu presentation (no live run; reads the saved best) -------------------------------------

    /// <summary>Uppercase metric label for the level card (e.g. "HEIGHT CHALLENGE").</summary>
    public abstract string MenuChallengeLabel { get; }

    /// <summary>(primary, suffix) progress parts for the level card.</summary>
    public abstract (string primary, string suffix) MenuProgress(ProgressStore.LevelBest best, bool completed);

    /// <summary>(target, best) lines for the level summary modal. <paramref name="attempted"/> = ever
    /// played or completed (controls the "-" placeholder).</summary>
    public abstract (string target, string best) TargetAndBest(ProgressStore.LevelBest best, bool completed, bool attempted);

    /// <summary>How a leaderboard row prints this level's stored score. Null = the raw number.
    /// Goals whose stored scores are ENCODED (ClearWaves packs waves + in-wave progress into one
    /// int) override this so the board shows the metric, never the encoding.</summary>
    public virtual string FormatBoardScore(int bestScore) => null;
}
