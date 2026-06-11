using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 承诺值 / 盐值详情浮层，由 RoundPanel 统一管理；点击 RoundPanel 区域时短暂展示。
/// 复制按钮将「承诺值：xxxx 盐值：xxxx」写入剪贴板（Windows / WebGL 通用）。
/// </summary>
public class CommitmentSaltDetailPanel : MonoBehaviour {
    [SerializeField] private TMP_Text commitmentText;
    [SerializeField] private TMP_Text saltText;
    [SerializeField] private Button copyButton;
    [SerializeField] private CanvasGroupFadeIn fadeIn;

    private string _commitment = "-";
    private string _salt = "-";

    private void Awake() {
        gameObject.SetActive(false);
        if (copyButton != null) {
            copyButton.onClick.AddListener(OnCopyButtonClick);
        }
    }

    public void SetContent(string commitment, string salt) {
        _commitment = CommitmentSaltDisplay.NormalizeCommitment(commitment);
        _salt = CommitmentSaltDisplay.FormatSaltLabel(salt);
        if (commitmentText != null) {
            commitmentText.text = _commitment;
        }
        if (saltText != null) {
            saltText.text = _salt;
        }
    }

    private void OnCopyButtonClick() {
        ClipboardUtility.Copy($"承诺值：{_commitment} 盐值：{_salt}");
        NotificationManager.Instance.ShowTip("承诺值", true, "已复制承诺值与盐值");
        // 重置浮层隐藏计时，避免点完按钮立刻消失
        RoundPanel.Instance?.RequestShowCommitmentSaltDetail();
    }

    public void ShowWithFadeIn() {
        gameObject.SetActive(true);
        if (fadeIn != null) {
            fadeIn.PlayFadeIn();
        }
    }

    public void HideImmediate() {
        gameObject.SetActive(false);
    }
}
