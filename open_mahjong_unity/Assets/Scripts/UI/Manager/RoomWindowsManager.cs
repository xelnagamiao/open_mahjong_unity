using UnityEngine;

/// <summary>
/// 房间窗口管理器，管理房间相关的窗口切换（房间列表、房间、创建房间）
/// </summary>
public class RoomWindowsManager : MonoBehaviour
{
    [Header("房间相关窗口")]
    [SerializeField] private GameObject roomPanel; // 房间窗口
    [SerializeField] private GameObject createRoomPanel; // 创建房间窗口

    public static RoomWindowsManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
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
    public void SwitchRoomWindow(string targetWindow)
    {
        Debug.Log($"切换到房间窗口: {targetWindow}");
        
        // 先关闭所有房间窗口
        roomPanel.SetActive(false);
        createRoomPanel.SetActive(false);

        // 根据目标窗口打开对应窗口
        switch (targetWindow)
        {
            case "roomInfo":
                roomPanel.SetActive(true);
                break;
            case "createRoom":
                createRoomPanel.SetActive(true);
                break;
            default:
                Debug.LogWarning($"未知的房间窗口类型: {targetWindow}");
                break;
        }
    }

    /// <summary>
    /// 关闭所有房间窗口
    /// </summary>
    public void CloseAllRoomWindows()
    {
        roomPanel.SetActive(false);
        createRoomPanel.SetActive(false);
    }
}

