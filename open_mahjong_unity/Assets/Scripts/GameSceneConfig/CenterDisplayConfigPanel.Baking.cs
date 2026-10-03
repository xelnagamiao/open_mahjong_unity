#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Editor-only authoring; runtime controls bind the serialized scene objects.
public sealed partial class CenterDisplayConfigPanel
{
    public void BakeLayout()
    {
        if (HasBakedUi) return;
        PrepareMinimalLayout(); EnsureCatalogOptions(); EnsureScrollableOptions();
        fixedUiBaked = true;
    }
    private void PrepareMinimalLayout()
    {
        foreach (string name in new[] { "Description", "StatusText", "HelpText" })
        {
            var annotation = transform.Find(name);
            if (annotation == null) continue;
            if (annotation.TryGetComponent<TMP_Text>(out var text)) text.text = "";
            annotation.gameObject.SetActive(false);
        }
        if (statusText != null) { statusText.text = ""; statusText.gameObject.SetActive(false); }
        // Use the space formerly reserved for explanation text, keeping card dimensions intact.
        var grid = transform.Find("StyleGrid") as RectTransform;
        if (grid == null) return;
        grid.anchorMin = grid.anchorMax = grid.pivot = new Vector2(0, 1);
        grid.anchoredPosition = new Vector2(30, -88);
        grid.sizeDelta = new Vector2(((RectTransform)transform).rect.width - 60,
            Mathf.Max(1, ((RectTransform)transform).rect.height - 112));
    }

    // Reconcile only this panel's registered cards. Retired cards remain in the
    // hierarchy for recovery, but inactive cards no longer occupy grid cells.
    private void EnsureCatalogOptions()
    {
        StyleOption[] existing = options ?? Array.Empty<StyleOption>();
        var catalogIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (CenterDisplayStyles.Entry entry in CenterDisplayStyles.All) catalogIds.Add(entry.Id);
        StyleOption template = Array.Find(existing, o => CompleteOption(o) && o.id == CenterDisplayStyles.Classic)
            ?? Array.Find(existing, o => CompleteOption(o) && catalogIds.Contains(o.id))
            ?? Array.Find(existing, CompleteOption);
        var ordered = new List<StyleOption>();
        var retainedButtons = new HashSet<Button>();
        foreach (CenterDisplayStyles.Entry entry in CenterDisplayStyles.DisplayOrder)
        {
            StyleOption option = Array.Find(existing,
                o => CompleteOption(o) && o.id == entry.Id && !retainedButtons.Contains(o.button));
            if (option == null)
            {
                if (template == null) continue;
                Transform source = template.button.transform;
                Button button = Instantiate(template.button, source.parent, false);
                button.name = "Style_" + entry.Id;
                button.onClick = new Button.ButtonClickedEvent();
                option = new StyleOption {
                    id = entry.Id, button = button,
                    preview = CloneChild(template.preview.transform, source, button.transform).GetComponent<RawImage>(),
                    nameText = CloneChild(template.nameText.transform, source, button.transform).GetComponent<TMP_Text>(),
                    selectedBadge = CloneChild(template.selectedBadge.transform, source, button.transform).gameObject,
                    fallback = CloneChild(template.fallback.transform, source, button.transform).gameObject
                };
                if (option.fallback.TryGetComponent<TMP_Text>(out var fallbackLabel))
                    fallbackLabel.text = "预览暂未载入";
            }
            option.nameText.text = entry.Name;
            option.nameText.gameObject.SetActive(false);
            option.preview.texture = CenterDisplayStyles.GetPreview(entry.Id);
            ordered.Add(option);
            retainedButtons.Add(option.button);
        }
        // This also hides duplicate or incomplete registered cards that were
        // replaced above. Never scan or hide unrelated UI children by name.
        foreach (StyleOption option in existing)
            if (option != null && option.button && !retainedButtons.Contains(option.button))
                option.button.gameObject.SetActive(false);
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].button.gameObject.SetActive(true);
            ordered[i].button.transform.SetSiblingIndex(i);
        }
        options = ordered.ToArray();
    }

    // Keep card dimensions; only overflowing rows become scrollable inside the gallery area.
    private void EnsureScrollableOptions()
    {
        StyleOption first = Array.Find(options, CompleteOption);
        if (first == null) return;
        RectTransform content = first.button.transform.parent as RectTransform;
        GridLayoutGroup grid = content ? content.GetComponent<GridLayoutGroup>() : null;
        if (!grid || grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount) return;
        int rows = Mathf.CeilToInt(options.Length / (float)Mathf.Max(1, grid.constraintCount));
        float contentHeight = grid.padding.vertical + rows * grid.cellSize.y + Mathf.Max(0, rows - 1) * grid.spacing.y;
        styleScroll = content.parent.GetComponent<ScrollRect>();
        float viewportHeight = styleScroll ? styleScroll.viewport.rect.height : content.rect.height;
        if (contentHeight <= viewportHeight + .1f && !styleScroll) return;

        if (!styleScroll)
        {
            var viewportObject = new GameObject("StyleScroll", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            viewportObject.layer = content.gameObject.layer;
            RectTransform viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(content.parent, false);
            viewport.SetSiblingIndex(content.GetSiblingIndex());
            viewport.anchorMin = content.anchorMin; viewport.anchorMax = content.anchorMax;
            viewport.pivot = content.pivot; viewport.sizeDelta = content.sizeDelta;
            viewport.anchoredPosition3D = content.anchoredPosition3D;
            viewport.localScale = content.localScale; viewport.localRotation = content.localRotation;
            Image hitArea = viewportObject.GetComponent<Image>();
            hitArea.color = Color.clear; hitArea.raycastTarget = true;

            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f); content.anchoredPosition = Vector2.zero;
            content.localScale = Vector3.one; content.localRotation = Quaternion.identity;
            content.sizeDelta = new Vector2(0f, contentHeight);

            styleScroll = viewportObject.GetComponent<ScrollRect>();
            styleScroll.viewport = viewport; styleScroll.content = content;
            styleScroll.horizontal = false; styleScroll.vertical = true;
            styleScroll.movementType = ScrollRect.MovementType.Clamped;
            styleScroll.scrollSensitivity = 45f;
            styleScroll.verticalScrollbar = MakeStyleScrollbar(viewport);
            styleScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(contentHeight, viewportHeight));
    }

    private static Scrollbar MakeStyleScrollbar(RectTransform viewport)
    {
        var go = new GameObject("StyleScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        go.layer = viewport.gameObject.layer;
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(viewport.parent, false);
        rect.anchorMin = viewport.anchorMin; rect.anchorMax = viewport.anchorMax;
        rect.pivot = new Vector2(0f, viewport.pivot.y);
        rect.sizeDelta = new Vector2(10f, viewport.rect.height);
        rect.anchoredPosition = viewport.anchoredPosition + new Vector2(viewport.rect.width * (1f - viewport.pivot.x) + 6f, 0f);
        go.GetComponent<Image>().color = new Color(.14f, .18f, .24f, 1f);
        RectTransform handle = PreviewRect(rect, "Handle", Vector2.zero, Vector2.zero);
        handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one;
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(.48f, .57f, .68f, 1f);
        Scrollbar scrollbar = go.GetComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
        return scrollbar;
    }

    private static Transform CloneChild(Transform child, Transform source, Transform clone)
    {
        var path = new Stack<int>();
        for (Transform current = child; current != source; current = current.parent)
            path.Push(current.GetSiblingIndex());
        while (path.Count > 0) clone = clone.GetChild(path.Pop());
        return clone;
    }

    private static RectTransform PreviewRect(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }
}
#endif
