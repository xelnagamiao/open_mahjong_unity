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

    /// <summary>
    /// 待进入的房间 ID。JoinRoom 设为具体 roomId；SyncMyRoom 设为 "*" 表示接受任意权威同步。
    /// 为空表示未请求进入，大厅中收到的滞后 refresh_room_info 应忽略，避免错误跳进房间页。
    /// </summary>
    private string _pendingEnterRoomId;

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
        return NetworkManager.Instance.GetWebSocket();
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
            case "room/sync_not_in_room":
                HandleSyncNotInRoomResponse(response);
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
    /// 处理获取房间列表响应（根据服务端回显的 show_tip 决定是否显示 tips）
    /// </summary>
    private void HandleGetRoomListResponse(Response response) {
        RoomListPanel.Instance.GetRoomListResponse(response.success, response.message, response.room_list);
        if (response.show_tip) {
            NotificationManager.Instance.ShowTip("get_room_list", true, "刷新房间列表成功");
        }
    }

    /// <summary>
    /// 处理获取房间信息响应。
    /// 仅在「当前用户确实在房间内」且「已在房间页 / 正在请求进入 / 重连同步」时应用，
    /// 避免退出房间后滞后的 refresh_room_info 把用户错误带回房间页。
    /// </summary>
    private void HandleGetRoomInfoResponse(Response response) {
        Debug.Log("处理房间信息更新");
        if (!response.success || response.room_info == null) {
            Debug.LogWarning($"忽略无效的 refresh_room_info: success={response.success}");
            return;
        }

        int myId = UserDataManager.Instance.UserId;
        int[] playerList = response.room_info.player_list;
        bool iAmInRoom = playerList != null && Array.IndexOf(playerList, myId) >= 0;
        if (!iAmInRoom) {
            Debug.Log("忽略 refresh_room_info：当前用户不在 player_list 中");
            return;
        }

        string roomId = response.room_info.room_id;
        string currentWindow = WindowsManager.Instance.GetCurrentWindow();
        bool onRoomPage = currentWindow == "room";
        bool pendingOk = _pendingEnterRoomId == "*"
            || (!string.IsNullOrEmpty(_pendingEnterRoomId) && _pendingEnterRoomId == roomId);
        bool reconnectLobbySync = AutoReconnect.IsActive && !AutoReconnect.ExpectGameRestore;

        if (!onRoomPage && !pendingOk && !reconnectLobbySync) {
            Debug.Log("忽略 refresh_room_info：未处于房间页且无匹配的待进入请求");
            return;
        }

        if (ShouldNavigateToRoomOnRefresh(roomId)) {
            WindowsManager.Instance.SwitchWindow("room");
            RoomWindowsManager.Instance.SwitchRoomWindow("roomInfo");
        }
        ClearPendingRoomEntry();
        RoomPanel.Instance.GetRoomInfoResponse(
            response.success,
            response.message,
            response.room_info
        );
        UserDataManager.Instance.SetRoomId(roomId);
        AutoReconnect.OnRoomSyncDone();
    }

    private void HandleSyncNotInRoomResponse(Response response) {
        Debug.Log($"房间同步: {response.message}");
        ClearPendingRoomEntry();
        if (AutoReconnect.IsActive && AutoReconnect.ExpectGameRestore) {
            ClearStaleLobbyState();
        } else {
            ApplyLeftRoomState(silent: AutoReconnect.IsActive);
        }
        AutoReconnect.OnRoomSyncDone();
    }

    private bool ShouldNavigateToRoomOnRefresh(string roomId) {
        if (AutoReconnect.IsActive && AutoReconnect.ExpectGameRestore) return false;
        if (WindowsManager.Instance.GetCurrentWindow() == "game") {
            return false;
        }
        string current = WindowsManager.Instance.GetCurrentWindow();
        if (current == "room") return true;
        if (_pendingEnterRoomId == "*") return true;
        if (!string.IsNullOrEmpty(_pendingEnterRoomId) && _pendingEnterRoomId == roomId) return true;
        if (AutoReconnect.IsActive && !AutoReconnect.ExpectGameRestore) return true;
        return false;
    }

    /// <summary>加入/创建失败（error_message）时取消待进入状态，避免后续滞后广播误跳转。</summary>
    public void CancelPendingRoomEntry() {
        if (!string.IsNullOrEmpty(_pendingEnterRoomId)) {
            Debug.Log($"取消待进入房间: {_pendingEnterRoomId}");
        }
        ClearPendingRoomEntry();
    }

    private void ClearPendingRoomEntry() {
        _pendingEnterRoomId = null;
    }

    /// <summary>
    /// 处理加入房间响应
    /// </summary>
    private void HandleJoinRoomResponse(Response response) {
        Debug.Log($"加入房间响应: {response.success}, {response.message}");
        if (response.success) {
            NotificationManager.Instance.ShowTip("join_room", true, "加入房间成功");
            // 房间信息由随后的 refresh_room_info 下发并跳转
        } else {
            ClearPendingRoomEntry();
            NotificationManager.Instance.ShowTip(
                "join_room",
                false,
                string.IsNullOrEmpty(response.message) ? "加入房间失败" : response.message
            );
        }
    }

    /// <summary>
    /// 处理离开房间响应
    /// </summary>
    private void HandleLeaveRoomResponse(Response response) {
        Debug.Log($"离开房间响应: {response.success}, {response.message}");
        if (response.success) {
            ApplyLeftRoomState();
        }
    }

    /// <summary>
    /// 离开/解散房间后重置客户端房间状态与 UI。
    /// </summary>
    public void ApplyLeftRoomState(bool silent = false) {
        ClearPendingRoomEntry();
        ClearStaleLobbyState();
        WindowsManager.Instance.OnLeftRoom();
        RoomWindowsManager.Instance.SwitchRoomWindow("createRoom");
        // 立即刷新列表，避免依赖 5 秒轮询导致基于过期人数误点加入
        GetRoomList(showTipOnSuccess: false);
        if (!silent) {
            NotificationManager.Instance.ShowTip("leave_room", true, "离开房间成功");
        }
    }

    /// <summary>仅清理房间 ID 与 RoomPanel，不切换窗口（对局恢复期间用）。</summary>
    public void ClearStaleLobbyState() {
        UserDataManager.Instance.SetRoomId("");
        RoomPanel.Instance?.ClearRoomState();
    }

    // ========== 房间相关的发送方法 ==========

    private static bool BlockRoomEntryRequest() {
        return LobbyStateGuard.BlockIfInMatchQueueForRoom();
    }

    /// <summary>解析复式主种子；未开启复式时返回空字符串。</summary>
    private static bool TryResolveRandomSeed(string raw, out string seedHex, out string error) {
        seedHex = "";
        error = null;
        if (string.IsNullOrEmpty(raw)) {
            return true;
        }
        return MasterSeedInputValidator.TryNormalizeHex(raw, out seedHex, out error);
    }

    /// <summary>
    /// 创建国标房间
    /// </summary>
    public async void Create_GB_Room(GB_Create_RoomConfig config) {
        if (BlockRoomEntryRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed, out string randomSeed, out string seedError)) {
                NotificationManager.Instance.ShowTip("create_room", false, seedError);
                return;
            }

            var request = new CreateGBRoomRequest {
                type = "room/create_GB_room",
                rule = config.Rule,
                sub_rule = config.SubRule ?? "guobiao/standard",
                roomname = config.RoomName,
                gameround = config.GameRound,
                roundTimerValue = config.RoundTimer,
                stepTimerValue = config.StepTimer,
                tips = config.Tips,
                password = config.Password,
                random_seed = randomSeed,
                open_cuohe = config.CuoHe,
                cuohe_type = config.CuoheType,
                hepai_limit = config.HepaiLimit,
                tourist_limit = config.TouristLimit,
                allow_spectator = config.AllowSpectator,
                tactical_call = config.TacticalCall,
                event_id = string.IsNullOrEmpty(config.EventId) ? null : config.EventId
            };
            Debug.Log($"发送创建房间消息: {config.RoomName}, {config.GameRound}, {config.Password}, {config.SubRule}, {config.RoundTimer}, {config.StepTimer}, {config.Tips}, RandomSeed: {randomSeed}, CuoHe: {config.CuoHe}, CuoheType: {config.CuoheType}, HepaiLimit: {config.HepaiLimit}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            NetworkManager.Instance.CreateRoomResponse.Invoke(false, e.Message);
        }
    }

    /// <summary>
    /// 创建青雀房间（规则字符串使用 qingque13，去掉错和配置，其他与国标类似）
    /// </summary>
    public async void Create_Qingque_Room(Qingque_Create_RoomConfig config) {
        if (BlockRoomEntryRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed, out string randomSeed, out string seedError)) {
                NotificationManager.Instance.ShowTip("create_room", false, seedError);
                return;
            }

            var request = new CreateGBRoomRequest {
                type = "room/create_Qingque_room",
                rule = config.Rule,
                sub_rule = config.SubRule ?? "qingque/standard",
                roomname = config.RoomName,
                gameround = config.GameRound,
                roundTimerValue = config.RoundTimer,
                stepTimerValue = config.StepTimer,
                tips = config.Tips,
                password = config.Password,
                random_seed = randomSeed,
                open_cuohe = false,
                hepai_limit = 1,
                tourist_limit = config.TouristLimit,
                allow_spectator = config.AllowSpectator,
                tactical_call = config.TacticalCall,
                event_id = string.IsNullOrEmpty(config.EventId) ? null : config.EventId
            };
            Debug.Log($"发送创建青雀房间消息: {config.RoomName}, {config.GameRound}, {config.Password}, {config.SubRule}, {config.RoundTimer}, {config.StepTimer}, {config.Tips}, RandomSeed: {randomSeed}, TouristLimit: {config.TouristLimit}, AllowSpectator: {config.AllowSpectator}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            NetworkManager.Instance.CreateRoomResponse.Invoke(false, e.Message);
        }
    }

    /// <summary>Create a fixed first-win Jiandan room using the existing room DTO shape.</summary>
    public async void Create_Jiandan_Room(Jiandan_Create_RoomConfig config) {
        if (BlockRoomEntryRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed, out string randomSeed, out string seedError)) {
                NotificationManager.Instance.ShowTip("create_room", false, seedError);
                return;
            }

            var request = new CreateGBRoomRequest {
                type = "room/create_Jiandan_room",
                rule = "jiandan",
                sub_rule = config.SubRule ?? "jiandan/standard",
                roomname = config.RoomName,
                gameround = config.GameRound,
                roundTimerValue = config.RoundTimer,
                stepTimerValue = config.StepTimer,
                tips = config.Tips,
                password = config.Password,
                random_seed = randomSeed,
                open_cuohe = false,
                hepai_limit = 0,
                tourist_limit = config.TouristLimit,
                allow_spectator = config.AllowSpectator,
                tactical_call = false,
                event_id = string.IsNullOrEmpty(config.EventId) ? null : config.EventId
            };
            Debug.Log($"发送创建简单麻将房间消息: {config.RoomName}, {config.GameRound}, {config.SubRule}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            NetworkManager.Instance.CreateRoomResponse.Invoke(false, e.Message);
        }
    }
    
    /// <summary>
    /// 创建古典麻将房间
    /// </summary>
    public async void Create_Classical_Room(Qingque_Create_RoomConfig config) {
        if (BlockRoomEntryRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed, out string randomSeed, out string seedError)) {
                NotificationManager.Instance.ShowTip("create_room", false, seedError);
                return;
            }

            var request = new CreateGBRoomRequest {
                type = "room/create_Classical_room",
                rule = config.Rule,
                sub_rule = config.SubRule ?? "classical/standard",
                roomname = config.RoomName,
                gameround = config.GameRound,
                roundTimerValue = config.RoundTimer,
                stepTimerValue = config.StepTimer,
                tips = config.Tips,
                password = config.Password,
                random_seed = randomSeed,
                open_cuohe = false,
                hepai_limit = 1,
                tourist_limit = config.TouristLimit,
                allow_spectator = config.AllowSpectator,
                event_id = string.IsNullOrEmpty(config.EventId) ? null : config.EventId
            };
            Debug.Log($"发送创建古典麻将房间消息: {config.RoomName}, {config.GameRound}, {config.SubRule}, {config.RoundTimer}, {config.StepTimer}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            NetworkManager.Instance.CreateRoomResponse.Invoke(false, e.Message);
        }
    }

    /// <summary>
    /// 创建四川麻将（血战到底）房间
    /// </summary>
    public async void Create_Sichuan_Room(Sichuan_Create_RoomConfig config) {
        if (BlockRoomEntryRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed, out string randomSeed, out string seedError)) {
                NotificationManager.Instance.ShowTip("create_room", false, seedError);
                return;
            }

            var request = new CreateSichuanRoomRequest {
                type = "room/create_Sichuan_room",
                rule = config.Rule,
                sub_rule = config.SubRule ?? "sichuan/standard",
                roomname = config.RoomName,
                gameround = config.GameRound,
                roundTimerValue = config.RoundTimer,
                stepTimerValue = config.StepTimer,
                tips = config.Tips,
                password = config.Password,
                random_seed = randomSeed,
                tourist_limit = config.TouristLimit,
                allow_spectator = config.AllowSpectator,
                tactical_call = config.TacticalCall,
                blood_battle = config.BloodBattle,
                event_id = string.IsNullOrEmpty(config.EventId) ? null : config.EventId
            };
            Debug.Log($"发送创建四川麻将房间消息: {config.RoomName}, {config.GameRound}, {config.SubRule}, blood_battle={config.BloodBattle}, tactical_call={config.TacticalCall}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            NetworkManager.Instance.CreateRoomResponse.Invoke(false, e.Message);
        }
    }

    /// <summary>
    /// 创建长沙麻将房间
    /// </summary>
    public async void Create_Changsha_Room(Changsha_Create_RoomConfig config) {
        if (BlockRoomEntryRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed, out string randomSeed, out string seedError)) {
                NotificationManager.Instance.ShowTip("create_room", false, seedError);
                return;
            }

            var request = new CreateChangshaRoomRequest {
                type = "room/create_Changsha_room",
                rule = config.Rule,
                sub_rule = config.SubRule ?? "changsha/classic_double_bird",
                roomname = config.RoomName,
                gameround = config.GameRound,
                roundTimerValue = config.RoundTimer,
                stepTimerValue = config.StepTimer,
                tips = config.Tips,
                password = config.Password,
                random_seed = randomSeed,
                tourist_limit = config.TouristLimit,
                allow_spectator = config.AllowSpectator,
                tactical_call = config.TacticalCall,
                open_kong_replacement_count = config.OpenKongReplacementCount,
                initial_hu_si_xi = config.InitialHuSiXi,
                initial_hu_ban_ban_hu = config.InitialHuBanBanHu,
                initial_hu_que_yi_se = config.InitialHuQueYiSe,
                initial_hu_liu_liu_shun = config.InitialHuLiuLiuShun,
                initial_hu_san_tong = config.InitialHuSanTong,
                bird_count = config.BirdCount,
                dealer_bird = config.DealerBird,
                base_score_no_dealer = config.BaseScoreNoDealer,
                small_hu_score = config.SmallHuScore,
                big_hu_score = config.BigHuScore,
                event_id = string.IsNullOrEmpty(config.EventId) ? null : config.EventId
            };
            Debug.Log($"发送创建长沙麻将房间消息: {config.RoomName}, {config.GameRound}, {config.SubRule}, open_kong={config.OpenKongReplacementCount}, bird_count={config.BirdCount}, dealer_bird={config.DealerBird}, no_dealer_score={config.BaseScoreNoDealer}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            NetworkManager.Instance.CreateRoomResponse.Invoke(false, e.Message);
        }
    }

    /// <summary>
    /// 创建立直麻将房间
    /// </summary>
    public async void Create_Riichi_Room(Riichi_Create_RoomConfig config) {
        if (BlockRoomEntryRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed, out string randomSeed, out string seedError)) {
                NotificationManager.Instance.ShowTip("create_room", false, seedError);
                return;
            }

            var request = new CreateRiichiRoomRequest {
                type = "room/create_Riichi_room",
                rule = config.Rule,
                sub_rule = config.SubRule ?? "riichi/standard",
                roomname = config.RoomName,
                gameround = config.GameRound,
                roundTimerValue = config.RoundTimer,
                stepTimerValue = config.StepTimer,
                tips = config.Tips,
                password = config.Password,
                random_seed = randomSeed,
                open_cuohe = config.CuoHe,
                hepai_limit = config.HepaiLimit,
                red_dora = config.RedDora,
                allow_kuikae = config.AllowKuikae,
                open_xiru = config.OpenXiru,
                open_tobi = config.OpenTobi,
                hepai_way = config.HepaiWay ?? "head_bump",
                tourist_limit = config.TouristLimit,
                allow_spectator = config.AllowSpectator,
                event_id = string.IsNullOrEmpty(config.EventId) ? null : config.EventId
            };
            Debug.Log($"发送创建立直麻将房间消息: {config.RoomName}, {config.GameRound}, {config.SubRule}, cuohe={config.CuoHe}, hepai_limit={config.HepaiLimit}, red_dora={config.RedDora}, allow_kuikae={config.AllowKuikae}, open_xiru={config.OpenXiru}, open_tobi={config.OpenTobi}, hepai_way={config.HepaiWay}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            NetworkManager.Instance.CreateRoomResponse.Invoke(false, e.Message);
        }
    }

    /// <summary>
    /// 获取房间列表。show_tip 会随请求发送，服务端原样回显，客户端据此决定是否显示 tips。
    /// </summary>
    /// <param name="showTipOnSuccess">仅手动点击刷新按钮时为 true，自动刷新和切换菜单时为 false</param>
    public async void GetRoomList(bool showTipOnSuccess = false) {
        try {
            var request = new GetRoomListRequest {
                type = "room/get_room_list",
                show_tip = showTipOnSuccess
            };
            Debug.Log($"发送获取房间列表消息{request.type}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            RoomListPanel.Instance.GetRoomListResponse(false, e.Message, null);
        }
    }

    /// <summary>重连后向服务端查询当前玩家是否在房间内，并同步 RoomPanel。</summary>
    public async void SyncMyRoom() {
        try {
            // 重连同步：接受随后任意权威 refresh_room_info
            _pendingEnterRoomId = "*";
            var request = new SyncMyRoomRequest { type = "room/sync_my_room" };
            Debug.Log("发送房间同步请求");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            ClearPendingRoomEntry();
            Debug.LogError($"发送房间同步请求失败: {e.Message}");
            AutoReconnect.OnRoomSyncDone();
        }
    }

    /// <summary>
    /// 加入房间
    /// </summary>
    public async void JoinRoom(string roomId, string password) {
        if (BlockRoomEntryRequest()) return;
        if (LobbyStateGuard.IsInRoom) {
            NotificationManager.Instance.ShowTip("join_room", false, "请先退出当前房间");
            return;
        }
        _pendingEnterRoomId = roomId;
        var request = new JoinRoomRequest {
            type = "room/join_room",
            room_id = roomId,
            password = password
        };
        Debug.Log($"发送加入房间消息: {roomId}, {password}");
        try {
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            ClearPendingRoomEntry();
            Debug.LogError($"发送加入房间消息失败: {e.Message}");
            NotificationManager.Instance.ShowTip("join_room", false, "加入房间失败");
        }
    }

    /// <summary>
    /// 离开房间
    /// </summary>
    public async void LeaveRoom(string roomId) {
        ClearPendingRoomEntry();
        // 先清本地房间 ID，避免离开完成前的滞后 refresh 或误点加入把用户带回房间页
        if (!string.IsNullOrEmpty(roomId) && roomId != UserDataManager.ROOM_ID_NONE) {
            UserDataManager.Instance.SetRoomId("");
        }
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

    /// <summary>
    /// 添加牌效机器人到房间
    /// </summary>
    public async void AddSmartBotToRoom(string roomId) {
        var request = new AddBotToRoomRequest {
            type = "room/add_smart_bot",
            room_id = roomId
        };
        await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
    }

    /// <summary>
    /// 设置准备状态（非房主玩家可用）
    /// </summary>
    public async void SetReady(string roomId, bool ready) {
        try {
            var request = new SetReadyRequest {
                type = "room/set_ready",
                room_id = roomId,
                ready = ready
            };
            Debug.Log($"发送准备状态消息: roomId={roomId}, ready={ready}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送准备状态请求失败: {e.Message}");
        }
    }

    /// <summary>
    /// 从房间移除玩家（仅房主可用）
    /// </summary>
    public async void KickPlayerFromRoom(string roomId, int targetUserId) {
        try {
            var request = new KickPlayerFromRoomRequest {
                type = "room/kick_player",
                room_id = roomId,
                target_user_id = targetUserId
            };
            Debug.Log($"发送移除玩家消息: roomId={roomId}, targetUserId={targetUserId}");
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"发送移除玩家请求失败: {e.Message}");
            NotificationManager.Instance.ShowTip("kick_player", false, "移除玩家失败");
        }
    }
}
