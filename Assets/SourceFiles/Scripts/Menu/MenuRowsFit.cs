using UnityEngine;

/// <summary>Sizes an absolutely positioned settings column for scrolling, without moving rows.</summary>
public sealed class MenuRowsFit : MonoBehaviour
{
    private void Start()
    {
        var rect = (RectTransform)transform;
        float height = 0f;
        foreach (RectTransform child in rect)
            height = Mathf.Max(height, -child.anchoredPosition.y + child.rect.height);
        rect.sizeDelta = new Vector2(0f, height + 24f);
    }
}
