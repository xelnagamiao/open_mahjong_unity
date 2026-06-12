using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A 端（发起者）的"等待对方回应"小弹窗。挂 PanelPopupTransition。
/// 流程：A 点了好友的"实时观战"按钮 → FriendItem 先 Show() 本面板，再发请求。
///   - 服务器 friend/realtime_request_result 失败：直接显示原因并隐藏。
///   - 服务器 friend/realtime_request_result 成功：开始 10s 倒计时。
///   - friend/realtime_started：跳转 game 场景。
///   - friend/realtime_request_timeout / declined：显示结果文本 2s 后关闭。
///   - 点击 cancelButton：撤销请求并关闭。
/// </summary>
public class RealtimeRequestWaitPanel : MonoBehaviour {
    public static RealtimeRequestWaitPanel Instance { get; private set; }

    [SerializeField] private PanelPopupTransition transition;
    [SerializeField] private TMP_Text waitingText;
    [SerializeField] private TMP_Text countdownText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button cancelButton;

    private string _pendingRequestId;
    private int _targetUserId;
    private string _targetUsername;
    private Coroutine _countdownRoutine;
    private const int CountdownSeconds = 10;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (transition == null) transition = GetComponent<PanelPopupTransition>();
        if (cancelButton != null) cancelButton.onClick.AddListener(OnCancelClicked);
        gameObject.SetActive(false);
    }

    private void OnEnable() {
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnRealtimeRequestResult += HandleRequestResult;
            FriendNetworkManager.Instance.OnRealtimeRequestTimeout += HandleTimeout;
            FriendNetworkManager.Instance.OnRealtimeRequestDeclined += HandleDeclined;
            FriendNetworkManager.Instance.OnRealtimeStarted += HandleStarted;
        }
    }

    private void OnDisable() {
        if (FriendNetworkManager.Instance != null) {
            FriendNetworkManager.Instance.OnRealtimeRequestResult -= HandleRequestResult;
            FriendNetworkManager.Instance.OnRealtimeRequestTimeout -= HandleTimeout;
            FriendNetworkManager.Instance.OnRealtimeRequestDeclined -= HandleDeclined;
            FriendNetworkManager.Instance.OnRealtimeStarted -= HandleStarted;
        }
        StopCountdown();
    }

    public void ShowWaiting(int targetUserId, string targetUsername) {
        _targetUserId = targetUserId;
        _targetUsername = targetUsername ?? "";
        _pendingRequestId = null;
        if (waitingText != null) waitingText.text = string.IsNullOrEmpty(_targetUsername)
            ? "正在等待玩家回应观战请求..."
            : $"正在等待 {_targetUsername} 回应观战请求...";
        if (resultText != null) resultText.gameObject.SetActive(false);
        if (countdownText != null) countdownText.text = $"{CountdownSeconds}";
        if (cancelButton != null) {
            cancelButton.interactable = true;
            cancelButton.gameObject.SetActive(true);
        }
        if (transition != null) transition.Show();
        else gameObject.SetActive(true);
    }

    private void HandleRequestResult(Response response) {
        if (!gameObject.activeSelf) return;
        if (!response.success) {
            ShowResultAndClose(response.message ?? "请求失败");
            return;
        }
        _pendingRequestId = response.realtime_request_id;
        StartCountdown();
    }

    private void HandleTimeout(Response response) {
        if (!gameObject.activeSelf) return;
        if (string.IsNullOrEmpty(_pendingRequestId)) return;
        if (!string.Equals(response.realtime_request_id, _pendingRequestId)) return;
        ShowResultAndClose(response.message ?? "请求已超时，对方忽略了该请求");
    }

    private void HandleDeclined(Response response) {
        if (!gameObject.activeSelf) return;
        if (string.IsNullOrEmpty(_pendingRequestId)) return;
        if (!string.Equals(response.realtime_request_id, _pendingRequestId)) return;
        ShowResultAndClose(response.message ?? "对方拒绝了实时观战申请");
    }

    private void HandleStarted(Response response) {
        if (!gameObject.activeSelf) return;
        StopCountdown();
        _pendingRequestId = null;
        if (transition != null) transition.Hide();
        else gameObject.SetActive(false);

        if (LobbyStateGuard.BlockIfInMatchQueueForSpectator()) return;
        if (GameSessionGuard.BlockIfExclusiveSession("进入实时观战")) return;

        string gamestateId = response.realtime_gamestate_id;
        if (string.IsNullOrEmpty(gamestateId)) {
            Debug.LogWarning("realtime_started 缺少 gamestate_id");
            return;
        }
        int hostUserId = response.realtime_to_user_id ?? 0;
        if (NormalGameStateManager.Instance != null) {
            NormalGameStateManager.Instance.StartAsRealtimeSpectator(gamestateId, hostUserId);
        }
        WindowsManager.Instance?.SwitchWindow("game");
    }

    private void OnCancelClicked() {
        if (!string.IsNullOrEmpty(_pendingRequestId)) {
            FriendNetworkManager.Instance?.CancelRealtime(_pendingRequestId);
        }
        _pendingRequestId = null;
        StopCountdown();
        if (transition != null) transition.Hide();
        else gameObject.SetActive(false);
    }

    private void StartCountdown() {
        StopCountdown();
        _countdownRoutine = StartCoroutine(RunCountdown());
    }

    private void StopCountdown() {
        if (_countdownRoutine != null) {
            StopCoroutine(_countdownRoutine);
            _countdownRoutine = null;
        }
    }

    private IEnumerator RunCountdown() {
        for (int i = CountdownSeconds; i >= 0; i--) {
            if (countdownText != null) countdownText.text = $"{i}";
            yield return new WaitForSecondsRealtime(1f);
        }
        _countdownRoutine = null;
    }

    private void ShowResultAndClose(string text) {
        StopCountdown();
        _pendingRequestId = null;
        if (waitingText != null) waitingText.gameObject.SetActive(false);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        if (resultText != null) {
            resultText.gameObject.SetActive(true);
            resultText.text = text;
        }
        StartCoroutine(_DelayedClose());
    }

    private IEnumerator _DelayedClose() {
        yield return new WaitForSecondsRealtime(2f);
        if (waitingText != null) waitingText.gameObject.SetActive(true);
        if (countdownText != null) countdownText.gameObject.SetActive(true);
        if (transition != null) transition.Hide();
        else gameObject.SetActive(false);
    }
}
