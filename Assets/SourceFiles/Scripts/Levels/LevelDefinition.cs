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
    [Tooltip("Seconds available for timed block-count / height goals. Ignored by untimed goals.")]
    [Min(1)]
    [SerializeField] private float timeLimitSeconds = 120f;
    [Tooltip("Silver medal target in target units. 0 = the LevelTiers formula (x1.25). Ignored by ClearWaves (always bronze+1 wave) and Endless.")]
    [Min(0)]
    [SerializeField] private float silverTargetOverride = 0f;
    [Tooltip("Gold medal target in target units. 0 = the LevelTiers formula (x1.6). Ignored by ClearWaves (always bronze+2 waves) and Endless.")]
    [Min(0)]
    [SerializeField] private float goldTargetOverride = 0f;
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
    public float TimeLimitSeconds => Mathf.Max(1f, timeLimitSeconds);
    public float SilverTargetOverride => Mathf.Max(0f, silverTargetOverride);
    public float GoldTargetOverride => Mathf.Max(0f, goldTargetOverride);
    public string Instruction => instruction;

#if UNITY_EDITOR
    // Authoring guard for the medal ladder: LevelTiers.Threshold clamps each rung to at least
    // the one below it at runtime, so an inverted override never corrupts derivation - but the
    // clamp silently flattens the ladder (two rungs sharing one goal earn together). Flag it
    // here, where the author is looking.
    private void OnValidate()
    {
        if (!LevelTiers.HasTiers(this) || targetType == LevelTargetType.ClearWaves) return;

        float bronze = TargetValue;
        float silver = LevelTiers.Threshold(this, MedalTier.Silver);
        float gold = LevelTiers.Threshold(this, MedalTier.Gold);
        if ((silverTargetOverride > 0f && silverTargetOverride <= bronze) ||
            (goldTargetOverride > 0f && goldTargetOverride <= silver) ||
            (silverTargetOverride > 0f && goldTargetOverride <= 0f && gold <= silver))
        {
            Debug.LogWarning($"{name}: medal overrides don't rise (bronze {bronze} / silver " +
                $"{silver} / gold {gold}) - runtime clamps them monotone, but rungs sharing a " +
                "goal are earned together. Author bronze < silver < gold.", this);
        }
    }
#endif

    /// <summary>The level's victory rule as a polymorphic <see cref="WinCondition"/>. This is the
    /// SINGLE place the authored <see cref="LevelTargetType"/> enum is translated into behaviour;
    /// every consumer (verification, rarity progress, menu) reads the condition, never the enum. To
    /// add a new game type, add a WinCondition subclass and one case here. Built fresh on access
    /// (cheap, immutable target) so an inspector tweak to the target during play is never stale;
    /// the runtime controller caches one for the run.</summary>
    public WinCondition WinCondition => WinConditionFor(TargetValue);

    /// <summary>The same victory rule aimed at an arbitrary target - how the medal ladder arms
    /// silver/gold: one condition instance per tier threshold (conditions are immutable), built
    /// from the SAME translation so a tier can never disagree with the level's goal type.</summary>
    public WinCondition WinConditionFor(float target) => targetType switch
    {
        LevelTargetType.PlaceBlocks => new PlaceBlocksWinCondition(target),
        LevelTargetType.ReachHeight => new ReachHeightWinCondition(target),
        LevelTargetType.TimedPlaceBlocks => new TimedWinCondition(
            new PlaceBlocksWinCondition(target), TimeLimitSeconds),
        LevelTargetType.TimedReachHeight => new TimedWinCondition(
            new ReachHeightWinCondition(target), TimeLimitSeconds),
        LevelTargetType.ClearWaves => new ClearWavesWinCondition(target),
        _ => new EndlessWinCondition(),
    };
    public IReadOnlyList<LevelModifier> Modifiers => modifiers;
    public AbilityRarityProfile AbilityRarityProfile => abilityRarityProfile;

    /// <summary>Runtime only (Custom Game screen): build a throwaway level around a runtime
    /// GameModeConfig. Held alive by LevelSelectionState across the scene load; never an asset.</summary>
    public static LevelDefinition CreateRuntime(string name, GameModeConfig config,
        LevelTargetType targetType, float targetValue, AbilityRarityProfile rarityProfile,
        float timeLimitSeconds = 120f)
    {
        LevelDefinition level = CreateInstance<LevelDefinition>();
        level.displayName = name;
        level.gameModeConfig = config;
        level.targetType = targetType;
        level.targetValue = Mathf.Max(1f, targetValue);
        level.timeLimitSeconds = Mathf.Max(1f, timeLimitSeconds);
        level.abilityRarityProfile = rarityProfile;
        return level;
    }

    /// <summary>Does any modifier run this level (twist rules AND the tutorial count)?
    /// A pure classic - block-count goal, no modifiers - is the game's easiest type and
    /// gets the hot-speed treatment (GameManager.ApplyConfig).</summary>
    public bool HasAnyModifier
    {
        get
        {
            if (modifiers == null) return false;
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i] != null) return true;
            }
            return false;
        }
    }

    /// <summary>Free RUN LIVES the level's game type grants at run start (Flood levels: the
    /// cap). The strongest modifier wins; see <see cref="LevelModifier.GrantedRunLives"/>.</summary>
    public int GrantedRunLives
    {
        get
        {
            int granted = 0;
            if (modifiers == null) return granted;
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i] != null) granted = Mathf.Max(granted, modifiers[i].GrantedRunLives);
            }
            return granted;
        }
    }

    public bool IsAbilityBanned(AbilityDefinition ability)
    {
        if (ability == null) return false;

        if (bannedAbilities != null)
        {
            for (int i = 0; i < bannedAbilities.Length; i++)
            {
                if (bannedAbilities[i] == ability) return true;
            }
        }

        // Game-type lockouts ride the modifier (LevelModifier.BansAbility), so every level
        // running that mode inherits them without per-level authoring.
        if (modifiers != null)
        {
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i] != null && modifiers[i].BansAbility(ability)) return true;
            }
        }
        return false;
    }
}
