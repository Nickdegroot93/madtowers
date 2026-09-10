using UnityEngine;
using UnityEngine.UI;

/// <summary>Isolated chapter fragments dress the panel edges while its content keeps the foreground.</summary>
public static class ChapterOrnaments
{
    public static Sprite Load(ChapterDefinition chapter)
    {
        if (chapter == null) return null;
        string folder = chapter.SkinFolder;
        return Resources.Load<Sprite>("ChapterArt/ornament_" + folder.Substring(folder.LastIndexOf('/') + 1));
    }

    public static RectTransform Dress(Transform panel, ChapterDefinition chapter)
    {
        // Idempotent so a panel can be restyled without stacking duplicate decoration.
        var existing = panel.Find("ChapterOrnaments") as RectTransform;
        Sprite sprite = Load(chapter);
        if (existing != null)
        {
            existing.gameObject.SetActive(sprite != null);
            foreach (var image in existing.GetComponentsInChildren<Image>(true))
            {
                image.sprite = sprite;
                if (sprite == null) continue;
                float offset = image.rectTransform.sizeDelta.x * CornerInset(sprite);
                image.rectTransform.anchoredPosition = new Vector2(image.name == "LowerRight" ? -offset : offset, offset);
            }
            return existing;
        }
        if (sprite == null) return null;
        var root = RuntimeUiKit.CreateRect(panel, "ChapterOrnaments", Vector2.zero, Vector2.one,
            new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
        root.SetAsFirstSibling();
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var group = root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false; group.interactable = false;
        // Use painted alpha coverage: these L-shaped sprites contain substantial empty space.
        // Their authored inset puts half the visible decoration inside the panel, half outside.
        float inset = CornerInset(sprite);
        Add(root, sprite, "LowerLeft", new Vector2(0f, 0f), Vector2.one * (280f * inset), 280f, .95f, false);
        Add(root, sprite, "LowerRight", new Vector2(1f, 0f), new Vector2(-1f, 1f) * (250f * inset), 250f, .85f, true);
        return root;
    }

    [System.Serializable] private sealed class LayoutEntry { public string key; public float inset; }
    [System.Serializable] private sealed class LayoutData { public LayoutEntry[] entries; }
    private static LayoutData _layout;

    private static float CornerInset(Sprite sprite)
    {
        if (_layout == null)
        {
            var data = Resources.Load<TextAsset>("ChapterArt/CornerLayout");
            _layout = data != null ? JsonUtility.FromJson<LayoutData>(data.text) : new LayoutData();
        }
        if (_layout.entries != null)
            foreach (var entry in _layout.entries)
                if (entry.key == sprite.name) return entry.inset;
        return .33f;
    }

    private static void Add(Transform parent, Sprite sprite, string name, Vector2 anchor,
        Vector2 position, float size, float opacity, bool mirror)
    {
        var rect = RuntimeUiKit.CreateRect(parent, name, anchor, anchor, new Vector2(.5f, .5f),
            position, Vector2.one * size);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
        image.color = new Color(1f, 1f, 1f, opacity);
        if (mirror) rect.localScale = new Vector3(-1f, 1f, 1f);
    }
}
