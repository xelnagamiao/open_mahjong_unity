using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>场景设置的中心盘皮肤选择页。场景保存 UI 引用，配置只保存稳定样式 ID。</summary>
public sealed class CenterDisplayConfigPanel : MonoBehaviour
{
    [Serializable]
    public sealed class StyleOption
    {
        public string id;
        public Button button;
        public RawImage preview;
        public TMP_Text nameText;
        public GameObject selectedBadge;
        public GameObject fallback;
    }

    [SerializeField] private StyleOption[] options = Array.Empty<StyleOption>();
    [SerializeField] private TMP_Text statusText;

    private UnityAction[] clickHandlers;
    private ConfigManager subscribedConfig;
    private ScrollRect styleScroll;

    private void Awake()
    {
        PrepareMinimalLayout();
        EnsureCatalogOptions();
        EnsureScrollableOptions();
        clickHandlers = new UnityAction[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            StyleOption option = options[i];
            if (option == null || option.button == null) continue;
            string id = option.id;
            clickHandlers[i] = () => SelectStyle(id);
            option.button.onClick.AddListener(clickHandlers[i]);
        }
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
        foreach (CenterDisplayStyles.Entry entry in CenterDisplayStyles.All)
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

    private static bool CompleteOption(StyleOption option) => option != null && option.button && option.preview
        && option.nameText && option.selectedBadge && option.fallback;

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

    private void ScrollToSelected()
    {
        if (!styleScroll || !styleScroll.content || !isActiveAndEnabled) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(styleScroll.content);
        ConfigManager config = ConfigManager.Instance;
        string selected = config ? config.SelectedCenterDisplayId : CenterDisplayStyles.Classic;
        StyleOption option = Array.Find(options, o => o != null && o.id == selected && o.button);
        if (option == null) return;
        RectTransform card = (RectTransform)option.button.transform;
        float height = styleScroll.viewport.rect.height;
        float range = Mathf.Max(0f, styleScroll.content.rect.height - height);
        if (range <= .1f) return;
        float top = -card.anchoredPosition.y;
        float visibleTop = (1f - styleScroll.verticalNormalizedPosition) * range;
        float next = visibleTop;
        if (top < visibleTop) next = top;
        else if (top + card.rect.height > visibleTop + height) next = top + card.rect.height - height;
        styleScroll.StopMovement();
        styleScroll.verticalNormalizedPosition = 1f - Mathf.Clamp(next, 0f, range) / range;
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

    private void OnEnable()
    {
        SubscribeToConfig();
        ReloadSaved();
        ScrollToSelected();
    }

    // Also covers the first frame when the config singleton's Awake ran after this panel's OnEnable.
    private void Start()
    {
        SubscribeToConfig();
        ReloadSaved();
        ScrollToSelected();
    }

    private void OnDisable()
    {
        UnsubscribeFromConfig();
    }

    private void OnDestroy()
    {
        UnsubscribeFromConfig();
        if (clickHandlers == null) return;
        for (int i = 0; i < options.Length; i++)
            if (options[i] != null && options[i].button != null && clickHandlers[i] != null)
                options[i].button.onClick.RemoveListener(clickHandlers[i]);
    }

    public void ShowPanel()
    {
        gameObject.SetActive(true);
        SubscribeToConfig();
        ReloadSaved();
        ScrollToSelected();
    }

    public void HidePanel()
    {
        gameObject.SetActive(false);
    }

    public void SelectStyle(string id)
    {
        ConfigManager config = ConfigManager.Instance;
        if (config == null) return;
        config.SetSelectedCenterDisplay(CenterDisplayStyles.Normalize(id));
        ReloadSaved();
    }

    public void ReloadSaved()
    {
        ConfigManager config = ConfigManager.Instance;
        string selectedId = config != null ? config.SelectedCenterDisplayId : "classic";
        selectedId = CenterDisplayStyles.Normalize(selectedId);
        foreach (StyleOption option in options)
        {
            if (option == null) continue;
            var entry = CenterDisplayStyles.Get(option.id);
            if (option.nameText != null) option.nameText.text = entry.Name;
            Texture2D texture = CenterDisplayStyles.GetPreview(entry.Id);
            if (option.preview != null)
            {
                option.preview.texture = texture;
                option.preview.color = Color.white;
                option.preview.gameObject.SetActive(texture != null);
            }
            if (option.fallback != null) option.fallback.SetActive(texture == null);
            bool selected = entry.Id == selectedId;
            if (option.button != null)
            {
                option.button.transition = Selectable.Transition.None;
                Image border = option.button.GetComponent<Image>();
                if (border != null)
                    border.color = selected ? SceneConfigUi.SelectedOrange : SceneConfigUi.UnselectedBlueGray;
            }
            if (option.selectedBadge != null) option.selectedBadge.SetActive(selected);
        }
    }

    private void SubscribeToConfig()
    {
        ConfigManager config = ConfigManager.Instance;
        if (config == subscribedConfig) return;
        UnsubscribeFromConfig();
        subscribedConfig = config;
        if (subscribedConfig != null) subscribedConfig.CenterDisplayChanged += OnStyleChanged;
    }

    private void UnsubscribeFromConfig()
    {
        if (subscribedConfig != null) subscribedConfig.CenterDisplayChanged -= OnStyleChanged;
        subscribedConfig = null;
    }

    private void OnStyleChanged(string id)
    {
        ReloadSaved();
        ScrollToSelected();
    }
}
