using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Chapter-aware styling for the in-game menus (pause, game over): the panels pick up the ACTIVE
/// chapter's menu palette (ChapterDefinition.MenuAccentColor etc.), so a jungle run pauses into a
/// jungle-green menu and a desert run into terracotta - the same treatment the main menu's play
/// screen uses. Falls back to the neutral kit colors when no chapter is resolvable (Custom Game).
/// </summary>
public static class GameMenuStyle
{
    public static ChapterDefinition ActiveChapter => Campaign.FindChapterOf(LevelSelectionState.SelectedLevel);

    public static Color Accent
    {
        get
        {
            ChapterDefinition chapter = ActiveChapter;
            Color c = chapter != null ? chapter.MenuAccentColor : RuntimeUiKit.TitleColor;
            c.a = 1f;
            return c;
        }
    }

    // Modern neutral chrome: the chapter colour lives in the ACCENTS (title, border, primary
    // button), never the surfaces - panels stay near-black translucent like the rest of the HUD.
    public static Color PanelColor => new Color(0.055f, 0.06f, 0.07f, 0.90f);
    public static Color BackdropColor => new Color(0f, 0f, 0f, 0.55f);
    public static Color BodyText => new Color(0.82f, 0.86f, 0.88f, 1f);

    /// <summary>Tint a kit panel to the chapter palette and give it a soft accent outline.</summary>
    public static void StylePanel(GameObject panel)
    {
        if (panel == null) return;
        Image image = panel.GetComponent<Image>();
        if (image != null) image.color = PanelColor;
        RuntimeUiKit.AddOutline(panel.transform, WithAlpha(Accent, 0.55f));
    }

    /// <summary>Style a kit button: primary = filled with the chapter accent; secondary = dark
    /// panel tone with accent-tinted text.</summary>
    public static void StyleButton(UnityEngine.UI.Button button, bool primary)
    {
        if (button == null) return;

        // The Button's ColorTint transition rewrites the target graphic's colour every frame,
        // so the fill must be expressed through the ColorBlock (image stays white).
        Color fill = primary
            ? WithAlpha(Color.Lerp(Accent, Color.white, 0.10f), 1f)
            : new Color(0.13f, 0.145f, 0.16f, 1f); // neutral dark; the accent lives in the text
        Image image = button.GetComponent<Image>();
        if (image != null) image.color = Color.white;
        ColorBlock colors = button.colors;
        colors.normalColor = fill;
        colors.highlightedColor = Color.Lerp(fill, Color.white, 0.08f);
        colors.pressedColor = Color.Lerp(fill, Color.black, 0.18f);
        colors.selectedColor = fill;
        colors.disabledColor = WithAlpha(fill, 0.4f);
        button.colors = colors;

        Text label = button.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.color = primary
                ? Color.Lerp(Accent, Color.black, 0.82f)
                : Color.Lerp(Accent, Color.white, 0.45f);
            label.fontStyle = FontStyle.Bold;
        }
    }

    private static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }
}
