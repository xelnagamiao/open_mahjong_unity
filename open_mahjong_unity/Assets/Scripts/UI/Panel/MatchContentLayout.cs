using UnityEngine;

/// <summary>Fits the four-column card grid without changing its parent page or the card aspect ratio.</summary>
[ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(400)]
public sealed class MatchContentLayout : MonoBehaviour
{
    public static readonly Vector2 DesignSize = new Vector2(1739.879f, 594.187f);
    RectTransform rect;
    Vector2 previousSize;

    void OnEnable() => Apply(true);
    void LateUpdate() => Apply();

    public void Apply(bool force = false)
    {
        if (!rect) rect = (RectTransform)transform;
        if (!(rect.parent is RectTransform parent)) return;
        Vector2 size = parent.rect.size;
        if (!force && size == previousSize) return;
        if (size.x <= 0 || size.y <= 0) return;
        previousSize = size;
        float scale = Mathf.Min(size.x / DesignSize.x, size.y / DesignSize.y);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = DesignSize;
        rect.localScale = new Vector3(scale, scale, 1);
    }
}
