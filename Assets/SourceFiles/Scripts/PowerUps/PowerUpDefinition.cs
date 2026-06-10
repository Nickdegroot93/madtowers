using UnityEngine;

/// <summary>
/// Everything a power-up may act on. Extend this (rather than method signatures) when new
/// power-ups need access to more of the game.
/// </summary>
public sealed class PowerUpContext
{
    public GameManager GameManager;
    public Spawner Spawner;
}

public enum PowerUpCategory
{
    /// <summary>One-shot effect (extra life, slow time, ...).</summary>
    Instant,
    /// <summary>Changes which/how blocks spawn from now on.</summary>
    BlockModifier,
    /// <summary>Acts on the tower that is already standing.</summary>
    TowerModifier
}

/// <summary>
/// Base class for every power-up the player can be offered. To add a new power-up:
/// 1. If no existing behaviour fits, create a subclass implementing Apply()
///    (one file in Scripts/PowerUps/Definitions/). Many power-ups can reuse an
///    existing class with different field values - then skip this step.
/// 2. Create an asset for it (right-click > Create > Stacking > Power Ups > ...)
///    in Assets/Data/PowerUps/&lt;Rarity&gt;/ and fill in name/description/rarity.
///    The rarity FIELD is what the game uses; the folder is just organization.
/// 3. Add the asset to a game mode's Power Up Choice Pool.
/// </summary>
public abstract class PowerUpDefinition : ScriptableObject
{
    [Header("Presentation")]
    [SerializeField] private string displayName = "Power-Up";
    [TextArea]
    [SerializeField] private string description = "";

    [Header("Classification")]
    [SerializeField] private PowerUpRarity rarity = PowerUpRarity.Common;
    [SerializeField] private PowerUpCategory category = PowerUpCategory.Instant;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public string Description => description;
    public PowerUpRarity Rarity => rarity;
    public PowerUpCategory Category => category;

    public abstract void Apply(PowerUpContext context);
}
