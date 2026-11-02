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
        if (!handleLabel) return;
        bool english = ConfigManager.IsEnglish;
        handleLabel.fontSize = english ? 18 : originalSize;
        handleLabel.fontSizeMax = english ? 18 : originalMaximum;
        handleLabel.fontSizeMin = english ? 14 : originalMinimum;
        handleLabel.enableAutoSizing = english || originalAutoSize;
        handleLabel.margin = english ? new Vector4(4, 0, 4, 0) : originalMargin;
        handleLabel.textWrappingMode = english ? TMPro.TextWrappingModes.NoWrap : originalWrapping;
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
