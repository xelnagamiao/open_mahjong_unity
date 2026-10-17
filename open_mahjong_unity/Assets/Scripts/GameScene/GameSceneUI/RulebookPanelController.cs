using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 游戏页规则书弹窗，支持直接显示规则文本或打开对应的规则书页面。
/// 弹窗显示/关闭复用现有的 PanelPopupTransition 淡入淡出。
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class RulebookPanelController : MonoBehaviour {
    [Header("淡入淡出")]
    [SerializeField] private PanelPopupTransition popup;

    [Header("UI")]
    [SerializeField] private Button openRulebookButton;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text openButtonText;
    [SerializeField] private TMP_Text inlineRuleText;

    [Header("番表")]
    [SerializeField] private GuobiaoFanTablePanel fanTablePanel;

    private string _currentPath;
    private bool _layoutCaptured;
    private bool _manualFromFanTable;
    private bool _returnAfterClose;
    private bool _initialized;
    private Vector2 _compactSize, _compactPosition, _compactAnchorMin, _compactAnchorMax;
    private Vector2 _compactTitleSize, _compactTitlePosition, _compactBackPosition;

    private void Awake() => Initialize();

    private void Initialize() {
        if (_initialized) return;
        _initialized = true;
        if (popup == null) popup = GetComponent<PanelPopupTransition>();
        CaptureCompactLayout();
        gameObject.SetActive(false);
        openRulebookButton.onClick.AddListener(OnOpenRulebookClicked);
        backButton.onClick.AddListener(Close);
    }

    /// <summary>对局、实时观战与牌谱共用的入口；牌谱配置不能回退到上一场对局。</summary>
    public void OpenCurrent() {
        var record = GameRecordManager.Instance;
        if (record && record.TryGetActiveRecordRuleContext(out string roomRule, out string subRule)) {
            Open(roomRule, subRule, record.GetDetailedConfigSnapshot());
            return;
        }
        var session = GameSession.Current;
        Open(session.RoomRule, session.SubRule, session.DetailedConfig);
    }

    /// <summary>显示弹窗。房间规则与子规则决定跳转目标和按钮上显示的规则名。</summary>
    public void Open(string roomRule, string subRule = null, IDictionary<string, object> detailedConfig = null) {
        Initialize();
        _manualFromFanTable = _returnAfterClose = false;
        _currentPath = ResolveRulebookPath(roomRule, subRule, detailedConfig);
        if (fanTablePanel) fanTablePanel.HideImmediate();
        // 旧牌谱可能只存 sub_rule；番表与完整说明书须使用同一份规则解析结果。
        string resolvedRule = RuleRegistry.Resolve(roomRule, subRule)?.RuleId ?? roomRule;
        string fanRuleId = MahjongFanCatalog.ForRoom(resolvedRule, subRule);
        if (fanRuleId != null && fanTablePanel) {
            popup.HideImmediate();
            fanTablePanel.Open(fanRuleId, _currentPath);
            return;
        }
        ApplyTexts(roomRule, subRule);
        ApplyInlineText(RuleRegistry.Resolve(roomRule, subRule)?.RulebookText?.Invoke(subRule, detailedConfig));
        popup.Show();
    }

    public void Close() {
        if (_manualFromFanTable) {
            _returnAfterClose = true;
            popup.Hide();
            return;
        }
        if (fanTablePanel) fanTablePanel.HideImmediate();
        popup.Hide();
    }

    public void HideImmediate() {
        _manualFromFanTable = _returnAfterClose = false;
        if (fanTablePanel) fanTablePanel.HideImmediate();
        popup.HideImmediate();
    }

    private void OnDisable() {
        bool resume = _returnAfterClose && transform.parent && transform.parent.gameObject.activeInHierarchy;
        _manualFromFanTable = _returnAfterClose = false;
        if (!fanTablePanel) return;
        if (resume) fanTablePanel.Resume();
        else fanTablePanel.HideImmediate();
    }

    public void OpenManualConfirmation(string ruleName, string path) {
        Initialize();
        _currentPath = path;
        _manualFromFanTable = true;
        _returnAfterClose = false;
        fanTablePanel.HideImmediate();
        ApplyRuleName(ruleName);
        ApplyInlineText(null);
        transform.SetAsLastSibling();
        popup.Show();
        if (UnityEngine.EventSystems.EventSystem.current)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(openRulebookButton.gameObject);
    }

    /// <summary>规则书弹窗是否处于显示中（含收起动画尚未结束的阶段）。</summary>
    public bool IsOpen => gameObject.activeSelf || (fanTablePanel && fanTablePanel.IsOpen);

    private void ApplyTexts(string roomRule, string subRule) {
        ApplyRuleName(RuleDisplayName(roomRule, subRule));
    }

    private void ApplyRuleName(string ruleName) {
        titleText.text = $"{ruleName}规则书";
        openButtonText.text = "查看规则书（跳转网页）";
    }

    private void CaptureCompactLayout() {
        if (_layoutCaptured) return;
        var rect = (RectTransform)transform;
        _compactSize = rect.sizeDelta;
        _compactPosition = rect.anchoredPosition;
        _compactAnchorMin = rect.anchorMin;
        _compactAnchorMax = rect.anchorMax;
        _compactTitleSize = titleText.rectTransform.sizeDelta;
        _compactTitlePosition = titleText.rectTransform.anchoredPosition;
        _compactBackPosition = ((RectTransform)backButton.transform).anchoredPosition;
        _layoutCaptured = true;
    }

    private void ApplyInlineText(string text) {
        CaptureCompactLayout();
        bool inline = !string.IsNullOrEmpty(text);
        var rect = (RectTransform)transform;
        var backRect = (RectTransform)backButton.transform;
        openRulebookButton.gameObject.SetActive(!inline);
        inlineRuleText.gameObject.SetActive(inline);
        inlineRuleText.text = text ?? "";
        if (inline) {
            var available = ((RectTransform)rect.parent).rect.size;
            var size = new Vector2(Mathf.Min(1240f, available.x - 80f), Mathf.Min(820f, available.y - 100f));
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            titleText.rectTransform.sizeDelta = new Vector2(size.x - 88f, _compactTitleSize.y);
            titleText.rectTransform.anchoredPosition = new Vector2(0f, size.y * .5f - 54f);
            backRect.anchoredPosition = new Vector2(0f, -size.y * .5f + 55f);
        } else {
            rect.anchorMin = _compactAnchorMin;
            rect.anchorMax = _compactAnchorMax;
            rect.anchoredPosition = _compactPosition;
            rect.sizeDelta = _compactSize;
            titleText.rectTransform.sizeDelta = _compactTitleSize;
            titleText.rectTransform.anchoredPosition = _compactTitlePosition;
            backRect.anchoredPosition = _compactBackPosition;
        }
    }

    /// <summary>规则族可按子规则选择文档；未声明时沿用规则书页签。</summary>
    private static string ResolveRulebookPath(string roomRule, string subRule, IDictionary<string, object> detailedConfig) {
        RuleManifest manifest = RuleRegistry.Resolve(roomRule, subRule);
        return manifest?.RulebookPath?.Invoke(subRule, detailedConfig)
            ?? "/rulebook/" + (manifest?.RulebookKey ?? manifest?.RuleId ?? "guobiao");
    }

    private static string RuleDisplayName(string roomRule, string subRule) {
        if (!string.IsNullOrEmpty(subRule)) {
            string wholeName = RuleNameDictionary.GetWholeName(subRule);
            if (!string.IsNullOrEmpty(wholeName) && wholeName != subRule) return wholeName;
        }
        string displayName = RuleRegistry.Resolve(roomRule, subRule)?.DisplayName;
        return string.IsNullOrEmpty(displayName) ? "麻将" : displayName;
    }

    /// <summary>Published HTTPS sources are supported alongside the existing site routes.</summary>
    public static string ResolveRulebookUrl(string path, string webUrl) {
        return Uri.TryCreate(path, UriKind.Absolute, out var source) && source.Scheme == Uri.UriSchemeHttps
            ? source.AbsoluteUri : (webUrl ?? "").TrimEnd('/') + path;
    }

    private void OnOpenRulebookClicked() {
        if (inlineRuleText.gameObject.activeSelf) return;
        string url = ResolveRulebookUrl(_currentPath, GameHost.Current.WebUrl);
        Application.OpenURL(url);
    }
}
