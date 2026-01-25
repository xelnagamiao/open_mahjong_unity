using UnityEngine;
using UnityEngine.Events;
using System;
using System.Threading.Tasks;
using NativeWebSocket;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Collections;
using System.Net;
using System.Net.Sockets;





[Serializable]
public class GameEvent : UnityEvent<bool, string> {} // 标准通知类

public class NetworkManager : MonoBehaviour {

    public static NetworkManager Instance { get; private set; }
    private WebSocket websocket; // 定义websocket
    private string playerId; // 定义玩家ID
    private bool isConnecting = false; // 定义连接状态
    private Queue<byte[]> messageQueue = new Queue<byte[]>(); // 定义消息队列
    public GameEvent ErrorResponse = new GameEvent(); // 定义错误响应事件
    public GameEvent CreateRoomResponse = new GameEvent(); // 定义创建房间响应事件
    
    // 主线程调度器
    private Queue<Action> mainThreadActions = new Queue<Action>();
    // 服务器统计信息协程控制
    private Coroutine serverStatsCoroutine;
    // 解析后的 WebSocket URL（用于存储 DNS 解析结果）


    // 1.Awake方法用于实例化单例进入DontDestroyOnLoad，并配置WebSocket基础的方法
    private void Awake(){
        if (Instance != null && Instance != this){
            Debug.Log($"Destroying duplicate NetworkManager. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        playerId = System.Guid.NewGuid().ToString(); // 生成一个不同机器唯一的玩家ID
        websocket = new WebSocket($"{ConfigManager.gameUrl}/{playerId}"); // 初始化WebSocket
        
        // 配置WebSocket事件处理器
        websocket.OnMessage += (bytes) => {
            lock(messageQueue) {
                messageQueue.Enqueue(bytes);
            }
        };
        
        websocket.OnOpen += () => {
            Debug.Log("WebSocket连接成功");
            isConnecting = false;
            // 使用主线程调度器执行UI操作
            ExecuteOnMainThread(() => {
                LoginPanel.Instance.ConnectOkText(); // 连接成功
            });
            // 发送发布版版本号
            SendReleaseVersion();
        };
        
        websocket.OnError += (errorMsg) => {
            Debug.LogError($"WebSocket连接失败: {errorMsg}");
            isConnecting = false;
            // 使用主线程调度器执行UI操作
            ExecuteOnMainThread(() => {
                LoginPanel.Instance.ConnectErrorText(errorMsg); 
                // 连接失败
                // NotificationManager.Instance.ShowMessage(
                //     "连接错误",
                //     $"与服务器连接失败：{errorMsg}",
                //     "disconnect"
                // );
            });
        };
        
        websocket.OnClose += (code) => {
            Debug.Log($"WebSocket已关闭: {code}");
            isConnecting = false;
            // 使用主线程调度器执行UI操作
            ExecuteOnMainThread(() => {
                LoginPanel.Instance.ConnectErrorText("连接已关闭"); // 连接关闭
                // 连接断开
                NotificationManager.Instance.ShowMessage(
                    "连接已断开",
                    "与服务器的连接已断开，如正处于一场游戏对局中，重新启动客户端可进行重新连接",
                    "disconnect"
                );
            });
        };
    }

    // 2.Start方法用于连接到服务器
    private async void Start(){
        // 确保网络管理器唯一，并且没有在连接中
        if (Instance == this && !isConnecting){
            isConnecting = true;
            try{
                Debug.Log($"开始连接服务器，当前状态: {websocket.State}");
                await websocket.Connect();
            }
            catch (Exception e){
                Debug.LogError($"连接错误: {e.Message}");
                isConnecting = false;
            }
        }
    }


    // 网络管理器实例在 Update 中处理消息队列
    private void Update(){
        // 非WebGL平台需要调用DispatchMessageQueue来处理WebSocket消息
        #if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
        #endif
        
        // 处理消息队列
        if (messageQueue.Count > 0) {
            byte[] message;
            lock(messageQueue) {
                message = messageQueue.Dequeue();
            }
            Get_Message(message);
        }
        
        // 处理主线程调度器
        if (mainThreadActions.Count > 0) {
            Action action;
            lock(mainThreadActions) {
                action = mainThreadActions.Dequeue();
            }
            action?.Invoke();
        }
    }

    // 主线程调度器方法
    private void ExecuteOnMainThread(Action action){
        lock(mainThreadActions) {
            mainThreadActions.Enqueue(action);
        }
    }

    // 处理登录响应
    private void HandleLoginResponse(Response response){
        if (response.success) {
            WindowsManager.Instance.SwitchWindow("menu");
            // 请求房间列表
            GetRoomList();
            // 设置用户信息
            MeunPanel.Instance.SetUserInfo(
                response.login_info.username,
                response.login_info.userkey,
                response.login_info.user_id
            );
            // 保存用户信息
            if (response.user_settings != null) {
                UserDataManager.Instance.SetUserSettings(
                    response.user_settings.title_id,
                    response.user_settings.profile_image_id,
                    response.user_settings.character_id,
                    response.user_settings.voice_id
                );
            }
            if (response.user_config != null) {
                ConfigManager.Instance.SetUserConfig(response.user_config.volume);
            }
            UserContainer.Instance.ShowUserSettings(response.user_settings);
            // 启动服务器统计信息协程
            StartServerStatsCoroutine();
        }
    }

    // 启动服务器统计信息协程（每30秒获取一次）
    private void StartServerStatsCoroutine() {
        // 如果已有协程在运行，先停止它
        if (serverStatsCoroutine != null) {
            StopCoroutine(serverStatsCoroutine);
        }
        // 立即获取一次统计信息
        GetServerStats();
        // 启动协程每30秒获取一次
        serverStatsCoroutine = StartCoroutine(ServerStatsUpdateCoroutine());
    }

    // 服务器统计信息更新协程
    private IEnumerator ServerStatsUpdateCoroutine() {
        while (true) {
            yield return new WaitForSeconds(30f); // 等待30秒
            GetServerStats();
        }
    }

    // 显示服务器统计信息（简化版，直接使用服务器返回的数据）
    private void DisplayServerStats(ServerStatsInfo stats) {
        if (stats != null) {
            MeunPanel.Instance.DisplayServerStats(stats.online_players, stats.waiting_rooms, stats.playing_rooms);
        }
    }

    // 3.Get_Message方法用于处理服务器返回的消息 
    private void Get_Message(byte[] bytes){
        try{
            string jsonStr = System.Text.Encoding.UTF8.GetString(bytes);
            Debug.Log($"收到服务器消息: {jsonStr}");
            var response = JsonConvert.DeserializeObject<Response>(jsonStr);

            switch (response.type){
                case "login":
                    HandleLoginResponse(response);
                    NotificationManager.Instance.ShowTip("login",true,"登录成功");
                    break;
                case "create_room":
                    CreateRoomResponse.Invoke(response.success, response.message);
                    UserDataManager.Instance.SetRoomId(response.room_info.room_id);
                    NotificationManager.Instance.ShowTip("create_room",true,"创建房间成功");
                    break;
                case "get_room_list": // 获取room_list
                    RoomListPanel.Instance.GetRoomListResponse(response.success, response.message, response.room_list);
                    NotificationManager.Instance.ShowTip("get_room_list",true,"获取房间列表成功");
                    break;
                case "get_server_stats": // 获取服务器统计信息
                    DisplayServerStats(response.server_stats);
                    break;
                case "get_room_info": // 获取room_info 更新房间面板
                    Debug.Log("处理房间信息更新");
                    WindowsManager.Instance.SwitchWindow("room");
                    RoomWindowsManager.Instance.SwitchRoomWindow("roomInfo");
                    RoomPanel.Instance.GetRoomInfoResponse(
                        response.success, 
                        response.message, 
                        response.room_info
                    );
                    UserDataManager.Instance.SetRoomId(response.room_info.room_id);
                    break;
                case "error_message":
                    Debug.Log($"错误消息: {response.message}");
                    ErrorResponse.Invoke(response.success, response.message);
                    NotificationManager.Instance.ShowTip("error_message",false,response.message);
                    break;
                case "leave_room":
                    Debug.Log($"离开房间响应: {response.success}, {response.message}");
                    if (response.success) {
                        UserDataManager.Instance.SetRoomId("");
                        NotificationManager.Instance.ShowTip("leave_room",true,"离开房间成功");
                    }
                    break;
                case "join_room":
                    Debug.Log($"加入房间响应: {response.success}, {response.message}");
                    NotificationManager.Instance.ShowTip("join_room",true,"加入房间成功");
                    break; // 只是打印一下 房间信息服务器从get_room_info中发送过来
                case "switch_seat":
                    Debug.Log($"收到换位消息: {response.message}");
                    GameSceneManager.Instance.HandleSwitchSeat(response.switch_seat_info.current_round);
                    break;
                case "refresh_player_tag_list":
                    Debug.Log($"收到刷新玩家标签列表消息: {response.message}");
                    RefreshPlayerTagListInfo tagInfo = response.refresh_player_tag_list_info;
                    GameSceneManager.Instance.RefreshPlayerTagList(tagInfo.player_to_tag_list);
                    break;
                case "game_start_GB":
                    Debug.Log($"游戏开始: {response.message}");
                    GameSceneManager.Instance.InitializeGame(response.success, response.message, response.game_info);
                    break;
                case "broadcast_hand_action_GB":
                    Debug.Log($"收到手牌轮操作信息: {response.ask_hand_action_info}");
                    AskHandActionGBInfo handresponse = response.ask_hand_action_info;
                    GameSceneManager.Instance.AskHandAction(
                        handresponse.remaining_time,
                        handresponse.player_index,
                        handresponse.remain_tiles,
                        handresponse.action_list
                    );
                    break;
                case "ask_other_action_GB":
                    Debug.Log($"收到询问弃牌后操作消息: {response.ask_other_action_info}");
                    AskOtherActionGBInfo askresponse = response.ask_other_action_info;
                    GameSceneManager.Instance.AskMingPaiAction(
                        askresponse.remaining_time,
                        askresponse.action_list,
                        askresponse.cut_tile
                    );
                    break;
                case "do_action_GB":
                    Debug.Log($"收到执行操作消息: {response.do_action_info}");
                    DoActionInfo doresponse = response.do_action_info;
                    GameSceneManager.Instance.DoAction(
                        doresponse.action_list,
                        doresponse.action_player,
                        doresponse.cut_tile,
                        doresponse.cut_tile_index,
                        doresponse.cut_class,
                        doresponse.deal_tile,
                        doresponse.buhua_tile,
                        doresponse.combination_mask,
                        doresponse.combination_target

                    );
                    break;
                case "show_result_GB":
                    Debug.Log($"收到显示结算结果消息: {response.show_result_info}");
                    ShowResultInfo showresponse = response.show_result_info;
                    GameSceneManager.Instance.ShowResult(
                        showresponse.hepai_player_index,
                        showresponse.player_to_score,
                        showresponse.hu_score,
                        showresponse.hu_fan,
                        showresponse.hu_class,
                        showresponse.hepai_player_hand,
                        showresponse.hepai_player_huapai,
                        showresponse.hepai_player_combination_mask
                    );
                    break;
                case "game_end_GB":
                    Debug.Log($"收到游戏结束消息: {response.game_end_info}");
                    GameEndInfo gameendresponse = response.game_end_info;
                    GameSceneManager.Instance.GameEnd(
                        gameendresponse.game_random_seed,
                        gameendresponse.player_final_data
                    );
                    break;
                case "get_record_list":
                    Debug.Log($"收到游戏记录列表: {response.message}");
                    RecordPanel.Instance.GetRecordListResponse(response.success, response.message, response.record_list);
                    break;
                case "get_player_info":
                    Debug.Log($"收到玩家信息: {response.message}");
                    NotificationManager.Instance.OpenPlayerInfoPanel(response.success, response.message, response.player_info);
                    break;

                case "tips":
                    // 服务端验证失败的提示并重置按钮
                    NotificationManager.Instance.ShowTip("验证", false, response.message);
                    LoginPanel.Instance.ResetLoginButton();
                    break;

                case "message":
                    // 处理服务端发送的消息（版本不匹配、账户被顶替、重连提示等）
                    // 传入 title, content 以及 message 标识符 (error_version/login_kickout/reconnect_ask)
                    MessagePrefab messageInstance = NotificationManager.Instance.ShowMessage(
                        response.message_info.title, 
                        response.message_info.content, 
                        response.message
                    );

                    LoginPanel.Instance.ResetLoginButton();
                    break;
                
                default:
                    NotificationManager.Instance.ShowTip("未知的消息类型", false,"未知的消息类型");
                    throw new Exception($"未知的消息类型: {response.type}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"消息处理错误: {e.Message}\n{e.StackTrace}");  // 添加堆栈信息
        }
    }
    

    // 发送发布版版本号
    public async void SendReleaseVersion(){
        try {
            var request = new SendReleaseVersionRequest {
                type = "send_release_version",
                release_version = ConfigManager.releaseVersion
            };
            Debug.Log($"发送发布版本号: {request.release_version}");
            await websocket.SendText(JsonConvert.SerializeObject(request));
        }
        catch (Exception e)
        {
            Debug.LogError($"发送版本号失败: {e.Message}");
            NotificationManager.Instance.ShowTip("send_release_version",false,"发送版本号失败,请检查网络连接");
        }
    }

    // 4.以下是所有定义的消息发送类型 客户端所有消息发送都通过以下列表
    // 4.1 登录方法 login 从LoginPanel发送
    public async void Login(string username, string password, bool is_tourist = false){
        try {
            if (websocket.State != WebSocketState.Open) {
                NotificationManager.Instance.ShowTip("登录", false, "尚未连接至OMU服务器");
                LoginPanel.Instance.ResetLoginButton();
                return;
            }
            // 如果网络连接成功，则发送登录消息
            var request = new LoginRequest {
                type = "login",
                username = is_tourist ? null : username,  // 游客登录时username为null
                password = is_tourist ? null : password,  // 游客登录时password为null
                is_tourist = is_tourist
            };
            Debug.Log($"发送登录消息: username={(is_tourist ? "null" : username)}, password={(is_tourist ? "null" : "***")}, is_tourist={is_tourist}");
            await websocket.SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogError($"登录发送错误: {e.Message}");
            NotificationManager.Instance.ShowTip("登录", false, "尚未连接至OMU服务器");
            LoginPanel.Instance.ResetLoginButton();
        }
    }

    // 4.2 创建房间方法 CreateRoom 从CreatePanel发送
    public async void Create_GB_Room(GB_Create_RoomConfig config){
        try {
            // 将字符串格式的随机种子转换为整数
            int randomSeed = 0;
            if (!string.IsNullOrEmpty(config.RandomSeed)) {
                if (!int.TryParse(config.RandomSeed, out randomSeed)) {
                    randomSeed = 0;
                }
            }
            
            var request = new CreateGBRoomRequest {
                type = "create_GB_room",
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
            await websocket.SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            CreateRoomResponse.Invoke(false, e.Message);
        }
    }   
    // 4.3 获取房间列表方法 get_room_list 从RoomPanel发送
    public async void GetRoomList(){
        try {
            var request = new GetRoomListRequest {
                type = "get_room_list"
            };
            Debug.Log($"发送获取房间列表消息{request.type}");
            await websocket.SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            RoomListPanel.Instance.GetRoomListResponse(false, e.Message, null);
        }
    }
    // 4.4 加入房间方法 JoinRoom 从RoomItem发送
    public async void JoinRoom(string roomId, string password) {
        var request = new JoinRoomRequest {
            type = "join_room",
            room_id = roomId,
            password = password
        };
        Debug.Log($"发送加入房间消息: {roomId}, {password}");
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }
    // 4.5 离开房间方法 LeaveRoom 从RoomPanel发送
    public async void LeaveRoom(string roomId){
        var request = new LeaveRoomRequest {
            type = "leave_room",
            room_id = roomId
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }
    // 4.6 开始游戏方法 StartGame 从RoomPanel发送
    public async void StartGame(string roomId){
        var request = new StartGameRequest {
            type = "start_game",
            room_id = roomId
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }
    // GameScene Case
    // 4.7 发送国标卡牌方法 SendChineseGameTile 从GameScene与其下属 发送    
    public async void SendChineseGameTile(bool cutClass,int tileId,int cutIndex,string roomId){
        var request = new SendChineseGameTileRequest {
            type = "CutTiles",
            cutClass = cutClass,
            TileId = tileId,
            cutIndex = cutIndex,
            room_id = roomId
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }
    // 4.8 发送吃碰杠回应
    public async void SendAction(string action,int targetTile) {
        var request = new SendActionRequest {
            type = "send_action",
            room_id = UserDataManager.Instance.RoomId,
            action = action,
            targetTile = targetTile
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }

    // 4.9 获取游戏记录方法 GetRecordList 从mainpanel调用发送
    public async void GetRecordList() {
        var request = new GetRecordListRequest {
            type = "get_record_list"
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }

    // 4.10 查询玩家信息方法 GetPlayerInfo 从PlayerPanel发送
    public async void GetPlayerInfo(string userid) {
        var request = new GetPlayerInfoRequest {
            type = "get_player_info",
            userid = userid
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }

    // 4.11 游客登录方法 TouristLogin 从LoginPanel发送
    public void TouristLogin() {
        try {
            if (websocket.State != WebSocketState.Open) {
                NotificationManager.Instance.ShowTip("登录", false, "尚未连接至OMU服务器");
                return;
            }
            // 游客登录 不传递用户名和密码
            Login("", "", is_tourist: true);
        }
        catch (Exception)
        {
            NotificationManager.Instance.ShowTip("登录", false, "尚未连接至OMU服务器");
            LoginPanel.Instance.ResetLoginButton();
        }
    }

    // 4.12 获取服务器统计信息方法 GetServerStats
    public async void GetServerStats() {
        try {
            if (websocket.State != WebSocketState.Open) {
                return; // 连接未建立时不发送请求
            }
            var request = new GetServerStatsRequest
            {
                type = "get_server_stats"
            };
            await websocket.SendText(JsonConvert.SerializeObject(request));
        }
        catch (Exception e)
        {
            Debug.LogError($"获取服务器统计信息失败: {e.Message}");
        }
    }

    // 4.13 放弃重连方法 GiveUpReconnect
    public async void ReconnectResponse(bool reconnect) {
        var request = new ReconnectRequest {
            type = "reconnect_response",
            reconnect = reconnect
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }


    private async void OnApplicationQuit() {
        // 停止服务器统计信息协程
        if (serverStatsCoroutine != null) {
            StopCoroutine(serverStatsCoroutine);
            serverStatsCoroutine = null;
        }
        if (websocket != null && websocket.State == WebSocketState.Open)
            await websocket.Close();
    }
    // 4.14 添加机器人方法 AddBotToRoom 从RoomPanel发送
    public async void AddBotToRoom(string roomId){
        var request = new AddBotToRoomRequest {
            type = "add_bot_to_room",
            room_id = roomId
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }
}
