using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Restore a gallery only after its content has been laid out, even while paused.</summary>
public sealed class GalleryScrollRestore : MonoBehaviour
{
    public ScrollRect Scroll;
    public float Position = 1f;
    public bool IsRestored { get; private set; }

    private IEnumerator Start()
    {
        yield return null;
        Restore();
    }

    private void Restore()
    {
        if (IsRestored || Scroll == null || Scroll.content == null || Scroll.viewport == null) return;
        Canvas.ForceUpdateCanvases();
        Scroll.StopMovement();
        Scroll.verticalNormalizedPosition = Mathf.Clamp01(Position);
        IsRestored = true;
    }
}
