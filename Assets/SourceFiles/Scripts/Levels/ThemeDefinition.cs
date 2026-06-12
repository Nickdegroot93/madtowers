using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An Archero-style chapter: a presentation skin plus an ordered run of levels. Themes live in
/// Assets/Resources/Themes/ (loaded by path), play in sortOrder, and their levels play in array
/// order - any count per theme. Presentation here applies to every level in the theme; per-level
/// rules stay on each LevelDefinition/GameModeConfig.
/// </summary>
[CreateAssetMenu(fileName = "ThemeDefinition", menuName = "Stacking/Levels/Theme Definition")]
public class ThemeDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string displayName = "Theme";
    [Tooltip("Themes are played lowest-first. Leave gaps (10, 20, 30) so inserting later is painless.")]
    [SerializeField] private int sortOrder = 0;

    [Header("Levels (played in this order)")]
    [SerializeField] private LevelDefinition[] levels;

    [Header("Presentation (shared by all levels in the theme)")]
    [Tooltip("Layered backdrop (sky/clouds/hills/particles). Empty = the classic dark sky.")]
    [SerializeField] private BackdropPreset backdrop;
    [SerializeField] private AudioClip music;
    [Tooltip("Theme soundtrack, played in order and looped as a whole (A, B, A, B...). Takes priority over the single 'music' clip above.")]
    [SerializeField] private AudioClip[] musicPlaylist;
    [Tooltip("Resources folder with this theme's generated skin (blocks/ground/laser). Empty = Skins/Classic. See ART.md.")]
    [SerializeField] private string skinFolder = "";

    [Header("Progression")]
    [Tooltip("Always playable regardless of campaign progress (testing/sandbox themes).")]
    [SerializeField] private bool alwaysUnlocked = false;

    [Header("Unlocks")]
    [Tooltip("Power-ups that become part of the game from this theme onward. Shown as 'NEW!' when the theme unlocks; actual availability is authored in each level's power-up pool.")]
    [SerializeField] private AbilityDefinition[] featuredUnlocks;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public int SortOrder => sortOrder;
    public IReadOnlyList<LevelDefinition> Levels => levels;
    public BackdropPreset Backdrop => backdrop;
    public AudioClip Music => music;
    /// <summary>The playlist if authored, else the single music clip wrapped, else empty.</summary>
    public IReadOnlyList<AudioClip> MusicPlaylist =>
        musicPlaylist != null && musicPlaylist.Length > 0
            ? musicPlaylist
            : (music != null ? new[] { music } : System.Array.Empty<AudioClip>());
    public string SkinFolder => string.IsNullOrWhiteSpace(skinFolder) ? "Skins/Classic" : skinFolder;
    public bool AlwaysUnlocked => alwaysUnlocked;
    public IReadOnlyList<AbilityDefinition> FeaturedUnlocks => featuredUnlocks;

    /// <summary>The level after the given one within this theme, or null if it was the last.</summary>
    public LevelDefinition GetNextLevel(LevelDefinition current)
    {
        if (levels == null) return null;

        for (int i = 0; i < levels.Length - 1; i++)
        {
            if (levels[i] == current) return levels[i + 1];
        }

        return null;
    }
}
