using UnityEngine;
using UnityEngine.Events;
using System;
using System.Threading.Tasks;
using WebSocketSharp;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Collections;





[Serializable]
public class GameEvent : UnityEvent<bool, string> {} // 标准通知类

public class NetworkManager : MonoBehaviour
{

    public static NetworkManager Instance { get; private set; }
    private WebSocket websocket; // 定义websocket
    private string playerId; // 定义玩家ID
    private bool isConnecting = false; // 定义连接状态
    private Queue<byte[]> messageQueue = new Queue<byte[]>(); // 定义消息队列
    private Action<bool, string> currentLoginCallback; // 登录回调
    public GameEvent ErrorResponse = new GameEvent(); // 定义错误响应事件
    public GameEvent CreateRoomResponse = new GameEvent(); // 定义创建房间响应事件
    
    // 主线程调度器
    private Queue<Action> mainThreadActions = new Queue<Action>();


    // 1.Awake方法用于实例化单例进入DontDestroyOnLoad，并配置WebSocket基础的方法
    private void Awake(){
        if (Instance != null && Instance != this){
            Debug.Log($"Destroying duplicate NetworkManager. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        playerId = System.Guid.NewGuid().ToString(); // 生成一个不同机器唯一的玩家ID
        websocket = new WebSocket($"ws://localhost:8081/game/{playerId}"); // 初始化WebSocket
        
        // 配置WebSocket事件处理器
        websocket.OnMessage += OnWebSocketMessage;
        websocket.OnOpen += OnWebSocketOpen;
        websocket.OnError += OnWebSocketError;
        websocket.OnClose += OnWebSocketClose;
    }

    // 2.Start方法用于连接到服务器
    private void Start(){
        // 确保网络管理器唯一，并且没有在连接中
        if (Instance == this && !isConnecting){
            isConnecting = true;
            try{
                Debug.Log($"开始连接服务器，当前状态: {websocket.ReadyState}");
                websocket.ConnectAsync();

            }
            catch (Exception e){
                Debug.LogError($"连接错误: {e.Message}");
                isConnecting = false;
            }
        }
    }

    // 获取数据添加入消息队列
    private void OnWebSocketMessage(object sender, MessageEventArgs e){
        lock(messageQueue) {
            messageQueue.Enqueue(e.RawData);
        }
    }

    // 连接成功
    private void OnWebSocketOpen(object sender, EventArgs e){
        Debug.Log("WebSocket连接成功");
        isConnecting = false;
        // 使用主线程调度器执行UI操作
        ExecuteOnMainThread(() => {
            LoginPanel.Instance.ConnectOkText(); // 连接成功
        });
    } 

    // 连接失败
    private void OnWebSocketError(object sender, ErrorEventArgs e){
        Debug.LogError($"WebSocket连接失败: {e.Message}");
        isConnecting = false;
        // 使用主线程调度器执行UI操作
        ExecuteOnMainThread(() => {
            LoginPanel.Instance.ConnectErrorText(e.Message); // 连接失败
        });
    }

    // 连接关闭
    private void OnWebSocketClose(object sender, CloseEventArgs e){
        Debug.Log($"WebSocket已关闭: {e.Code} - {e.Reason}");
        isConnecting = false;
        // 使用主线程调度器执行UI操作
        ExecuteOnMainThread(() => {
            LoginPanel.Instance.ConnectErrorText(e.Reason); // 连接关闭
        });
    }

    // 网络管理器实例在 Update 中处理消息队列
    private void Update()
    {
        // 处理消息队列
        if (messageQueue.Count > 0)
        {
            byte[] message;
            lock(messageQueue)
            {
                message = messageQueue.Dequeue();
            }
            Get_Message(message);
        }
        
        // 处理主线程调度器
        if (mainThreadActions.Count > 0)
        {
            Action action;
            lock(mainThreadActions)
            {
                action = mainThreadActions.Dequeue();
            }
            action?.Invoke();
        }
    }

    // 主线程调度器方法
    private void ExecuteOnMainThread(Action action)
    {
        lock(mainThreadActions)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    // 主线程协程执行器（备用方案）
    private IEnumerator ExecuteOnMainThreadCoroutine(Action action)
    {
        // 等待一帧，确保在主线程中执行
        yield return null;
        action?.Invoke();
    }

    // 3.Get_Message方法用于处理服务器返回的消息 
    private void Get_Message(byte[] bytes){
        try{
            string jsonStr = System.Text.Encoding.UTF8.GetString(bytes);
            Debug.Log($"收到服务器消息: {jsonStr}");
            var response = JsonConvert.DeserializeObject<Response>(jsonStr);

            switch (response.type){
                case "login":
                    if (response.success){
                        Administrator.Instance.SetUserInfo(response.username,response.userkey);
                    }
                    currentLoginCallback?.Invoke(response.success, response.message);
                    currentLoginCallback = null; // 清除回调
                    break;
                case "create_room":
                    CreateRoomResponse.Invoke(response.success, response.message);
                    Administrator.Instance.SetRoomId(response.room_info.room_id);
                    break;
                case "get_room_list": // 获取room_list
                    RoomListPanel.Instance.GetRoomListResponse(response.success, response.message, response.room_list);
                    break;
                case "get_room_info": // 获取room_info 更新房间面板
                    Debug.Log("处理房间信息更新");
                    WindowsManager.Instance.SwitchWindow("room");
                    RoomPanel.Instance.GetRoomInfoResponse(
                        response.success, 
                        response.message, 
                        response.room_info
                    );
                    Administrator.Instance.SetRoomId(response.room_info.room_id);
                    break;
                case "error_message":
                    Debug.Log($"错误消息: {response.message}");
                    ErrorResponse.Invoke(response.success, response.message);
                    break;
                case "leave_room":
                    Debug.Log($"离开房间响应: {response.success}, {response.message}");
                    if (response.success)
                    {
                        WindowsManager.Instance.SwitchWindow("roomList");
                        Administrator.Instance.SetRoomId("");
                    }
                    break;
                case "join_room":
                    Debug.Log($"加入房间响应: {response.success}, {response.message}");
                    break; // 只是打印一下 房间信息服务器从get_room_info中发送过来
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
                    GameSceneManager.Instance.AskOtherAction(
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
                    break;
                
                default:
                    throw new Exception($"未知的消息类型: {response.type}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"消息处理错误: {e.Message}\n{e.StackTrace}");  // 添加堆栈信息
        }
    }
    
    // 4.以下是所有定义的消息发送类型 客户端所有消息发送都通过以下列表
    // 4.1 登录方法 login 从LoginPanel发送
    public void Login(string username, string password, Action<bool, string> callback)
    {
        try
        {
            if (websocket.ReadyState != WebSocketState.Open)
            {
                callback?.Invoke(false, "网络未连接"); // 直接通过回调报告错误
                return;
            }
            currentLoginCallback = callback; // 存储回调
            // 3.2 如果网络连接成功，则发送登录消息
            var request = new LoginRequest
            {
                type = "login",
                username = username,
                password = password
            };
            Debug.Log($"发送登录消息: {username}, {password}");
            websocket.Send(JsonConvert.SerializeObject(request));
        }
        catch (Exception e)
        {
            callback?.Invoke(false, e.Message); // 通过回调报告错误
            currentLoginCallback = null; // 清除回调
        }
    }

    // 4.2 创建房间方法 CreateRoom 从CreatePanel发送
    public void Create_GB_Room(GB_Create_RoomConfig config)
    {
        try
        {
            var request = new CreateGBRoomRequest
            {
                type = "create_GB_room",
                rule = config.Rule,
                roomname = config.RoomName,
                gameround = config.GameRound,
                roundTimerValue = config.RoundTimer,
                stepTimerValue = config.StepTimer,
                tips = config.Tips,
                password = config.Password
            };
            Debug.Log($"发送创建房间消息: {config.RoomName}, {config.GameRound}, {config.Password}, {config.Rule}, {config.RoundTimer}, {config.StepTimer}, {config.Tips}");
            websocket.Send(JsonConvert.SerializeObject(request));
        }
        catch (Exception e)
        {
            CreateRoomResponse.Invoke(false, e.Message);
        }
    }   
    // 4.3 获取房间列表方法 get_room_list 从RoomPanel发送
    public void GetRoomList()
    {
        try
        {
            var request = new GetRoomListRequest
            {
                type = "get_room_list"
            };
            Debug.Log($"发送获取房间列表消息{request.type}");
            websocket.Send(JsonConvert.SerializeObject(request));
        }
        catch (Exception e)
        {
            RoomListPanel.Instance.GetRoomListResponse(false, e.Message, null);
        }
    }
    // 4.4 加入房间方法 JoinRoom 从RoomItem发送
    public void JoinRoom(string roomId, string password)
    {
        var request = new JoinRoomRequest
        {
            type = "join_room",
            room_id = roomId,
            password = password
        };
        Debug.Log($"发送加入房间消息: {roomId}, {password}");
        websocket.Send(JsonConvert.SerializeObject(request));
    }
    // 4.5 离开房间方法 LeaveRoom 从RoomPanel发送
    public void LeaveRoom(string roomId)
    {
        var request = new LeaveRoomRequest
        {
            type = "leave_room",
            room_id = roomId
        };
        websocket.Send(JsonConvert.SerializeObject(request));
    }
    // 4.6 开始游戏方法 StartGame 从RoomPanel发送
    public void StartGame(string roomId)
    {
        var request = new StartGameRequest
        {
            type = "start_game",
            room_id = roomId
        };
        websocket.Send(JsonConvert.SerializeObject(request));
    }
    // GameScene Case
    // 4.7 发送国标卡牌方法 SendChineseGameTile 从GameScene与其下属 发送    
    public void SendChineseGameTile(bool cutClass,int tileId,string roomId){
        var request = new SendChineseGameTileRequest
        {
            type = "CutTiles",
            cutClass = cutClass,
            TileId = tileId,
            room_id = roomId
        };
        websocket.Send(JsonConvert.SerializeObject(request));
    }
    // 4.8 发送吃碰杠回应
    public void SendAction(string action,int targetTile)
    {
        var request = new SendActionRequest
        {
            type = "send_action",
            room_id = Administrator.Instance.room_id,
            action = action,
            targetTile = targetTile
        };
        websocket.Send(JsonConvert.SerializeObject(request));
    }






    private void OnApplicationQuit()
    {
        if (websocket != null && websocket.ReadyState == WebSocketState.Open)
            websocket.Close();
    }
}
