using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

public static partial class MainMenuRuntime
{
    // Shared gallery geometry keeps the atlas and collection scrolling consistently.
    private static ScrollRect BuildGalleryScroll(Transform parent, string name,
        ChapterDefinition chapter, float topInset, float bottomInset)
    {
        RectTransform viewport = CreateRect(parent, name + "Viewport",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(0f, bottomInset);
        viewport.offsetMax = new Vector2(0f, -topInset);
        Image viewportHit = viewport.gameObject.AddComponent<Image>();
        viewportHit.color = Color.clear;
        viewportHit.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>().padding = new Vector4(0f, -12f, 0f, -12f);

        RectTransform content = CreateRect(viewport, name + "Content",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, Vector2.zero);
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 0f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.gameObject.AddComponent<DirectionalScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 34f;

        RectTransform sbar = CreateRect(parent, name + "Scrollbar",
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);
        sbar.offsetMin = new Vector2(-42f, bottomInset + 12f);
        sbar.offsetMax = new Vector2(-34f, -(topInset + 12f));
        Image track = sbar.gameObject.AddComponent<Image>();
        track.sprite = RuntimeSprites.RoundedPanel();
        track.type = Image.Type.Sliced;
        track.pixelsPerUnitMultiplier = 6f;
        track.color = WithAlpha(TextPrimary, 0.10f);
        track.raycastTarget = false;
        Scrollbar scrollbar = sbar.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        RectTransform handle = CreateRect(sbar, "Handle",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.sprite = RuntimeSprites.RoundedPanel();
        handleImage.type = Image.Type.Sliced;
        handleImage.pixelsPerUnitMultiplier = 6f;
        handleImage.color = WithAlpha(ChapterLight(chapter), 0.55f);
        handleImage.raycastTarget = false;
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        return scroll;
    }

    private static RectTransform NewGridRow(Transform content, float height)
    {
        RectTransform row = CreateRect(content, "Row",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            Vector2.zero, new Vector2(0f, height));
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return row;
    }

}
