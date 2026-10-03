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

    private void Awake() {
        handle.onClick.AddListener(Toggle);
    }

    private void OnEnable() {
        IsExpanded = startExpanded;
        RefreshState();
        SetPosition(TargetX);
        content.interactable = IsExpanded;
    }

    private void OnDisable() {
        StopSlide();
    }

    private void OnDestroy() {
        if (handle != null) handle.onClick.RemoveListener(Toggle);
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
