using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Overlay 上的密码加入框。Awake 时自行隐藏，大厅与赛事房间列表共用。
/// </summary>
[DefaultExecutionOrder(-100)]
public class PasswordJoinPanel : MonoBehaviour {
    public static PasswordJoinPanel Instance { get; private set; }

    private PanelPopupTransition _transition;
    private TMP_InputField _input;
    private Button _admit;
    private Button _cancel;
    private string _roomId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() {
        for (int i = 0; i < SceneManager.sceneCount; i++) {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded) continue;
            foreach (GameObject root in scene.GetRootGameObjects()) {
                if (root.name != "OverlayCanvas") continue;
                Attach(root.transform);
                return;
            }
        }
    }

    private static void Attach(Transform t) {
        if (t.name.StartsWith("PasswordInputPanel")) {
            if (t.GetComponent<PasswordJoinPanel>() == null) {
                t.gameObject.AddComponent<PasswordJoinPanel>();
            }
            return;
        }
        for (int i = 0; i < t.childCount; i++) {
            Attach(t.GetChild(i));
        }
    }

    public static void TryJoin(string roomId, bool needPassword) {
        if (needPassword) {
            Instance.ShowForRoom(roomId);
            return;
        }
        RoomNetworkManager.Instance.JoinRoom(roomId, "");
    }

    private void Awake() {
        Instance = this;
        _transition = GetComponent<PanelPopupTransition>();
        ResolveChildren();
        _admit.onClick.AddListener(Admit);
        _cancel.onClick.AddListener(Cancel);
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
        _input.text = "";
        _transition.Show();
    }

    private void Admit() {
        if (string.IsNullOrEmpty(_input.text)) {
            NotificationManager.Instance.ShowTip("join_room", false, "密码不能为空");
            return;
        }
        string pwd = _input.text;
        string roomId = _roomId;
        _transition.Hide(() => RoomNetworkManager.Instance.JoinRoom(roomId, pwd));
    }

    private void Cancel() {
        _transition.Hide();
    }

    private void ResolveChildren() {
        _input = GetComponentInChildren<TMP_InputField>(true);
        foreach (Button button in GetComponentsInChildren<Button>(true)) {
            if (button.gameObject.name == "Join") _admit = button;
            else if (button.gameObject.name == "Back") _cancel = button;
        }
    }
}
