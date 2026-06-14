using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The in-memory model the Custom Game setup screen edits, and the single source the runtime
/// GameModeConfig/LevelDefinition are built from. Seeded from a base GameModeConfig (a preset)
/// so every field starts at a sane default; the screen mutates it; <see cref="GameModeConfig
/// .ApplyCustomGameOverrides"/> reads it back. Holds only the CURATED gameplay knobs - the
/// fiddly physics/camera tuning stays at the preset's values.
///
/// Lives only at runtime (a dev/testing tool); never serialized to an asset.
/// </summary>
public sealed class CustomGameSettings
{
    // Round
    public int StartingLives;
    public float InitialFallSpeed;
    public float MaxFallSpeed;
    public DifficultyScalingMode DifficultyScalingMode;
    public DifficultyAdjustmentMode DifficultyAdjustmentMode;
    public float SpeedIncreasePerBlock;
    public float SpeedIncreaseIntervalSeconds;
    public float SpeedIncreasePerInterval;

    // Play area
    public int FloorColumns;

    // Spawning
    public float SpawnDelay;
    public int PowerUpChoiceEveryBlocks;
    public bool StaticIslandsEnabled;
    public float StaticIslandSpawnChance;

    // Goal
    public LevelTargetType TargetType;
    public float TargetValue;

    // Content (toggled on/off in the screen; auto-discovered, so new assets just appear)
    public readonly HashSet<BlockDefinition> EnabledBlocks = new HashSet<BlockDefinition>();
    public readonly HashSet<AbilityDefinition> EnabledAbilities = new HashSet<AbilityDefinition>();

    /// <summary>Seed the numeric knobs from a preset config. Content (blocks/abilities) is
    /// filled separately by the screen from the catalogs so newly-added assets default on.</summary>
    public static CustomGameSettings FromConfig(GameModeConfig c)
    {
        var s = new CustomGameSettings
        {
            StartingLives = c != null ? c.StartingLives : 3,
            InitialFallSpeed = c != null ? c.InitialFallSpeed : 2f,
            MaxFallSpeed = c != null ? c.MaxFallSpeed : 5f,
            DifficultyScalingMode = c != null ? c.DifficultyScalingMode : DifficultyScalingMode.PerBlock,
            DifficultyAdjustmentMode = c != null ? c.DifficultyAdjustmentMode : DifficultyAdjustmentMode.Additive,
            SpeedIncreasePerBlock = c != null ? c.SpeedIncreasePerBlock : 0.1f,
            SpeedIncreaseIntervalSeconds = c != null ? c.SpeedIncreaseIntervalSeconds : 60f,
            SpeedIncreasePerInterval = c != null ? c.SpeedIncreasePerInterval : 0.1f,
            FloorColumns = c != null ? c.FloorColumnCount : 9,
            SpawnDelay = c != null ? c.SpawnDelay : 0f,
            PowerUpChoiceEveryBlocks = c != null ? c.PowerUpChoiceEveryBlocks : 10,
            StaticIslandsEnabled = c != null && c.StaticSupportIslandsEnabled,
            StaticIslandSpawnChance = c != null ? c.StaticSupportIslandSpawnChance : 0.25f,
            TargetType = LevelTargetType.Endless,
            TargetValue = 25f
        };
        return s;
    }
}
