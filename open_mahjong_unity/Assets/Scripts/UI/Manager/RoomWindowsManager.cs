using System.Collections;
using UnityEngine;

/// <summary>
/// 房间窗口管理器，管理房间相关的窗口切换（房间列表、房间、创建房间）
/// </summary>
public class RoomWindowsManager : MonoBehaviour {
    [Header("房间相关窗口")]
    [SerializeField] private GameObject roomPanel; // 房间窗口
    [SerializeField] private GameObject createRoomPanel; // 创建房间窗口

    public static RoomWindowsManager Instance { get; private set; }

    private Coroutine _switchRoutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Debug.Log($"Destroying duplicate NotificationManager. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SwitchRoomWindow("createRoom");
    }

    /// <summary>
    /// 切换房间相关窗口
    /// </summary>
    /// <param name="targetWindow">目标窗口：roomList, roomInfo, createRoom</param>
    public void SwitchRoomWindow(string targetWindow) {
        GameObject target = Resolve(targetWindow);
        if (target == null) return;

        GameObject current = null;
        if (roomPanel != null && roomPanel.activeInHierarchy) current = roomPanel;
        else if (createRoomPanel != null && createRoomPanel.activeInHierarchy) current = createRoomPanel;

        if (current == target) return;

        if (_switchRoutine != null) {
            StopCoroutine(_switchRoutine);
            _switchRoutine = null;
            InstantSwitch(target);
            return;
        }

        if (current == null) {
            InstantSwitch(target);
            return;
        }

        _switchRoutine = StartCoroutine(SwitchRoutine(current, target));
    }

    private GameObject Resolve(string targetWindow) {
        switch (targetWindow) {
            case "roomInfo":
                return roomPanel;
            case "createRoom":
                return createRoomPanel;
            default:
                Debug.LogWarning($"未知的房间窗口类型: {targetWindow}");
                return null;
        }
    }

    private void InstantSwitch(GameObject target) {
        GameObject hide = target == roomPanel ? createRoomPanel : roomPanel;
        WindowFadeTransition.Snap(hide, target);
    }

    private IEnumerator SwitchRoutine(GameObject from, GameObject to) {
        yield return WindowFadeTransition.CrossFade(from, to, WindowFadeTransition.DurationSeconds);
        _switchRoutine = null;
    }

    /// <summary>
    /// 关闭所有房间窗口
    /// </summary>
    public void CloseAllRoomWindows() {
        if (_switchRoutine != null) {
            StopCoroutine(_switchRoutine);
            _switchRoutine = null;
        }
        WindowFadeTransition.Snap(new[] { roomPanel, createRoomPanel }, (GameObject[])null);
    }
}
