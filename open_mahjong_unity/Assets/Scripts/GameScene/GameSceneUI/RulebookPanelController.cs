using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    private string _currentRule;

    private void Awake() {
        if (popup == null) popup = GetComponent<PanelPopupTransition>();
        gameObject.SetActive(false);
        openRulebookButton.onClick.AddListener(OnOpenRulebookClicked);
        backButton.onClick.AddListener(Close);
    }

    /// <summary>显示弹窗。规则字段决定跳转目标和按钮上显示的规则名。</summary>
    public void Open(string rule) {
        _currentRule = string.IsNullOrEmpty(rule) ? "guobiao" : rule;
        ApplyTexts(_currentRule);
        popup.Show();
    }

    public void Close() {
        popup.Hide();
    }

    private void ApplyTexts(string rule) {
        string ruleName = RuleDisplayName(rule);
        titleText.text = $"{ruleName}规则书";
        openButtonText.text = $"打开{ruleName}规则书";
    }

    private static string RuleDisplayName(string rule) {
        switch (rule) {
            case "guobiao": return "国标麻将";
            case "qingque": return "青雀";
            case "classical": return "古典麻将";
            case "riichi": return "立直麻将";
            default: return "麻将";
        }
    }

    private void OnOpenRulebookClicked() {
        string url = $"{ConfigManager.webUrl}/rulebook/{_currentRule}";
        Application.OpenURL(url);
    }
}
