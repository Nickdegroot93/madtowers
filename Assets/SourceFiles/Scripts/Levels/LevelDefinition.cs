using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelDefinition", menuName = "Stacking/Levels/Level Definition")]
public class LevelDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string displayName = "Level";

    [Header("Menu Presentation")]
    [Tooltip("Optional square image for the campaign menu card. Empty = generated placeholder.")]
    [SerializeField] private Sprite menuThumbnail;
    [Tooltip("Optional uppercase label for the card metric. Empty = inferred from target/modifiers.")]
    [SerializeField] private string menuChallengeLabelOverride = "";

    [Header("Rules")]
    [SerializeField] private GameModeConfig gameModeConfig;

    [Header("Goal")]
    [SerializeField] private LevelTargetType targetType = LevelTargetType.Endless;
    [Tooltip("Blocks to place or height in meters, depending on the target type.")]
    [Min(1)]
    [SerializeField] private float targetValue = 10f;
    [Tooltip("One-sentence player instruction shown as a banner when the level starts. Empty = no banner.")]
    [SerializeField] private string instruction = "";

    [Header("Custom Behaviour")]
    [Tooltip("Optional composable behaviours beyond settings (earthquakes, wind, ...). See LevelModifier.")]
    [SerializeField] private LevelModifier[] modifiers;

    [Header("Abilities")]
    [Tooltip("Abilities that must never be offered on this level (deliberate design lockouts). Content-dependent conditions are automatic and live on the ability itself.")]
    [SerializeField] private AbilityDefinition[] bannedAbilities;
    [Tooltip("Overrides the rarity odds of ability offers for this level (e.g. a legendaries-only gimmick). Empty = the built-in progress-scaled defaults.")]
    [SerializeField] private AbilityRarityProfile abilityRarityProfile;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite MenuThumbnail => menuThumbnail;
    public string MenuChallengeLabelOverride => menuChallengeLabelOverride;
    public GameModeConfig GameModeConfig => gameModeConfig;
    public LevelTargetType TargetType => targetType;
    public float TargetValue => Mathf.Max(1f, targetValue);
    public string Instruction => instruction;
    public IReadOnlyList<LevelModifier> Modifiers => modifiers;
    public AbilityRarityProfile AbilityRarityProfile => abilityRarityProfile;

    /// <summary>Runtime only (Custom Game screen): build a throwaway level around a runtime
    /// GameModeConfig. Held alive by LevelSelectionState across the scene load; never an asset.</summary>
    public static LevelDefinition CreateRuntime(string name, GameModeConfig config,
        LevelTargetType targetType, float targetValue, AbilityRarityProfile rarityProfile)
    {
        LevelDefinition level = CreateInstance<LevelDefinition>();
        level.displayName = name;
        level.gameModeConfig = config;
        level.targetType = targetType;
        level.targetValue = Mathf.Max(1f, targetValue);
        level.abilityRarityProfile = rarityProfile;
        return level;
    }

    public bool IsAbilityBanned(AbilityDefinition ability)
    {
        if (bannedAbilities == null || ability == null) return false;

        for (int i = 0; i < bannedAbilities.Length; i++)
        {
            if (bannedAbilities[i] == ability) return true;
        }
        return false;
    }
}
