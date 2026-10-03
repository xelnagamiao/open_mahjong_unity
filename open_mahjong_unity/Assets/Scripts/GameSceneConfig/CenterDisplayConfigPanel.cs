using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>场景设置的中心盘皮肤选择页。场景保存 UI 引用，配置只保存稳定样式 ID。</summary>
public sealed partial class CenterDisplayConfigPanel : MonoBehaviour
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
    [SerializeField] private ScrollRect styleScroll;
    [SerializeField] private bool fixedUiBaked;
    public bool HasBakedUi => fixedUiBaked && options != null && options.Length == CenterDisplayStyles.All.Count
        && Array.TrueForAll(options, CompleteOption) && HasCurrentDisplayOrder();

    private bool HasCurrentDisplayOrder()
    {
        int index = 0;
        foreach (var entry in CenterDisplayStyles.DisplayOrder)
            if (options[index++].id != entry.Id) return false;
        return true;
    }

    private void Awake()
    {
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

    private static bool CompleteOption(StyleOption option) => option != null && option.button && option.preview
        && option.nameText && option.selectedBadge && option.fallback;

    private void ScrollToSelected()
    {
        if (!styleScroll || !styleScroll.content || !isActiveAndEnabled) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(styleScroll.content);
        ConfigManager config = ConfigManager.Instance;
        string selected = config ? config.SelectedCenterDisplayId : CenterDisplayStyles.Default;
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
        string selectedId = config != null ? config.SelectedCenterDisplayId : CenterDisplayStyles.Default;
        selectedId = CenterDisplayStyles.Normalize(selectedId);
        foreach (StyleOption option in options)
        {
            if (option == null) continue;
            var entry = CenterDisplayStyles.Get(option.id);
            if (option.nameText != null)
            {
                option.nameText.text = entry.Name;
                option.nameText.gameObject.SetActive(false);
            }
            Texture2D texture = option.preview != null ? option.preview.texture as Texture2D : null;
            if (option.preview != null)
            {
                option.preview.texture = texture;
                option.preview.color = Color.white;
                option.preview.gameObject.SetActive(texture != null);
            }
            if (option.fallback != null) option.fallback.SetActive(texture == null);
            bool selected = entry.Id == selectedId;
            if (option.button != null)
                SceneConfigUi.SetButtonSelected(option.button, selected, SceneConfigUi.SelectedOrange, SceneConfigUi.UnselectedBlueGray);
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
