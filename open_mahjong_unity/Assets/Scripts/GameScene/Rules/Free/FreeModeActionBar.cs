using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>自由模式专用单排动作栏；退出时恢复场景容器的原始布局。</summary>
public sealed class FreeModeActionBar : MonoBehaviour {
    private RectTransform rect;
    private GridLayoutGroup grid;
    private bool gridWasEnabled, captured;
    private Vector2 originalMin, originalMax, originalPivot, originalPosition, originalSize;
    private readonly List<ActionButton> buttons = new List<ActionButton>();
    private const float ButtonWidth = 86f, ButtonHeight = 64f, Spacing = 8f, GroupGap = 24f;

    private void OnEnable() {
        rect = (RectTransform)transform;
        grid = GetComponent<GridLayoutGroup>();
        originalMin = rect.anchorMin;
        originalMax = rect.anchorMax;
        originalPivot = rect.pivot;
        originalPosition = rect.anchoredPosition;
        originalSize = rect.sizeDelta;
        captured = true;
        if (grid != null) { gridWasEnabled = grid.enabled; grid.enabled = false; }
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-48f, 250f);
    }

    private void LateUpdate() {
        buttons.Clear();
        foreach (Transform child in transform) {
            ActionButton button = child.GetComponent<ActionButton>();
            if (child.gameObject.activeSelf && button != null) buttons.Add(button);
        }
        if (buttons.Count == 0) return;
        float width = buttons.Count * ButtonWidth + (buttons.Count - 1) * Spacing + (buttons.Count > 3 ? GroupGap : 0f);
        float available = rect.parent is RectTransform parent ? Mathf.Max(1f, parent.rect.width - 96f) : width;
        float scale = Mathf.Min(1f, available / width);
        rect.sizeDelta = new Vector2(width * scale, ButtonHeight * scale);
        float x = 0f;
        for (int i = 0; i < buttons.Count; i++) {
            if (i == 3) x += GroupGap;
            RectTransform cell = (RectTransform)buttons[i].transform;
            cell.anchorMin = cell.anchorMax = Vector2.zero;
            cell.pivot = Vector2.zero;
            cell.anchoredPosition = new Vector2(x * scale, 0f);
            cell.sizeDelta = new Vector2(ButtonWidth * scale, ButtonHeight * scale);
            cell.localScale = Vector3.one;
            if (buttons[i].TextObject != null) {
                buttons[i].TextObject.fontSizeMin = 18f * scale;
                buttons[i].TextObject.fontSizeMax = 28f * scale;
            }
            x += ButtonWidth + Spacing;
        }
    }

    private void OnDisable() {
        if (!captured || rect == null) return;
        rect.anchorMin = originalMin;
        rect.anchorMax = originalMax;
        rect.pivot = originalPivot;
        rect.anchoredPosition = originalPosition;
        rect.sizeDelta = originalSize;
        if (grid != null) grid.enabled = gridWasEnabled;
        captured = false;
    }
}
