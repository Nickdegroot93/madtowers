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

    /// <summary>0..1 progress toward the goal, read live for ability rarity escalation (offers near the
    /// goal are spicier than offers at the start).</summary>
    public abstract float RunProgress01(GameManager gameManager);

    // ---- Menu presentation (no live run; reads the saved best) -------------------------------------

    /// <summary>Uppercase metric label for the level card (e.g. "HEIGHT CHALLENGE").</summary>
    public abstract string MenuChallengeLabel { get; }

    /// <summary>(primary, suffix) progress parts for the level card.</summary>
    public abstract (string primary, string suffix) MenuProgress(ProgressStore.LevelBest best, bool completed);

    /// <summary>(target, best) lines for the level summary modal. <paramref name="attempted"/> = ever
    /// played or completed (controls the "-" placeholder).</summary>
    public abstract (string target, string best) TargetAndBest(ProgressStore.LevelBest best, bool completed, bool attempted);
}
