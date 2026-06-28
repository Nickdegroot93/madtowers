using UnityEngine;

/// <summary>
/// Everything an ability may act on or consult. Extend this (rather than method
/// signatures) when new abilities need access to more of the game. One context type
/// serves picking, availability filtering, activation and event handlers alike.
/// </summary>
public sealed class AbilityContext
{
    public GameManager GameManager;
    public Spawner Spawner;
    public AbilityRuntime Runtime;
    public StatusEffects Status;
    public GameModeConfig Config;
    public LevelDefinition Level;
    public HoldCache Hold;

    /// <summary>True if the level's spawn tables can produce this variant (ambient
    /// chances or fallback variants) - the automatic availability condition.</summary>
    public bool LevelHasVariant(BlockData variant)
    {
        if (variant == null || Config == null) return false;

        var ambient = Config.AmbientBlockVariantChances;
        if (ambient != null)
        {
            for (int i = 0; i < ambient.Count; i++)
            {
                if (ambient[i] != null && ambient[i].Variant == variant) return true;
            }
        }

        var fallback = Config.FallbackBlockDataVariants;
        if (fallback != null)
        {
            for (int i = 0; i < fallback.Count; i++)
            {
                if (fallback[i] == variant) return true;
            }
        }

        return false;
    }

    /// <summary>How many DISTINCT hazard variants (BlockData.IsHazard) the level's spawn tables can
    /// produce - the gate for the "all hazards" abilities (Purifier needs several, Ward needs one).</summary>
    public int LevelHazardVariantCount()
    {
        if (Config == null) return 0;

        var seen = new System.Collections.Generic.HashSet<BlockData>();
        var ambient = Config.AmbientBlockVariantChances;
        if (ambient != null)
        {
            for (int i = 0; i < ambient.Count; i++)
                if (ambient[i] != null && ambient[i].Variant != null && ambient[i].Variant.IsHazard)
                    seen.Add(ambient[i].Variant);
        }

        var fallback = Config.FallbackBlockDataVariants;
        if (fallback != null)
        {
            for (int i = 0; i < fallback.Count; i++)
                if (fallback[i] != null && fallback[i].IsHazard) seen.Add(fallback[i]);
        }

        return seen.Count;
    }
}

/// <summary>
/// Base class for every ability the player can be offered. See ABILITIES.md for the
/// full architecture. The four kinds are the subclasses in Kinds/: InstantAbility
/// (apply-on-pick), ConsumableAbility (held in slots, manually fired), PassiveAbility
/// (always on; charges make it one-shot), ComboAbility (fires when its block-pattern
/// trigger matches). To add an ability:
/// 1. If no existing behaviour fits, subclass the right kind (one file in
///    Definitions/). Many abilities reuse an existing class with different field
///    values - then skip this step.
/// 2. Create an asset (right-click > Create > Stacking > Abilities > ...) in
///    Assets/Data/PowerUps/&lt;Rarity&gt;/. The rarity FIELD is what the game uses.
/// 3. Add the asset to a game mode's Power Up Choice Pool.
///
/// Definitions are immutable assets: AbilityRuntime clones an instance per acquisition
/// (the LevelModifier pattern), so instance fields are safe per-run state.
/// </summary>
public abstract class AbilityDefinition : ScriptableObject
{
    // Every ability carries the full presentation block: title, icon, short and long
    // description. UIs pick what fits (cards: icon + title + short; HUD slots: icon;
    // future detail view: everything). Short falls back to long and vice versa, so
    // half-authored assets degrade gracefully instead of rendering blank.
    [Header("Presentation")]
    [SerializeField] private string displayName = "Ability";
    [Tooltip("Card/HUD icon. Generated per ability later; empty shows the title text instead.")]
    [SerializeField] private Sprite icon;
    [Tooltip("One line for cards and the swap dialog.")]
    [SerializeField] private string shortDescription = "";
    [Tooltip("Optional one-liner shown on the card once the player ALREADY owns at least one - a stackable ability's 'what another pick adds' copy (e.g. \"Drops molten blocks more often\"), so a second+ card doesn't read like a first-time intro. Blank falls back to the short description.")]
    [SerializeField] private string ownedShortDescription = "";
    [Tooltip("The full explanation for the (future) details view.")]
    [TextArea]
    [SerializeField] private string description = "";

    [Header("Classification")]
    [SerializeField] private AbilityRarity rarity = AbilityRarity.Common;

    [Header("Ownership rules")]
    [Tooltip("Unique abilities can be picked exactly once per run (e.g. a queue-visibility upgrade that can't meaningfully stack).")]
    [SerializeField] private bool unique;
    [Tooltip("For stackable abilities: how many copies may be owned. 0 = unlimited. Ignored when Unique is set (that means 1).")]
    [Min(0)]
    [SerializeField] private int maxStacks;

    [Header("Availability")]
    [Tooltip("Offered only when the level's spawn tables (ambient chances or fallback variants) contain ALL of these variants - e.g. a 'no more Vortex bricks' ability needs Vortex bricks to exist. Empty = no requirement.")]
    [SerializeField] private BlockData[] requiresVariantsInLevel;
    [Tooltip("Offered only from this chapter number onward - the chapter where the relevant brick is introduced, so players are never offered an ability for a brick they've never seen (e.g. Magma from the volcano chapter). 0 = always available. Levels with no chapter (custom/endless) are never gated.")]
    [Min(0)]
    [SerializeField] private int minChapterNumber;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public string ShortDescription => string.IsNullOrWhiteSpace(shortDescription) ? description : shortDescription;
    public string LongDescription => string.IsNullOrWhiteSpace(description) ? shortDescription : description;

    /// <summary>Card short line for a given owned stack count. Once the player owns at least
    /// one and an "owned" line is authored, it explains what ANOTHER pick adds (a stackable's
    /// second+ card otherwise reads like a first-time intro); blank falls back to the normal
    /// short description. The long/detail text is intentionally left alone - it stays the full
    /// mechanic explainer regardless of stacks.</summary>
    public string ShortDescriptionFor(int ownedStacks) =>
        ownedStacks > 0 && !string.IsNullOrWhiteSpace(ownedShortDescription)
            ? ownedShortDescription
            : ShortDescription;
    public AbilityRarity Rarity => rarity;

    /// <summary>Player-facing type badge - derived from the kind class + charges, never
    /// authored, so it can't drift from what the ability actually does.</summary>
    public AbilityType Type
    {
        get
        {
            switch (this)
            {
                case ConsumableAbility: return AbilityType.Consumable;
                case PassiveAbility passive:
                    return passive.Charges > 0 ? AbilityType.OneTimePassive : AbilityType.Passive;
                case ComboAbility combo:
                    return combo.Charges > 0 ? AbilityType.OneTimePassive : AbilityType.Passive;
                default: return AbilityType.Instant;
            }
        }
    }
    public bool Unique => unique;
    /// <summary>Effective stack cap: unique = 1, otherwise the authored cap (0 = unlimited).</summary>
    public int EffectiveMaxStacks => unique ? 1 : maxStacks;

    /// <summary>
    /// Whether this ability may appear in an offer right now. The default covers the
    /// standard rules (unique/stack caps, level bans, required variants); override for
    /// exotic conditions and call base for the standard ones.
    /// </summary>
    public virtual bool IsAvailable(AbilityContext context, int ownedStacks)
    {
        if (unique && ownedStacks > 0) return false;
        if (EffectiveMaxStacks > 0 && ownedStacks >= EffectiveMaxStacks) return false;
        if (context.Level != null && context.Level.IsAbilityBanned(this)) return false;

        if (requiresVariantsInLevel != null)
        {
            for (int i = 0; i < requiresVariantsInLevel.Length; i++)
            {
                if (requiresVariantsInLevel[i] == null) continue;
                if (!context.LevelHasVariant(requiresVariantsInLevel[i])) return false;
            }
        }

        // Chapter gate: hide the ability until the chapter that introduces its brick. A chapter
        // of 0 means "no chapter resolved" (custom/endless) - those are never gated.
        if (minChapterNumber > 0 && context.GameManager != null)
        {
            int chapter = context.GameManager.CurrentChapterNumber;
            if (chapter > 0 && chapter < minChapterNumber) return false;
        }

        return true;
    }
}
