using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 牌谱/观战页面顶部的退出按钮统一管理器：
/// - 正常对局：所有按钮都隐藏
/// - 牌谱回放：仅显示「退出牌谱」
/// - 直播观战 / 观战内牌谱：仅显示「退出观战」
/// - 实时观战：仅显示「退出实时观战」（点击发送 friend/exit_realtime）
/// 由 GameRecordManager / NormalGameStateManager 在切换模式时显式调用，保证在场景层级中无论按钮挂在哪个父节点都能可靠隐藏。
/// </summary>
public class ExitButtonManager : MonoBehaviour {
    public static ExitButtonManager Instance { get; private set; }

    [SerializeField] private Button quitRecordButton;
    [SerializeField] private Button quitSpectatorButton;
    [SerializeField] private Button quitRealtimeSpectatorButton;

    private void Awake() {
        Instance = this;
        HideAll();
    }

    private void Start() {
        if (quitRealtimeSpectatorButton != null) {
            quitRealtimeSpectatorButton.onClick.AddListener(OnClickQuitRealtimeSpectator);
        }
    }

    /// <summary>正常对局：隐藏全部退出按钮。</summary>
    public void HideAll() {
        if (quitRecordButton != null) quitRecordButton.gameObject.SetActive(false);
        if (quitSpectatorButton != null) quitSpectatorButton.gameObject.SetActive(false);
        if (quitRealtimeSpectatorButton != null) quitRealtimeSpectatorButton.gameObject.SetActive(false);
    }

    /// <summary>牌谱模式：仅显示退出牌谱按钮。</summary>
    public void ShowForRecord() {
        if (quitRecordButton != null) quitRecordButton.gameObject.SetActive(true);
        if (quitSpectatorButton != null) quitSpectatorButton.gameObject.SetActive(false);
        if (quitRealtimeSpectatorButton != null) quitRealtimeSpectatorButton.gameObject.SetActive(false);
    }

    /// <summary>直播观战 / 观战内牌谱：仅显示退出观战按钮。</summary>
    public void ShowForSpectator() {
        if (quitRecordButton != null) quitRecordButton.gameObject.SetActive(false);
        if (quitSpectatorButton != null) quitSpectatorButton.gameObject.SetActive(true);
        if (quitRealtimeSpectatorButton != null) quitRealtimeSpectatorButton.gameObject.SetActive(false);
    }

    /// <summary>实时观战：仅显示退出实时观战按钮。点击后调用 FriendNetworkManager.ExitRealtime()。</summary>
    public void ShowForRealtimeSpectator() {
        if (quitRecordButton != null) quitRecordButton.gameObject.SetActive(false);
        if (quitSpectatorButton != null) quitSpectatorButton.gameObject.SetActive(false);
        if (quitRealtimeSpectatorButton != null) quitRealtimeSpectatorButton.gameObject.SetActive(true);
    }

    private void OnClickQuitRealtimeSpectator() {
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.ExitRealtime();
        }
        PostGameNavigator.ExitToFriend();
    }

    /// <summary>对外暴露按钮引用，供 GameRecordManager 等绑定 onClick。</summary>
    public Button QuitRecordButton => quitRecordButton;
    public Button QuitSpectatorButton => quitSpectatorButton;
    public Button QuitRealtimeSpectatorButton => quitRealtimeSpectatorButton;
}
