using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 立直选牌模式控制：进入后隐藏其他操作按钮容器，仅显示「返回」按钮，
/// 自家手牌按 selfRiichiCandidateCuts 限制可点（不可立直的切牌变暗）；
/// 点击合法手牌发送 riichi_cut；点击返回回到普通切牌状态；
/// SwitchCurrentPlayer 在自家行动结束/超时/清空操作时也会调用 ExitRiichiCutMode。
/// </summary>
public class RiichiCutSelectionController : MonoBehaviour {
    public static RiichiCutSelectionController Instance { get; private set; }

    [SerializeField] private GameObject riichiCutPanelRoot;
    [SerializeField] private Button backButton;

    public bool IsActive { get; private set; }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (riichiCutPanelRoot != null) riichiCutPanelRoot.SetActive(false);
        if (backButton != null) backButton.onClick.AddListener(ExitRiichiCutMode);
    }

    public void EnterRiichiCutMode() {
        if (IsActive) return;
        HandCardSelectionController.Instance?.DisarmAll();
        IsActive = true;
        GameCanvas.Instance.SetActionButtonContainerVisible(false);
        if (riichiCutPanelRoot != null) riichiCutPanelRoot.SetActive(true);
        GameCanvas.Instance.RefreshHandTileSelectability();
    }

    public void ExitRiichiCutMode() {
        if (!IsActive) return;
        IsActive = false;
        if (riichiCutPanelRoot != null) riichiCutPanelRoot.SetActive(false);
        GameCanvas.Instance.SetActionButtonContainerVisible(true);
        GameCanvas.Instance.RefreshHandTileSelectability();
    }
}
