using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// An Archero-style chapter: a menu skin, gameplay presentation, and ordered run of levels.
/// Chapters live in Assets/Resources/Chapters/ (loaded by path), play in sortOrder, and their
/// levels play in array order - any count per chapter. Per-level rules stay on each
/// LevelDefinition/GameModeConfig.
/// </summary>
[CreateAssetMenu(fileName = "ChapterDefinition", menuName = "Stacking/Levels/Chapter Definition")]
public class ChapterDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string displayName = "Chapter";
    [Min(1)]
    [SerializeField] private int chapterNumber = 1;
    [Tooltip("Chapters are played lowest-first. Leave gaps (10, 20, 30) so inserting later is painless.")]
    [SerializeField] private int sortOrder = 0;

    [Header("Levels (played in this order)")]
    [SerializeField] private LevelDefinition[] levels;

    [Header("Menu Presentation")]
    [Tooltip("Optional looping video behind the chapter menu. Empty = procedural placeholder/fallback.")]
    [SerializeField] private VideoClip menuBackgroundVideo;
    [Tooltip("Still background shown before/without video. Empty = procedural placeholder.")]
    [SerializeField] private Sprite menuBackgroundImage;
    [Tooltip("Small locked-preview image for the next chapter card. Empty = procedural placeholder.")]
    [SerializeField] private Sprite nextChapterPreviewImage;
    [Tooltip("True when the TOP of the menu background image is LIGHT (pale sky, sand): the chapter eyebrow + title then render in dark ink instead of cream, so they stay readable without drop shadows.")]
    [SerializeField] private bool menuTopIsLight = false;
    [SerializeField] private Color menuAccentColor = new Color(1f, 0.62f, 0.18f, 1f);
    [SerializeField] private Color menuAccentSecondaryColor = new Color(0.58f, 0.38f, 0.16f, 1f);
    [SerializeField] private Color menuPanelColor = new Color(0.12f, 0.09f, 0.06f, 0.72f);
    [SerializeField] private Color playButtonTopColor = new Color(1f, 0.72f, 0.27f, 1f);
    [SerializeField] private Color playButtonBottomColor = new Color(0.88f, 0.38f, 0.08f, 1f);

    [Header("Gameplay Presentation (shared by all levels in the chapter)")]
    [Tooltip("Layered backdrop (sky/clouds/hills/particles). Empty = the classic dark sky.")]
    [SerializeField] private BackdropPreset backdrop;
    [Tooltip("Chapter soundtrack, played in order and looped as a whole (A, B, A, B...).")]
    [SerializeField] private AudioClip[] musicPlaylist;
    [Tooltip("Resources folder with this chapter's generated skin (blocks/ground/laser). Empty = Skins/Classic. See ART.md.")]
    [SerializeField] private string skinFolder = "";
    [Tooltip("How the landing beam blends with the backdrop. Screen LIFTS a dark sky toward the tint (the default); Multiply DARKENS a bright/snow sky toward it, where Screen is invisible (white on white).")]
    [SerializeField] private PlacementBeamBlend placementBeamBlend = PlacementBeamBlend.Screen;
    [Tooltip("Beam tint override. Alpha 0 = derive it from the menu accent (pulled toward white). Alpha is otherwise ignored; strength is the mode default unless Placement Beam Strength is set.")]
    [SerializeField] private Color placementBeamColor = new Color(0f, 0f, 0f, 0f);
    [Tooltip("Beam strength override in the shader's LINEAR terms. 0 = the mode default (Screen 0.11, Multiply 0.25). Tiny values go a long way on dark skies.")]
    [SerializeField, Range(0f, 1f)] private float placementBeamStrength = 0f;

    [Header("Progression")]
    [Tooltip("Always playable regardless of campaign progress (testing/sandbox chapters).")]
    [SerializeField] private bool alwaysUnlocked = false;


    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public int ChapterNumber => Mathf.Max(1, chapterNumber);
    public int SortOrder => sortOrder;
    public IReadOnlyList<LevelDefinition> Levels => levels;
    public VideoClip MenuBackgroundVideo => menuBackgroundVideo;
    public Sprite MenuBackgroundImage => menuBackgroundImage;
    public Sprite NextChapterPreviewImage => nextChapterPreviewImage;
    public bool MenuTopIsLight => menuTopIsLight;
    public Color MenuAccentColor => menuAccentColor;
    public Color MenuAccentSecondaryColor => menuAccentSecondaryColor;
    public Color MenuPanelColor => menuPanelColor;
    public Color PlayButtonTopColor => playButtonTopColor;
    public Color PlayButtonBottomColor => playButtonBottomColor;
    public BackdropPreset Backdrop => backdrop;
    public PlacementBeamBlend PlacementBeamBlend => placementBeamBlend;
    /// <summary>True when the chapter overrides the beam tint (alpha &gt; 0); else derive from the accent.</summary>
    public bool HasPlacementBeamColor => placementBeamColor.a > 0f;
    public Color PlacementBeamColor => placementBeamColor;
    /// <summary>Beam strength override, or 0 for the blend mode's default.</summary>
    public float PlacementBeamStrength => placementBeamStrength;
    /// <summary>The chapter's soundtrack clips (looped as a whole), or empty.</summary>
    public IReadOnlyList<AudioClip> MusicPlaylist =>
        musicPlaylist ?? System.Array.Empty<AudioClip>();
    public string SkinFolder => string.IsNullOrWhiteSpace(skinFolder) ? "Skins/Classic" : skinFolder;
    public bool AlwaysUnlocked => alwaysUnlocked;

    /// <summary>The level after the given one within this chapter, or null if it was the last.</summary>
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

/// <summary>Photoshop-style layer mode of the landing beam (Resources/PlacementBeam.shader).</summary>
public enum PlacementBeamBlend
{
    /// <summary>Lifts a dark backdrop toward the tint. Invisible on bright skies.</summary>
    Screen = 0,
    /// <summary>Darkens a bright backdrop toward the tint. For snow/daylight chapters.</summary>
    Multiply = 1,
}
