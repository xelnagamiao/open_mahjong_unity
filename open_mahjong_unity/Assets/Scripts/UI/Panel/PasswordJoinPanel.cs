using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Overlay 上的密码加入框。Awake 时自行隐藏，大厅与赛事房间列表共用。
/// </summary>
[DefaultExecutionOrder(-100)]
public class PasswordJoinPanel : MonoBehaviour {
    public static PasswordJoinPanel Instance { get; private set; }

    [SerializeField] private PanelPopupTransition transition;
    [SerializeField] private TMP_InputField input;
    [SerializeField] private Button admitButton;
    [SerializeField] private Button cancelButton;

    private string _roomId;
    private System.Action<string> _join;
    private System.Action _cancel;

    public static void TryJoin(string roomId, bool needPassword, System.Action<string> join = null, System.Action cancel = null) {
        if (needPassword) {
            Instance.ShowForRoom(roomId, join, cancel);
            return;
        }
        if (join != null) join(""); else RoomNetworkManager.Instance.JoinRoom(roomId, "");
    }

    private void Awake() {
        Instance = this;
        admitButton.onClick.AddListener(Admit);
        cancelButton.onClick.AddListener(Cancel);
        gameObject.SetActive(false);
    }

    private void OnDestroy() {
        if (Instance == this) Instance = null;
    }

    public void ShowForRoom(string roomId, System.Action<string> join = null, System.Action cancel = null) {
        if (LobbyStateGuard.BlockIfInMatchQueueForRoom()) return;
        if (LobbyStateGuard.IsInRoom) {
            NotificationManager.Instance.ShowTip("join_room", false, "请先退出当前房间");
            return;
        }
        _roomId = roomId;
        _join = join;
        _cancel = cancel;
        input.text = "";
        transition.Show();
    }

    private void Admit() {
        if (string.IsNullOrEmpty(input.text)) {
            NotificationManager.Instance.ShowTip("join_room", false, "密码不能为空");
            return;
        }
        string pwd = input.text;
        string roomId = _roomId;
        var join = _join;
        _join = null; _cancel = null; input.text = "";
        transition.Hide(() => { if (join != null) join(pwd); else RoomNetworkManager.Instance.JoinRoom(roomId, pwd); });
    }

    private void Cancel() {
        var cancel = _cancel;
        _join = null; _cancel = null; input.text = "";
        transition.Hide();
        cancel?.Invoke();
    }
}
