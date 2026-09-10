using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Open the atlas near the current chapter once its scroll layout is measured.</summary>
public sealed class ChapterJourneyFocus : MonoBehaviour
{
    public ScrollRect Scroll;
    public int RowIndex;
    public float RowHeight;

    private IEnumerator Start()
    {
        yield return null;
        if (Scroll == null || Scroll.content == null || Scroll.viewport == null) yield break;
        Canvas.ForceUpdateCanvases();
        float viewportHeight = Scroll.viewport.rect.height;
        float overflow = Scroll.content.rect.height - viewportHeight;
        if (overflow <= 0f) yield break;
        float offset = Mathf.Max(0f, RowIndex * RowHeight - viewportHeight * .25f);
        Scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - offset / overflow);
    }
}
