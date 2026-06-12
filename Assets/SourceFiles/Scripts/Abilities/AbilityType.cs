using UnityEngine;

/// <summary>
/// The player-facing ability type shown as a badge on cards. DERIVED, never authored:
/// the kind comes from the definition's class and "one-time" from its charges, so the
/// badge can never contradict what the ability actually does.
/// </summary>
public enum AbilityType
{
    /// <summary>Applies once at pick (extra life, slow motion).</summary>
    Instant,
    /// <summary>Held in a slot, fired by the player.</summary>
    Consumable,
    /// <summary>Always on for the rest of the run (combo abilities included).</summary>
    Passive,
    /// <summary>Armed until it triggers, then gone (a passive with charges).</summary>
    OneTimePassive
}

public static class AbilityTypeInfo
{
    public static string GetLabel(AbilityType type)
    {
        switch (type)
        {
            case AbilityType.Consumable: return "CONSUMABLE";
            case AbilityType.Passive: return "PASSIVE";
            case AbilityType.OneTimePassive: return "ONE-TIME PASSIVE";
            default: return "INSTANT";
        }
    }

    public static Color GetColor(AbilityType type)
    {
        switch (type)
        {
            case AbilityType.Consumable: return new Color(0.45f, 0.85f, 0.95f); // cyan: in your hands
            case AbilityType.Passive: return new Color(0.55f, 0.9f, 0.6f);      // green: always working
            case AbilityType.OneTimePassive: return new Color(0.95f, 0.8f, 0.45f); // amber: spends itself
            default: return new Color(0.85f, 0.85f, 0.9f);                      // gray: fire and forget
        }
    }
}
