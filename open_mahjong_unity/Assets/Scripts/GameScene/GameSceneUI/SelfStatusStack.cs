using UnityEngine;

/// <summary>Dock statuses below the automatic-action sidebar in the current canvas coordinates.</summary>
public sealed class SelfStatusStack : MonoBehaviour {
    private RectTransform rect;
    private RectTransform parent;
    private RectTransform canvas;
    private readonly Vector3[] corners = new Vector3[4];

    private void Awake() {
        rect = (RectTransform)transform;
        parent = (RectTransform)rect.parent;
        canvas = (RectTransform)GetComponentInParent<Canvas>().rootCanvas.transform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0f, 1f);
        var layout = GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.spacing = 6f;
        layout.padding = new RectOffset();
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        rect.sizeDelta = new Vector2(440f, 180f);
    }

    private void LateUpdate() {
        RefreshPosition();
    }

    public void RefreshPosition() {
        // Replay hides the automatic-action sidebar; use the full viewport, not the short controls container.
        var fallback = canvas.TransformPoint(new Vector3(canvas.rect.xMin, canvas.rect.yMin + canvas.rect.height * 0.2f, 0f));
        float bottom = parent.InverseTransformPoint(fallback).y;
        var automatic = AutoAction.Instance;
        if (automatic != null && automatic.gameObject.activeInHierarchy) {
            automatic.SidebarFooter.GetWorldCorners(corners);
            bottom = float.PositiveInfinity;
            for (int i = 0; i < corners.Length; i++) bottom = Mathf.Min(bottom, parent.InverseTransformPoint(corners[i]).y);
        }
        var position = new Vector2(0f, bottom - parent.rect.yMin - 8f);
        if (rect.anchoredPosition != position) rect.anchoredPosition = position;
    }
}
