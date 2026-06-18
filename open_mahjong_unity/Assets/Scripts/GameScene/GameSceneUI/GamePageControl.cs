using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 游戏页内的常驻按钮管理：延迟文本 + 规则书按钮 + 回到主菜单按钮。
/// 延迟文本订阅 NetworkManager 的 ping/pong 事件，按 100ms / 200ms 阈值显示绿/黄/红。
/// 规则书按钮：未打开时弹出入口面板，已打开时再次点击等同「返回」关闭。
/// 回到主菜单按钮：切换到 menu 窗口，并由 HeaderPanel 显示「正在对局中」的红色返回入口。
/// </summary>
public class GamePageControl : MonoBehaviour {
    [Header("延迟显示")]
    [SerializeField] private TMP_Text latencyText;
    [Tooltip("低于该延迟视为良好（绿色）")]
    [SerializeField] private int goodLatencyMs = 100;
    [Tooltip("低于该延迟视为一般（黄色），高于则视为较差（红色）")]
    [SerializeField] private int warningLatencyMs = 200;
    [SerializeField] private Color goodColor = new Color(0.3f, 0.85f, 0.4f);
    [SerializeField] private Color warningColor = new Color(0.95f, 0.78f, 0.2f);
    [SerializeField] private Color badColor = new Color(0.92f, 0.34f, 0.34f);

    [Header("按钮")]
    [SerializeField] private Button rulebookButton;
    [SerializeField] private Button backToMenuButton;

    [Header("规则书弹窗")]
    [SerializeField] private RulebookPanelController rulebookPanelController;

    private void Awake() {
        latencyText.text = "-- ms";
        latencyText.color = warningColor;
    }

    private void OnEnable() {
        NetworkManager.Instance.OnLatencyChanged += HandleLatencyChanged;
        ApplyLatency(NetworkManager.Instance.LatencyMs);
        rulebookButton.onClick.RemoveListener(OnRulebookClicked);
        rulebookButton.onClick.AddListener(OnRulebookClicked);
        backToMenuButton.onClick.RemoveListener(OnBackToMenuClicked);
        backToMenuButton.onClick.AddListener(OnBackToMenuClicked);
    }

    private void OnDisable() {
        if (NetworkManager.Instance != null) {
            NetworkManager.Instance.OnLatencyChanged -= HandleLatencyChanged;
        }
        rulebookButton.onClick.RemoveListener(OnRulebookClicked);
        backToMenuButton.onClick.RemoveListener(OnBackToMenuClicked);
    }

    private void HandleLatencyChanged(int latencyMs) {
        ApplyLatency(latencyMs);
    }

    private void ApplyLatency(int latencyMs) {
        if (latencyMs < 0) {
            latencyText.text = "-- ms";
            latencyText.color = warningColor;
            return;
        }
        latencyText.text = $"{latencyMs} ms";
        if (latencyMs < goodLatencyMs) {
            latencyText.color = goodColor;
        } else if (latencyMs < warningLatencyMs) {
            latencyText.color = warningColor;
        } else {
            latencyText.color = badColor;
        }
    }

    private void OnRulebookClicked() {
        if (rulebookPanelController.IsOpen) {
            rulebookPanelController.Close();
            return;
        }
        var gsm = NormalGameStateManager.Instance;
        rulebookPanelController.Open(gsm.roomRule, gsm.subRule);
    }

    private void OnBackToMenuClicked() {
        WindowsManager.Instance.SwitchWindow("menu");
        HeaderPanel.Instance.SetBackToGameVisible(true);
    }
}
