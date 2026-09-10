using UnityEngine;
using UnityEngine.UI;

/// <summary>Background-led chapter illustrations. Decorative only: never participate in input or layout.</summary>
public static class ChapterArtwork
{
    public static ChapterDefinition ActiveChapter => Campaign.FindChapterOf(LevelSelectionState.SelectedLevel);

    public static Sprite Load(ChapterDefinition chapter)
    {
        if (chapter == null) return null;
        string folder = chapter.SkinFolder;
        string key = folder.Substring(folder.LastIndexOf('/') + 1);
        // No permanent cache: unloaded chapter pages must not pin all fifteen textures in memory.
        return Resources.Load<Sprite>("ChapterArt/" + key);
    }

    public static Image Place(Transform parent, ChapterDefinition chapter, string name,
        Vector2 anchor, Vector2 pivot, Vector2 position, float size, float opacity = 1f)
    {
        Sprite sprite = Load(chapter);
        return PlaceSprite(parent, sprite, name, anchor, pivot, position, size, opacity);
    }

    private static Image PlaceSprite(Transform parent, Sprite sprite, string name,
        Vector2 anchor, Vector2 pivot, Vector2 position, float size, float opacity)
    {
        if (sprite == null) return null;
        var rect = RuntimeUiKit.CreateRect(parent, name, anchor, anchor, pivot, position, Vector2.one * size);
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = new Color(1f, 1f, 1f, opacity);
        return image;
    }
}
