using System.Collections;
using UnityEngine;

/// <summary>牌谱侧栏的横向收纳；保留原信息按钮与 RecordSetting 的行为。</summary>
public sealed class RecordSidebarDrawer : MonoBehaviour {
    [SerializeField] private RectTransform drawer;
    [SerializeField] private CanvasGroup content;
    [SerializeField] private UnityEngine.UI.Button handle;
    [SerializeField] private GameObject expandedIcon;
    [SerializeField] private GameObject collapsedLabel;
    [SerializeField] private GameObject collapsedIcon;
    [SerializeField] private bool startExpanded;
    [SerializeField] private float slideDuration = 0.16f;

    public bool IsExpanded { get; private set; }
    private Coroutine slide;
    private TMPro.TMP_Text handleLabel;
    private float originalSize, originalMinimum, originalMaximum;
    private bool originalAutoSize;
    private Vector4 originalMargin;
    private TMPro.TextWrappingModes originalWrapping;
    private Vector2 originalLabelSize, originalLabelSizeDelta, originalLabelPosition, originalLabelPivot;
    private Quaternion originalLabelRotation;
    private bool languageLayoutPending;

    private void Awake() {
        handle.onClick.AddListener(Toggle);
        handleLabel = collapsedLabel.GetComponent<TMPro.TMP_Text>();
        if (handleLabel) {
            var binding = handleLabel.GetComponent<LocalizedPlatformLabel>();
            originalSize = binding ? binding.authoredSize : handleLabel.fontSize;
            originalMinimum = handleLabel.fontSizeMin;
            originalMaximum = handleLabel.fontSizeMax;
            originalAutoSize = binding ? binding.authoredAutoSize : handleLabel.enableAutoSizing;
            originalMargin = handleLabel.margin;
            originalWrapping = handleLabel.textWrappingMode;
            var rect = handleLabel.rectTransform;
            originalLabelSize = rect.rect.size;
            originalLabelSizeDelta = rect.sizeDelta;
            originalLabelPosition = rect.anchoredPosition;
            originalLabelPivot = rect.pivot;
            originalLabelRotation = rect.localRotation;
            if (binding) binding.fitTranslation = false;
        }
    }

    private void OnEnable() {
        ConfigManager.OnLanguageChanged += RefreshLanguage;
        RefreshLanguage();
        IsExpanded = startExpanded;
        RefreshState();
        SetPosition(TargetX);
        content.interactable = IsExpanded;
    }

    private void OnDisable() {
        ConfigManager.OnLanguageChanged -= RefreshLanguage;
        StopSlide();
    }

    private void OnDestroy() {
        if (handle != null) handle.onClick.RemoveListener(Toggle);
    }

    private void RefreshLanguage() {
        ApplyLanguageLayout();
        // Run once after localization callbacks have restored their own typography state.
        languageLayoutPending = true;
    }

    private void LateUpdate() {
        if (!languageLayoutPending) return;
        languageLayoutPending = false;
        ApplyLanguageLayout();
    }

    private void ApplyLanguageLayout() {
        if (!handleLabel) return;
        bool additional = PlatformText.IsAdditionalLanguage(ConfigManager.CurrentLanguage);
        bool english = ConfigManager.IsEnglish || additional;
        handleLabel.fontSize = english ? 18 : originalSize;
        handleLabel.fontSizeMax = english ? 18 : originalMaximum;
        handleLabel.fontSizeMin = additional ? 12 : english ? 14 : originalMinimum;
        handleLabel.enableAutoSizing = english || originalAutoSize;
        handleLabel.margin = additional ? new Vector4(2, 0, 2, 0)
            : english ? new Vector4(4, 0, 4, 0) : originalMargin;
        handleLabel.textWrappingMode = english ? TMPro.TextWrappingModes.NoWrap : originalWrapping;
        var rect = handleLabel.rectTransform;
        if (additional) {
            // Rotate only the label within its original bounds; the handle's hit area stays unchanged.
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = originalLabelPosition + new Vector2(
                (.5f - originalLabelPivot.x) * originalLabelSize.x,
                (.5f - originalLabelPivot.y) * originalLabelSize.y);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, originalLabelSize.y);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, originalLabelSize.x);
            rect.localRotation = originalLabelRotation * Quaternion.Euler(0, 0, 90);
        } else {
            rect.pivot = originalLabelPivot;
            rect.anchoredPosition = originalLabelPosition;
            rect.sizeDelta = originalLabelSizeDelta;
            rect.localRotation = originalLabelRotation;
        }
    }

    private float TargetX => IsExpanded ? 0f : -drawer.rect.width;

    public void Toggle() {
        SetExpanded(!IsExpanded);
    }

    public void SetExpanded(bool expanded) {
        StopSlide();
        IsExpanded = expanded;
        RefreshState();
        if (isActiveAndEnabled && slideDuration > 0f) {
            slide = StartCoroutine(SlideTo(TargetX));
        } else {
            SetPosition(TargetX);
            content.interactable = IsExpanded;
        }
    }

    private void RefreshState() {
        expandedIcon.SetActive(IsExpanded);
        collapsedLabel.SetActive(!IsExpanded);
        collapsedIcon.SetActive(!IsExpanded);
        content.interactable = false;
        content.blocksRaycasts = IsExpanded;
    }

    private IEnumerator SlideTo(float target) {
        float from = drawer.anchoredPosition.x;
        float elapsed = 0f;
        while (elapsed < slideDuration) {
            elapsed += Time.unscaledDeltaTime;
            SetPosition(Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, elapsed / slideDuration)));
            yield return null;
        }
        SetPosition(target);
        content.interactable = IsExpanded;
        slide = null;
    }

    private void SetPosition(float x) {
        Vector2 position = drawer.anchoredPosition;
        position.x = x;
        drawer.anchoredPosition = position;
    }

    private void StopSlide() {
        if (slide == null) return;
        StopCoroutine(slide);
        slide = null;
    }
}
