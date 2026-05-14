using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// B 端（被申请方）的"对方申请实时观战"小弹窗。挂 PanelPopupTransition。
/// 收到 friend/realtime_request_incoming 时弹出；点 allow / deny 会回包；点 close 仅本地关闭，等服务器 10s 超时。
/// 若被 cancel/revoke，本面板自动 Hide。
/// </summary>
public class RealtimeRequestIncomingPanel : MonoBehaviour {
    public static RealtimeRequestIncomingPanel Instance { get; private set; }

    [SerializeField] private PanelPopupTransition transition;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button allowButton;
    [SerializeField] private Button denyButton;
    [SerializeField] private Button closeButton;

    private string _currentRequestId;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (transition == null) transition = GetComponent<PanelPopupTransition>();
        if (allowButton != null) allowButton.onClick.AddListener(OnAllow);
        if (denyButton != null) denyButton.onClick.AddListener(OnDeny);
        if (closeButton != null) closeButton.onClick.AddListener(OnClose);
        gameObject.SetActive(false);
    }

    private void OnEnable() {
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnRealtimeRequestIncoming += HandleIncoming;
            FriendNetworkManager.Instance.OnRealtimeRequestRevoked += HandleRevoked;
        }
    }

    private void OnDisable() {
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnRealtimeRequestIncoming -= HandleIncoming;
            FriendNetworkManager.Instance.OnRealtimeRequestRevoked -= HandleRevoked;
        }
    }

    private void HandleIncoming(Response response) {
        _currentRequestId = response.realtime_request_id;
        string fromName = string.IsNullOrEmpty(response.realtime_from_username)
            ? (response.realtime_from_user_id?.ToString() ?? "对方")
            : response.realtime_from_username;
        if (messageText != null) messageText.text = $"玩家 {fromName} 申请实时观战您的游戏";
        if (transition != null) transition.Show();
        else gameObject.SetActive(true);
    }

    private void HandleRevoked(Response response) {
        if (string.IsNullOrEmpty(_currentRequestId)) return;
        if (!string.Equals(response.realtime_request_id, _currentRequestId)) return;
        _currentRequestId = null;
        if (transition != null) transition.Hide();
        else gameObject.SetActive(false);
    }

    private void OnAllow() {
        if (!string.IsNullOrEmpty(_currentRequestId)) {
            FriendNetworkManager.Instance?.RespondRealtime(_currentRequestId, true);
        }
        _currentRequestId = null;
        if (transition != null) transition.Hide();
        else gameObject.SetActive(false);
    }

    private void OnDeny() {
        if (!string.IsNullOrEmpty(_currentRequestId)) {
            FriendNetworkManager.Instance?.RespondRealtime(_currentRequestId, false);
        }
        _currentRequestId = null;
        if (transition != null) transition.Hide();
        else gameObject.SetActive(false);
    }

    private void OnClose() {
        _currentRequestId = null;
        if (transition != null) transition.Hide();
        else gameObject.SetActive(false);
    }
}
