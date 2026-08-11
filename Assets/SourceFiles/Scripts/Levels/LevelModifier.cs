using UnityEngine;

/// <summary>
/// Everything a level modifier may act on. Extend this (rather than hook signatures) when a
/// modifier needs access to more of the game.
/// </summary>
public sealed class LevelModifierContext
{
    public GameManager GameManager;
    public Spawner Spawner;
    public StatusEffects Status;
    public LevelDefinition Level;
}

/// <summary>
/// The escape hatch for levels that need behaviour beyond settings: a composable
/// ScriptableObject attached to a LevelDefinition's Modifiers list. Same authoring pattern as
/// power-ups - subclass, override the hooks you need, create an asset, drag it onto a level.
/// The runtime clones each modifier per run, so instance fields are safe per-play state.
/// See EarthquakeModifier for a complete example.
/// </summary>
public abstract class LevelModifier : ScriptableObject
{
    /// <summary>Called once when the level begins.</summary>
    public virtual void OnLevelStart(LevelModifierContext context) { }

    /// <summary>Called every frame while the level runs (not while paused).</summary>
    public virtual void OnUpdate(LevelModifierContext context, float deltaTime) { }

    /// <summary>Called each time a block locks into the tower.</summary>
    public virtual void OnBlockLocked(LevelModifierContext context, int totalBlocksPlaced) { }

    /// <summary>Called whenever the LIVE standing-block count changes: +1 per counting
    /// placement, -1 when a counting block is destroyed or falls (BLOCKS.md). Progression that
    /// must not credit blocks that no longer stand (puzzle waves) keys off this, never off
    /// cumulative placements.</summary>
    public virtual void OnStandingBlocksChanged(LevelModifierContext context, int standingBlocks) { }

    /// <summary>A modifier that owns the level's metric may replace the score reported to
    /// personal bests and the leaderboard (encoded waves, not raw blocks). First non-null
    /// wins; null leaves the run's raw score untouched.</summary>
    public virtual int? OverrideReportedScore(LevelModifierContext context, int rawScore) => null;

    /// <summary>Called once when the level tears down (scene unload). Unsubscribe from any
    /// events and destroy any UI built in OnLevelStart here - the runtime clone is otherwise
    /// kept alive by a live static-event subscription, leaking it (and firing stale handlers
    /// against destroyed objects) after a scene reload.</summary>
    public virtual void OnLevelEnd(LevelModifierContext context) { }

    /// <summary>True while this modifier owns the level's intro messaging, so the runtime
    /// should not stack its own goal banner on top (the first-run tutorial shows the goal
    /// itself when its lessons end).</summary>
    public virtual bool SuppressesGoalBanner => false;

    /// <summary>Game-type design lockout: abilities that must never be OFFERED while this
    /// modifier runs the level (e.g. anchor sources trivialize the wave puzzle). Consulted by
    /// <see cref="LevelDefinition.IsAbilityBanned"/> alongside the level's own banned list, so
    /// the ban rides the modifier into every level that uses it - no per-level authoring.</summary>
    public virtual bool BansAbility(AbilityDefinition ability) => false;

    /// <summary>True removes RUN LIVES from the level entirely (the Flood: losing bricks is
    /// its own punishment, the only death is the modifier's - Nick 2026-08-10). Block losses
    /// and hazard bites charge nothing and never end the run, the hearts HUD hides, and the
    /// pre-run lives shop skips the level. The modifier owns ending the run (EndRunNow).</summary>
    public virtual bool DisablesRunLives => false;
}
