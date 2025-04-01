using UnityEngine;
using UnityEngine.Events;
using System;
using System.Threading.Tasks;
using WebSocketSharp;
using System.Collections.Generic;




[Serializable]
public class GameEvent : UnityEvent<bool, string> {} // 标准通知类
public class GetRoomListEvent : UnityEvent<bool, string, RoomData[]> {} // 获取房间列表类
public class GetRoomInfoEvent : UnityEvent<bool, string, RoomInfo> {} // 获取房间信息类
public class GameStartEvent : UnityEvent<bool, string, GameInfo> {} // 游戏开始类

public class NetworkManager : MonoBehaviour
{

    public static NetworkManager Instance { get; private set; }
    private WebSocket websocket; // 定义websocket
    private string playerId; // 定义玩家ID
    private bool isConnecting = false; // 定义连接状态
    private Queue<byte[]> messageQueue = new Queue<byte[]>(); // 定义消息队列
    public GameEvent ErrorResponse = new GameEvent(); // 定义错误响应事件
    public GameEvent LoginResponse = new GameEvent(); // 定义登录响应事件
    public GameEvent CreateRoomResponse = new GameEvent(); // 定义创建房间响应事件
    public GetRoomListEvent GetRoomListResponse = new GetRoomListEvent(); // 定义获取房间列表响应事件
    public GetRoomInfoEvent GetRoomInfoResponse = new GetRoomInfoEvent(); // 定义获取房间信息响应事件
    public GameStartEvent GameStartResponse = new GameStartEvent(); // 定义游戏开始响应事件


    // 1.Awake方法用于实例化单例进入DontDestroyOnLoad，并配置WebSocket基础的方法
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.Log($"Destroying duplicate NetworkManager. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        playerId = System.Guid.NewGuid().ToString(); // 生成一个不同机器唯一的玩家ID
        websocket = new WebSocket($"ws://localhost:8081/game/{playerId}"); // 初始化WebSocket
        // websocket收到消息将消息放入update的消息队列
        websocket.OnMessage += (sender, e) => {
            lock(messageQueue) {
                messageQueue.Enqueue(e.RawData);
            }
        };
        websocket.OnOpen += (sender, e) => Debug.Log("WebSocket连接已打开"); // 定义websocket打开以后的通知
        websocket.OnError += (sender, e) => Debug.LogError($"WebSocket错误: {e.Message}"); // 定义websocket错误以后的通知
        websocket.OnClose += (sender, e) => Debug.Log($"WebSocket已关闭: {e.Code}"); // 定义websocket关闭以后的通知
    }
    // 2.Start方法用于连接到服务器
    private void Start()
    {
        // 确保网络管理器唯一，并且没有在连接中
        if (Instance == this && !isConnecting)
        {
            isConnecting = true;
            try
            {
                Debug.Log($"开始连接服务器，当前状态: {websocket.ReadyState}");
                websocket.Connect();
                Debug.Log($"连接完成，当前状态: {websocket.ReadyState}");
            }
            catch (Exception e)
            {
                Debug.LogError($"连接错误: {e.Message}");
            }
            finally
            {
                isConnecting = false; // 连接失败，设置连接状态为false
            }
        }
    }

    // 网络管理器实例在 Update 中处理消息队列
    private void Update()
    {
        if (messageQueue.Count > 0)
        {
            byte[] message;
            lock(messageQueue)
            {
                message = messageQueue.Dequeue();
            }
            Get_Message(message);
        }
    }

    // 3.Get_Message方法用于处理服务器返回的消息 
    private void Get_Message(byte[] bytes)
    {
        try
        {
            string jsonStr = System.Text.Encoding.UTF8.GetString(bytes);
            Debug.Log($"收到服务器消息: {jsonStr}");
            var response = JsonUtility.FromJson<Response>(jsonStr);

            switch (response.type)
            {
                case "login":
                    if (response.success)
                    {
                        Administrator.Instance.SetUserInfo(response.username);
                    }
                    LoginResponse.Invoke(response.success, response.message);
                    break;
                case "create_room":
                    CreateRoomResponse.Invoke(response.success, response.message);
                    Administrator.Instance.SetRoomId(response.room_info.room_id);
                    break;
                case "get_room_list": // 获取room_list
                    GetRoomListResponse.Invoke(response.success, response.message, response.room_list);
                    break;
                case "get_room_info": // 获取room_info
                    Debug.Log("处理房间信息更新");
                    WindowsMannager.Instance.GetWindowsSwitchResponse.Invoke("room");
                    GetRoomInfoResponse.Invoke(
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
                        WindowsMannager.Instance.GetWindowsSwitchResponse.Invoke("roomList");
                    }
                    break;
                case "game_start_chinese":
                    Debug.Log($"游戏开始: {response.message}");
                    GameStartResponse.Invoke(
                        response.success, 
                        response.message, 
                        response.game_info
                    );
                    break;
                case "cut_tiles_chinese":
                    Debug.Log($"收到切牌消息: {response.cut_info}");
                    CutTileResponse cutresponse = response.cut_info;
                    GameSceneMannager.Instance.CutCards(
                        cutresponse.cut_tiles,
                        cutresponse.cut_player_index,
                        cutresponse.cut_class
                        );
                    break;
                case "deal_tile_chinese":
                    Debug.Log($"收到发牌消息: {response.deal_tile_info}");
                    DealTileInfo dealresponse = response.deal_tile_info;
                    GameSceneMannager.Instance.GetCards(
                        dealresponse.remaining_time,
                        dealresponse.deal_tiles,
                        dealresponse.deal_player_index,
                        dealresponse.remain_tiles
                    );
                    break;
                case "ask_action_chinese":
                    Debug.Log($"收到询问操作消息: {response.ask_action_info}");
                    AskActionInfo askresponse = response.ask_action_info;
                    GameSceneMannager.Instance.AskAction(
                        askresponse.remaining_time,
                        askresponse.action_list,
                        askresponse.cut_tile
                    );
                    break;
                case "do_action_chinese":
                    Debug.Log($"收到执行操作消息: {response.action_info}");
                    GameSceneMannager.Instance.DoAction(
                        response.action_info.action_type,
                        response.action_info.remaining_time,
                        response.action_info.player_index
                    );
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
    public void Login(string username, string password)
    {
        try
        {
            if (websocket.ReadyState != WebSocketState.Open)
            {
                throw new Exception("网络未连接");
            }
            // 3.2 如果网络连接成功，则发送登录消息
            var request = new LoginRequest
            {
                type = "login",
                username = username,
                password = password
            };
            Debug.Log($"发送登录消息: {username}, {password}");
            websocket.Send(JsonUtility.ToJson(request));
        }
        catch (Exception e)
        {
            LoginResponse.Invoke(false, e.Message);
        }
    }
    // 4.2 创建房间方法 CreateRoom 从CreatePanel发送
    public void CreateRoom(string roomname, int gametime, string password)
    {
        try
        {
            var request = new CreateRoomRequest
            {
                type = "create_room",
                roomname = roomname,
                gametime = gametime,
                cuttime = 5,
                password = password
            };
            Debug.Log($"发送创建房间消息: {roomname}, {gametime}, {password}");
            websocket.Send(JsonUtility.ToJson(request));
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
            websocket.Send(JsonUtility.ToJson(request));
        }
        catch (Exception e)
        {
            GetRoomListResponse.Invoke(false, e.Message, null);
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
        websocket.Send(JsonUtility.ToJson(request));
    }
    // 4.5 离开房间方法 LeaveRoom 从RoomPanel发送
    public void LeaveRoom(string roomId)
    {
        var request = new LeaveRoomRequest
        {
            type = "leave_room",
            room_id = roomId
        };
        websocket.Send(JsonUtility.ToJson(request));
    }
    // 4.6 开始游戏方法 StartGame 从RoomPanel发送
    public void StartGame(string roomId)
    {
        var request = new StartGameRequest
        {
            type = "start_game",
            room_id = roomId
        };
        websocket.Send(JsonUtility.ToJson(request));
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
        websocket.Send(JsonUtility.ToJson(request));
    }
    // 4.8 发送吃碰杠回应
    public void SendAction(string action)
    {
        var request = new SendActionRequest
        {
            type = "send_action",
            room_id = Administrator.Instance.room_id,
            action = action
        };
        websocket.Send(JsonUtility.ToJson(request));
    }






    private void OnApplicationQuit()
    {
        if (websocket != null && websocket.ReadyState == WebSocketState.Open)
            websocket.Close();
    }
}

// 5.Response 接收数据类型
[Serializable]
public class RoomData // 刷新房间列表数据类型 通过get_room_list更新 标识了 had_password：bool
{
    public string room_id;
    public string host_name;
    public string room_name;
    public int game_time;
    public string[] player_list;
    public int player_count;
    public bool has_password;
}

[Serializable]
public class RoomInfo // 房间信息数据类型 通过get_room_info更新
{
    public string[] player_list;
    public int player_count;
    public string host_name;
    public int game_time;
    public string room_id;
    public string room_name;
}

[Serializable]
public class Response 
{
    public string type;
    public bool success;
    public string message;
    public string username;
    public RoomData[] room_list;
    public RoomInfo room_info;
    public GameInfo game_info;
    public CutTileResponse cut_info;
    public DealTileInfo deal_tile_info;
    public AskActionInfo ask_action_info;
    public ActionInfo action_info;
}

[Serializable]
public class CutTileResponse
{
    public int cut_player_index;
    public bool cut_class;
    public int cut_tiles; 
}

[Serializable]
public class DealTileInfo
{
    public int deal_player_index;
    public int deal_tiles;
    public int remaining_time;
    public int remain_tiles;
}

[Serializable]
public class AskActionInfo
{
    public int remaining_time;
    public string[] action_list;
    public int cut_tile;
}

public class ActionInfo
{
    public string action_type;
    public int remaining_time;
    public int player_index;
}

// 6.Request 发送数据类型 ##################################################################
[Serializable]
public class LoginRequest
{
    public string type;
    public string username;
    public string password;
}

[Serializable]
public class CreateRoomRequest
{
    public string type;
    public string roomname;
    public int gametime;
    public int cuttime;
    public string password;
}

[Serializable]
public class GetRoomListRequest
{
    public string type;
}

[Serializable]
public class JoinRoomRequest
{
    public string type;
    public string room_id;
    public string password;
}

[Serializable]
public class LeaveRoomRequest
{
    public string type;
    public string room_id;
}

[Serializable]
public class StartGameRequest
{
    public string type;
    public string room_id;
}

[Serializable]
public class PlayerInfo
{
    public string username;             // 玩家名
    public int hand_tiles_count;        // 手牌数量
    public string[] discard_tiles;      // 弃牌
    public string[] combination_tiles;   // 组合牌
    public int remaining_time;          // 剩余时间
    public int current_player_index;    // 东南西北位置
    public int score;                   // 得分
}

[Serializable]
public class PlayerPosition  // 新增类来替代 Dictionary
{
    public string username;
    public int position;
}

[Serializable]
public class GameInfo
{
    public string[] player_list;
    public PlayerPosition[] player_positions;  // 改用数组
    public int current_player_index;
    public int tile_count;
    public int random_seed;
    public string game_status;
    public int current_round;
    public int cuttime;
    public int game_time;
    public PlayerInfo[] players_info;
    public int[] self_hand_tiles;
}

[Serializable]
public class SendChineseGameTileRequest
{
    public string type;
    public bool cutClass;
    public int TileId;
    public string room_id;
}



[Serializable]
public class SendActionRequest
{
    public string type;
    public string action;
    public string room_id;
}
