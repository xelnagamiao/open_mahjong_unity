using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 游戏页规则书弹窗：显示「打开 X 规则规则书」与「返回」两个按钮。
/// 点击打开按钮时跳转到 web 站对应规则的规则书页面（不同规则 → 不同页签）。
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

    private string _currentPath;

    private void Awake() {
        if (popup == null) popup = GetComponent<PanelPopupTransition>();
        gameObject.SetActive(false);
        openRulebookButton.onClick.AddListener(OnOpenRulebookClicked);
        backButton.onClick.AddListener(Close);
    }

    /// <summary>显示弹窗。房间规则与子规则决定跳转目标和按钮上显示的规则名。</summary>
    public void Open(string roomRule, string subRule = null, IDictionary<string, object> detailedConfig = null) {
        _currentPath = ResolveRulebookPath(roomRule, subRule, detailedConfig);
        ApplyTexts(roomRule, subRule);
        popup.Show();
    }

    public void Close() {
        popup.Hide();
    }

    /// <summary>规则书弹窗是否处于显示中（含收起动画尚未结束的阶段）。</summary>
    public bool IsOpen => gameObject.activeSelf;

    private void ApplyTexts(string roomRule, string subRule) {
        string ruleName = RuleDisplayName(roomRule, subRule);
        titleText.text = $"{ruleName}规则书";
        openButtonText.text = $"打开{ruleName}规则书";
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

    private void OnOpenRulebookClicked() {
        string url = GameHost.Current.WebUrl.TrimEnd('/') + _currentPath;
        Application.OpenURL(url);
    }
}
