using System;
using System.Collections;
using TMPro;
using UnityEngine;

public partial class AutoAction {
    [Serializable]
    private sealed class SidebarRow {
        public RectTransform root;
        public TMP_Text label;
        public UnityEngine.UI.Image background;
        public UnityEngine.UI.Image indicator;
        public string compactName;
        public string fullName;
    }

    [Header("自动操作侧栏")]
    [SerializeField] private SidebarRow[] sidebarRows = Array.Empty<SidebarRow>();
    [SerializeField] private UnityEngine.UI.Button sidebarHandle;
    [SerializeField] private RectTransform sidebarArrow;
    [SerializeField] private float sidebarCompactWidth = 64f;
    [SerializeField] private float sidebarSlideDuration = 0.16f;

    private RectTransform sidebarRect;
    private UnityEngine.UI.VerticalLayoutGroup sidebarLayout;
    private float sidebarExpandedWidth;
    private Coroutine sidebarSlide;
    private RectTransform sidebarCanvas;
    private RectTransform sidebarPopup;
    private Vector2 sidebarPopupPosition;
    private Vector2 sidebarPopupCanvasSize;
    private UnityEngine.UI.Graphic[] sidebarPopupGraphics;
    private readonly Vector3[] sidebarPopupCorners = new Vector3[4];
    private bool sidebarPopupLayoutDirty;
    public bool IsSidebarExpanded { get; private set; }
    public RectTransform SidebarFooter => sidebarHandle != null
        ? sidebarHandle.transform as RectTransform : transform as RectTransform;

    private void InitializeSidebar() {
        sidebarRect = transform as RectTransform;
        sidebarLayout = GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        var canvas = GetComponentInParent<Canvas>(true);
        if (canvas != null) sidebarCanvas = canvas.rootCanvas.transform as RectTransform;
        if (mingPaiPanel != null) {
            sidebarPopup = mingPaiPanel.transform as RectTransform;
            sidebarPopupPosition = sidebarPopup.anchoredPosition;
            sidebarPopupGraphics = sidebarPopup.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
        }
        if (sidebarHandle != null) sidebarHandle.onClick.AddListener(ToggleSidebar);
    }

    private void OnRectTransformDimensionsChange() {
        sidebarPopupLayoutDirty = true;
    }

    private void LateUpdate() {
        if (sidebarPopup == null || !sidebarPopup.gameObject.activeInHierarchy) return;
        // 固定尺寸侧栏不一定收到 CanvasScaler 的尺寸回调；弹窗打开时也检查画布变化。
        if (sidebarPopupLayoutDirty || (sidebarCanvas != null && sidebarCanvas.rect.size != sidebarPopupCanvasSize)) {
            FitSidebarPopup();
        }
    }

    private void FitSidebarPopup() {
        sidebarPopupLayoutDirty = false;
        if (sidebarPopup == null || sidebarCanvas == null || !sidebarPopup.gameObject.activeInHierarchy) return;

        // 每次以正常停靠位置为基准，再按实际可见内容收进画布，避免缩放后累积偏移。
        Canvas.ForceUpdateCanvases();
        sidebarPopup.anchoredPosition = sidebarPopupPosition;
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var graphic in sidebarPopupGraphics) {
            if (graphic == null || !graphic.isActiveAndEnabled) continue;
            graphic.rectTransform.GetWorldCorners(sidebarPopupCorners);
            foreach (Vector3 corner in sidebarPopupCorners) {
                Vector2 point = sidebarCanvas.InverseTransformPoint(corner);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }
        if (float.IsPositiveInfinity(min.x)) return;
        Rect viewport = sidebarCanvas.rect;
        sidebarPopupCanvasSize = viewport.size;
        Vector2 offset = Vector2.zero;
        offset.x = Mathf.Min(0f, viewport.xMax - 8f - max.x);
        offset.y = Mathf.Min(0f, viewport.yMax - 8f - max.y);
        offset.x = Mathf.Max(offset.x, viewport.xMin + 8f - min.x);
        offset.y = Mathf.Max(offset.y, viewport.yMin + 8f - min.y);
        sidebarPopup.anchoredPosition += (Vector2)sidebarPopup.parent.InverseTransformVector(sidebarCanvas.TransformVector(offset));
        sidebarPopupLayoutDirty = false;
    }

    private void OnEnable() {
        ConfigManager.OnLanguageChanged += RefreshSidebarLanguage;
        RefreshExpandLabel();
        if (sidebarRect != null) RefreshSidebarLanguage();
    }

    private void RefreshExpandLabel() {
        if (!expandButtonText) return;
        TableText.Set(expandButtonText, TableText.CompactActionKey("展"));
        var binding = expandButtonText.GetComponent<LocalizedPlatformLabel>();
        if (binding) binding.fitTranslation = false;
        expandButtonText.fontSize = 40;
        expandButtonText.enableAutoSizing = false;
        expandButtonText.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void RefreshSidebarLanguage() {
        RefreshExpandLabel();
        if (sidebarRect == null || sidebarHandle == null) return;
        StopSidebarSlide();
        RefreshSidebarPresentation();
        SetSidebarWidth(IsSidebarExpanded ? sidebarExpandedWidth : sidebarCompactWidth);
        sidebarPopupLayoutDirty = true;
    }

    private void OnDisable() {
        ConfigManager.OnLanguageChanged -= RefreshSidebarLanguage;
        StopSidebarSlide();
        if (sidebarRect != null && sidebarHandle != null) {
            SetSidebarWidth(IsSidebarExpanded ? sidebarExpandedWidth : sidebarCompactWidth);
        }
        CloseMingPaiPanel();
    }

    private void OnDestroy() {
        if (sidebarHandle != null) sidebarHandle.onClick.RemoveListener(ToggleSidebar);
        if (Instance == this) Instance = null;
    }

    private void ResetSidebar() {
        if (sidebarHandle == null) return;
        StopSidebarSlide();
        IsSidebarExpanded = false;
        RefreshSidebarPresentation();
        SetSidebarWidth(sidebarCompactWidth);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(sidebarRect);
    }

    private void ToggleSidebar() {
        SetSidebarExpanded(!IsSidebarExpanded);
    }

    public void SetSidebarExpanded(bool expanded) {
        if (sidebarHandle == null) return;
        StopSidebarSlide();
        IsSidebarExpanded = expanded;
        RefreshSidebarPresentation();
        float targetWidth = expanded ? sidebarExpandedWidth : sidebarCompactWidth;
        if (isActiveAndEnabled && sidebarSlideDuration > 0f) {
            sidebarSlide = StartCoroutine(SlideSidebar(targetWidth));
        } else {
            SetSidebarWidth(targetWidth);
        }
    }

    private void RefreshSidebarPresentation() {
        RefreshExpandLabel();
        sidebarExpandedWidth = sidebarCompactWidth;
        foreach (SidebarRow row in sidebarRows) {
            // 规则/观战模式仍由 AutoAction 决定可见性；整行退出布局，不留空位。
            row.root.gameObject.SetActive(row.label.gameObject.activeSelf);
            string fullName = row.fullName switch {
                "自动补花" => "table.auto.flower.full", "自动理牌" => "table.auto.sort.full",
                "自动和牌" => "table.auto.win.full", "不吃碰杠" => "table.auto.pass.full",
                "自动摸切" => "table.auto.cut.full", _ => row.fullName
            };
            TableText.Set(row.label, IsSidebarExpanded ? fullName : TableText.CompactActionKey(row.compactName));
            bool english = ConfigManager.CurrentLanguage == AppLanguage.English || PlatformText.IsAdditionalLanguage(ConfigManager.CurrentLanguage);
            row.label.fontSize = IsSidebarExpanded ? (english ? 30f : 36f) : (english ? 22f : 40f);
            row.label.enableAutoSizing = false;
            row.label.textWrappingMode = TextWrappingModes.NoWrap;
            row.label.alignment = IsSidebarExpanded ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Midline;
            row.label.margin = IsSidebarExpanded ? new Vector4(36f, 0f, 12f, 0f) : Vector4.zero;
            row.background.color = IsSidebarExpanded ? new Color32(27, 46, 99, 255) : Color.clear;
            row.indicator.gameObject.SetActive(IsSidebarExpanded);
            if (IsSidebarExpanded && row.root.gameObject.activeSelf) {
                // TMP 的首选宽度包含文本边距；只按当前规则可见的行计算，避免固定宽度留下空栏。
                float rowWidth = row.label.GetPreferredValues(row.label.text, Mathf.Infinity, Mathf.Infinity).x;
                sidebarExpandedWidth = Mathf.Max(sidebarExpandedWidth,
                    Mathf.Ceil(rowWidth + sidebarLayout.padding.horizontal));
            }
        }
        sidebarArrow.localRotation = Quaternion.Euler(0f, 0f, IsSidebarExpanded ? 180f : 0f);
    }

    private void UpdateSidebarIndicator(TMP_Text label, bool enabled) {
        foreach (SidebarRow row in sidebarRows) {
            if (row.label != label) continue;
            row.indicator.color = enabled ? trueColor : new Color32(89, 111, 159, 255);
            return;
        }
    }

    private IEnumerator SlideSidebar(float targetWidth) {
        float startWidth = sidebarRect.rect.width;
        float elapsed = 0f;
        while (elapsed < sidebarSlideDuration) {
            elapsed += Time.unscaledDeltaTime;
            SetSidebarWidth(Mathf.Lerp(startWidth, targetWidth,
                Mathf.SmoothStep(0f, 1f, elapsed / sidebarSlideDuration)));
            yield return null;
        }
        SetSidebarWidth(targetWidth);
        sidebarSlide = null;
    }

    private void SetSidebarWidth(float width) {
        sidebarRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
    }

    private void StopSidebarSlide() {
        if (sidebarSlide == null) return;
        StopCoroutine(sidebarSlide);
        sidebarSlide = null;
    }
}
