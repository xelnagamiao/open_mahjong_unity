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

    public static void TryJoin(string roomId, bool needPassword) {
        if (needPassword) {
            Instance.ShowForRoom(roomId);
            return;
        }
        RoomNetworkManager.Instance.JoinRoom(roomId, "");
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

    public void ShowForRoom(string roomId) {
        if (LobbyStateGuard.BlockIfInMatchQueueForRoom()) return;
        if (LobbyStateGuard.IsInRoom) {
            NotificationManager.Instance.ShowTip("join_room", false, "请先退出当前房间");
            return;
        }
        _roomId = roomId;
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
        transition.Hide(() => RoomNetworkManager.Instance.JoinRoom(roomId, pwd));
    }

    private void Cancel() {
        transition.Hide();
    }
}
