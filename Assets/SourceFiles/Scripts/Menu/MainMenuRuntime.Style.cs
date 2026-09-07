using UnityEngine;
using UnityEngine.UI;
using static RuntimeUiKit;

// Shared menu presentation. State, store callbacks and navigation remain with each builder.
public static partial class MainMenuRuntime
{
    // A separator, never a closed frame. Returns an Image because unlock/selection code
    // animates the same marker's colour. Ignore layout so it never shifts a content row.
    private static Image MenuRule(Transform parent, Color color)
    {
        var image = CreateImage(parent, "Rule", RuntimeSprites.Square(), WithAlpha(color, color.a * .4f));
        var rect = image.rectTransform;
        rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(.5f, 0); rect.sizeDelta = new Vector2(-32, 1.5f);
        image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return image;
    }

    private static void StyleMenuAction(Image image, bool primary, Color accent)
    {
        if (image == null) return;
        image.sprite = RuntimeSprites.RoundedPanel(); image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 3f;
        image.color = primary ? Color.Lerp(accent, Color.white, .78f) : new Color(.11f,.11f,.12f,1);
        foreach (var label in image.GetComponentsInChildren<TMPro.TextMeshProUGUI>())
        {
            label.color = primary ? new Color(.13f,.13f,.14f,1) : TextPrimary;
            label.font = RuntimeUiKit.TmpTitleFont;
            label.fontStyle = TMPro.FontStyles.Normal;
            label.characterSpacing = 1f;
        }
    }
}
