using UnityEngine;
using System;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;

/// <summary>
/// 房间网络管理器 - 处理所有房间相关的网络通信
/// </summary>
public class RoomNetworkManager : MonoBehaviour {
    
    public static RoomNetworkManager Instance { get; private set; }
    
    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    /// <summary>
    /// 获取 websocket 连接（通过 NetworkManager）
    /// </summary>
    private WebSocket GetWebSocket() {
        return NetworkManager.Instance?.GetWebSocket();
    }
    
    /// <summary>
    /// 处理房间相关的服务器响应消息
    /// </summary>
    public void HandleRoomMessage(Response response) {
        switch (response.type) {
            case "room/create_room_done":
                HandleCreateRoomResponse(response);
                break;
            case "room/get_room_list":
                HandleGetRoomListResponse(response);
                break;
            case "room/refresh_room_info":
                HandleGetRoomInfoResponse(response);
                break;
            case "room/join_room_done":
                HandleJoinRoomResponse(response);
                break;
            case "room/leave_room_done":
                HandleLeaveRoomResponse(response);
                break;
            default:
                Debug.LogWarning($"未知的房间消息类型: {response.type}");
                break;
        }
    }
    
    /// <summary>
    /// 处理创建房间响应
    /// </summary>
    private void HandleCreateRoomResponse(Response response) {
        NetworkManager.Instance.CreateRoomResponse.Invoke(response.success, response.message);
        UserDataManager.Instance.SetRoomId(response.room_info.room_id);
        NotificationManager.Instance.ShowTip("create_room", true, "创建房间成功");
    }
    
    /// <summary>
    /// 处理获取房间列表响应
    /// </summary>
    private void HandleGetRoomListResponse(Response response) {
        RoomListPanel.Instance.GetRoomListResponse(response.success, response.message, response.room_list);
        NotificationManager.Instance.ShowTip("get_room_list", true, "获取房间列表成功");
    }
    
    /// <summary>
    /// 处理获取房间信息响应
    /// </summary>
    private void HandleGetRoomInfoResponse(Response response) {
        Debug.Log("处理房间信息更新");
        WindowsManager.Instance.SwitchWindow("room");
        RoomWindowsManager.Instance.SwitchRoomWindow("roomInfo");
        RoomPanel.Instance.GetRoomInfoResponse(
            response.success, 
            response.message, 
            response.room_info
        );
        UserDataManager.Instance.SetRoomId(response.room_info.room_id);
    }
    
    /// <summary>
    /// 处理加入房间响应
    /// </summary>
    private void HandleJoinRoomResponse(Response response) {
        Debug.Log($"加入房间响应: {response.success}, {response.message}");
        NotificationManager.Instance.ShowTip("join_room", true, "加入房间成功");
        // 房间信息服务器从get_room_info中发送过来
    }
    
    /// <summary>
    /// 处理离开房间响应
    /// </summary>
    private void HandleLeaveRoomResponse(Response response) {
        Debug.Log($"离开房间响应: {response.success}, {response.message}");
        if (response.success) {
            UserDataManager.Instance.SetRoomId("");
            NotificationManager.Instance.ShowTip("leave_room", true, "离开房间成功");
        }
    }
    
    // ========== 房间相关的发送方法 ==========
    
    /// <summary>
    /// 创建国标房间
    /// </summary>
    public async void Create_GB_Room(GB_Create_RoomConfig config) {
        try {
            // 将字符串格式的随机种子转换为整数
            int randomSeed = 0;
            if (!string.IsNullOrEmpty(config.RandomSeed)) {
                if (!int.TryParse(config.RandomSeed, out randomSeed)) {
                    randomSeed = 0;
                }
            }
            
            var request = new CreateGBRoomRequest {
                type = "room/create_GB_room",
                rule = config.Rule,
                roomname = config.RoomName,
                gameround = config.GameRound,
                roundTimerValue = config.RoundTimer,
                stepTimerValue = config.StepTimer,
                tips = config.Tips,
                password = config.Password,
                random_seed = randomSeed,
                open_cuohe = config.CuoHe
            };
            Debug.Log($"发送创建房间消息: {config.RoomName}, {config.GameRound}, {config.Password}, {config.Rule}, {config.RoundTimer}, {config.StepTimer}, {config.Tips}, RandomSeed: {randomSeed}, CuoHe: {config.CuoHe}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            NetworkManager.Instance.CreateRoomResponse.Invoke(false, e.Message);
        }
    }
    
    /// <summary>
    /// 获取房间列表
    /// </summary>
    public async void GetRoomList() {
        try {
            var request = new GetRoomListRequest {
                type = "room/get_room_list"
            };
            Debug.Log($"发送获取房间列表消息{request.type}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            RoomListPanel.Instance.GetRoomListResponse(false, e.Message, null);
        }
    }
    
    /// <summary>
    /// 加入房间
    /// </summary>
    public async void JoinRoom(string roomId, string password) {
        var request = new JoinRoomRequest {
            type = "room/join_room",
            room_id = roomId,
            password = password
        };
        Debug.Log($"发送加入房间消息: {roomId}, {password}");
        await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
    }
    
    /// <summary>
    /// 离开房间
    /// </summary>
    public async void LeaveRoom(string roomId) {
        var request = new LeaveRoomRequest {
            type = "room/leave_room",
            room_id = roomId
        };
        await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
    }
    
    /// <summary>
    /// 开始游戏
    /// </summary>
    public async void StartGame(string roomId) {
        var request = new StartGameRequest {
            type = "room/start_game",
            room_id = roomId
        };
        await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
    }
    
    /// <summary>
    /// 添加机器人到房间
    /// </summary>
    public async void AddBotToRoom(string roomId) {
        var request = new AddBotToRoomRequest {
            type = "room/add_bot",
            room_id = roomId
        };
        await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
    }
}

