using UnityEngine;

/// <summary>
/// Everything a level modifier may act on. Extend this (rather than hook signatures) when a
/// modifier needs access to more of the game.
/// </summary>
public sealed class LevelModifierContext
{
    public GameManager GameManager;
    public Spawner Spawner;
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
}
