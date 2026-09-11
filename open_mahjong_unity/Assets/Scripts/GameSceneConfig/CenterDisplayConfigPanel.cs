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

    // Existing scenes keep their authored cards. New catalog entries reuse a
    // complete image card without copying another style's generated preview.
    private void EnsureCatalogOptions()
    {
        StyleOption template = Array.Find(options, o => CompleteOption(o) && o.id == CenterDisplayStyles.Classic)
            ?? Array.Find(options, o => CompleteOption(o) && o.id != CenterDisplayStyles.SimpleNavy);
        foreach (CenterDisplayStyles.Entry entry in CenterDisplayStyles.All)
        {
            if (Array.Exists(options, o => o != null && o.id == entry.Id)) continue;
            if (template == null) break;
            Transform source = template.button.transform;
            Button button = Instantiate(template.button, source.parent, false);
            button.name = "Style_" + entry.Id;
            button.onClick = new Button.ButtonClickedEvent();
            var option = new StyleOption {
                id = entry.Id, button = button,
                preview = CloneChild(template.preview.transform, source, button.transform).GetComponent<RawImage>(),
                nameText = CloneChild(template.nameText.transform, source, button.transform).GetComponent<TMP_Text>(),
                selectedBadge = CloneChild(template.selectedBadge.transform, source, button.transform).gameObject,
                fallback = CloneChild(template.fallback.transform, source, button.transform).gameObject
            };
            option.nameText.text = entry.Name;
            if (option.fallback.TryGetComponent<TMP_Text>(out var fallbackLabel))
                fallbackLabel.text = "预览暂未载入";
            Array.Resize(ref options, options.Length + 1);
            options[options.Length - 1] = option;
        }
        StyleOption navy = Array.Find(options, o => o != null && o.id == CenterDisplayStyles.SimpleNavy);
        if (navy != null) BuildSimpleNavyPreview(navy);
    }

    private static bool CompleteOption(StyleOption option) => option != null && option.button && option.preview
        && option.nameText && option.selectedBadge && option.fallback;

    // Keep the page, header, footer and card dimensions exactly as authored.
    // Only overflowing rows become scrollable inside the existing grid area.
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

    private static void BuildSimpleNavyPreview(StyleOption option)
    {
        if (!option.fallback || !option.nameText || option.fallback.transform.Find("SimpleNavyPreview")) return;
        // This miniature exists only on the settings card. The actual skin
        // never creates text or replaces game-owned score/round/wind values.
        var fallbackGraphic = option.fallback.GetComponent<Graphic>();
        if (fallbackGraphic) fallbackGraphic.enabled = false;
        RectTransform root = PreviewRect(option.fallback.transform, "SimpleNavyPreview", Vector2.zero, new Vector2(77, 77));
        root.localScale = Vector3.one * 2.65f;
        PreviewFace(root, "Rim", new Vector2(77, 77), new Color(.13f, .20f, .25f));
        PreviewFace(root, "Panel", new Vector2(71, 71), new Color(.045f, .09f, .12f));
        PreviewFace(root, "Readout", new Vector2(34, 27), new Color(.025f, .06f, .078f));
        PreviewText(root, option.nameText.font, "東1局", new Vector2(0, 5), new Vector2(31, 11), 7.4f, new Color(.5f, .92f, .83f));
        PreviewText(root, option.nameText.font, "余:48", new Vector2(0, -7), new Vector2(31, 11), 5.2f, new Color(.56f, .68f, .72f));
        string[] winds = { "東", "南", "西", "北" };
        for (int seat = 0; seat < 4; seat++)
        {
            RectTransform cluster = PreviewRect(root, "Seat" + seat, Vector2.zero, new Vector2(77, 77));
            cluster.localRotation = Quaternion.Euler(0, 0, seat * 90);
            PreviewText(cluster, option.nameText.font, "26800", new Vector2(0, -27), new Vector2(39, 11), 6.5f, new Color(.94f, .77f, .43f));
            PreviewText(cluster, option.nameText.font, winds[seat], new Vector2(-29, -28), new Vector2(9, 11), 6.5f, new Color(.89f, .94f, .94f));
        }
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

    private static void PreviewFace(Transform parent, string name, Vector2 size, Color color)
    {
        var graphic = PreviewRect(parent, name, Vector2.zero, size).gameObject.AddComponent<CenterSkinGraphic>();
        graphic.SetShape(CenterSkinGraphic.CutRect(size.x, size.y, .12f), color, Color.clear, 0);
    }

    private static void PreviewText(Transform parent, TMP_FontAsset font, string value, Vector2 position, Vector2 size, float fontSize, Color color)
    {
        var text = PreviewRect(parent, "Preview " + value, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = fontSize; text.color = color;
        text.fontStyle = FontStyles.Bold; text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.raycastTarget = false;
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
        if (statusText != null)
            statusText.text = "当前：" + CenterDisplayStyles.Get(selectedId).Name + "  ·  选择后自动保存";
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
