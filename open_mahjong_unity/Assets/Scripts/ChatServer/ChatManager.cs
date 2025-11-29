using UnityEngine;
using UnityEngine.Events;
using System;
using UnityEngine.UI;
using WebSocketSharp;
using TMPro;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

public class ChatManager : MonoBehaviour
{
    // ui组件
    [SerializeField] private Button SendButton;
    [SerializeField] private TMP_InputField MessageInputField;
    [SerializeField] private TMP_Dropdown SwitchSendTarget;
    [SerializeField] private GameObject ChatTextPrefab;
    [SerializeField] private GameObject ChatTextContainer;

    public static ChatManager Instance { get; private set; }
    private bool isConnecting = false; // 定义连接状态
    private WebSocket websocket;
    private string playerId;

    // 监听按钮事件
    public void OnSendButtonClick(){SendChatMessage(MessageInputField.text);}

    private Queue<byte[]> ConcurrentQueue = new Queue<byte[]>(); // 定义消息队列

    // 初始化连接
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.Log($"Destroying duplicate NetworkManager. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        playerId = System.Guid.NewGuid().ToString(); // 生成一个不同机器唯一的玩家ID
        websocket = new WebSocket($"ws://localhost:8083/chat/{playerId}"); // 初始化WebSocket
        websocket.OnOpen += (sender, e) => Debug.Log("WebSocket To ChatServer连接已打开");
        websocket.OnMessage += (sender, e) => {
            lock(ConcurrentQueue) {
                ConcurrentQueue.Enqueue(e.RawData);
            }
        };
        websocket.OnError += (sender, e) => Debug.LogError($"WebSocket To ChatServer错误: {e.Message}");
        websocket.OnClose += (sender, e) => Debug.Log($"WebSocket To ChatServer已关闭: {e.Code}");
        
        // 绑定按钮事件
        SendButton.onClick.AddListener(OnSendButtonClick);
    }

    // 连接到聊天服务器
    private void Start()
    {
        // 确保网络管理器唯一，并且没有在连接中
        if (Instance == this && !isConnecting)
        {
            isConnecting = true;
            try
            {
                Debug.Log($"开始连接聊天服务器，当前状态: {websocket.ReadyState}");
                websocket.ConnectAsync();
                Debug.Log($"连接聊天服务器完成，当前状态: {websocket.ReadyState}");
            }
            catch (Exception e)
            {
                Debug.LogError($"连接聊天服务器错误: {e.Message}");
            }
            finally
            {
                isConnecting = false; // 连接失败，设置连接状态为false
            }
        }
    }

    // 收到聊天服务器消息
    private void GetMessage(byte[] bytes)
    {
        try
        {
            string jsonStr = System.Text.Encoding.UTF8.GetString(bytes);
            Debug.Log($"收到聊天服务器消息: {jsonStr}");
            GetChatMessage(jsonStr);
        }
        catch (Exception e)
        {
            Debug.LogError($"解析聊天服务器消息错误: {e.Message}");
        }
    }

    // 检测消息队列
    private void Update()
    {
        if (ConcurrentQueue.Count > 0)
        {
            byte[] messageBytes;
            lock(ConcurrentQueue)
            {
                messageBytes = ConcurrentQueue.Dequeue();
            }
            GetMessage(messageBytes);
        }
    }

    // 处理聊天消息
    public void GetChatMessage(string message){
        // 解析消息
        ChatResponse response = JsonConvert.DeserializeObject<ChatResponse>(message);
        DisplayChatMessage(response);
    }

    // 显示聊天消息
    private void DisplayChatMessage(ChatResponse response){
        // 根据responseType 创建不同的聊天消息对象
        if (response.responseType == "Chat")
        {
            // 创建聊天消息对象ChatTextObject 进入 ChatTextContainer 
            // 文字白色 表示聊天消息
            GameObject ChatText = Instantiate(ChatTextPrefab, ChatTextContainer.transform);
            if (response.roomId == 0){
                // [大厅]:用户1001:helloworld
                ChatText.GetComponent<TextMeshProUGUI>().text = $"[大厅]: {response.content}";
            }
            else{
                // [房间3102]用户1001:helloworld
                ChatText.GetComponent<TextMeshProUGUI>().text = $"[房间{response.roomId}]: {response.content}";
            }
            ChatText.GetComponent<TextMeshProUGUI>().color = Color.white;
        }
        else if (response.responseType == "False")
        {
            // 文字红色 表示错误信息
            GameObject ChatText = Instantiate(ChatTextPrefab, ChatTextContainer.transform);
            ChatText.GetComponent<TextMeshProUGUI>().text = response.content;
            ChatText.GetComponent<TextMeshProUGUI>().color = Color.red;
        }
        else if (response.responseType == "Tips")
        {
            // 文字蓝色 表示进出房间
            GameObject ChatText = Instantiate(ChatTextPrefab, ChatTextContainer.transform);
            ChatText.GetComponent<TextMeshProUGUI>().text = response.content;
            ChatText.GetComponent<TextMeshProUGUI>().color = Color.blue;
        }
        else if (response.responseType == "Sercet") // 未启用
        {
            // 文字绿色 表示私聊信息
            GameObject ChatText = Instantiate(ChatTextPrefab, ChatTextContainer.transform);
            ChatText.GetComponent<TextMeshProUGUI>().text = response.content;
            ChatText.GetComponent<TextMeshProUGUI>().color = Color.green;
        }
        else if (response.responseType == "sendChatOk")
        {
            // 预留解决消息发送失败显示的需求 如果消息发送成功这里会收到sendChatOk消息
            // 目前服务器会接收到客户端发送的广播消息，然后重新广播回客户端，客户端会略有延迟的看到自己发送的消息
        }
        else
        {
            // 文字红色 表示未知信息
            GameObject ChatText = Instantiate(ChatTextPrefab, ChatTextContainer.transform);
            ChatText.GetComponent<TextMeshProUGUI>().text = response.content;
            ChatText.GetComponent<TextMeshProUGUI>().color = Color.red;
        }
        ScrollChatToBottom(); // 将聊天窗口滚动到底部
    }

    // 登录聊天服务器
    public void LoginChatServer(string username, string userkey)
    {
        Debug.Log($"开始登录聊天服务器，玩家ID: {playerId}, 用户名: {username}, 用户密钥: {userkey}");
        
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open)
        {
            Debug.Log("WebSocket连接未建立，无法发送登录消息");
            return;
        }
        
        var request = new ChatRequest
        {
            type = "login",
            data = new ChatLoginRequest { 
                username = username, 
                userkey = userkey 
            }
        };
        
        // 发送登录消息
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送登录聊天服务器消息: {jsonMessage}");
        websocket.Send(jsonMessage);
    }

    // 发送聊天消息
    public void SendChatMessage(string message)
    {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open)
        {
            Debug.LogError("WebSocket连接未建立，无法发送聊天消息");
            return;
        }
        // 根据SwitchSendTarget的值 选择发送目标房间id 0为大厅 1为UserDataManager中存储的房间id 2为私聊(未启用)
        int targetRoomId;

        if (SwitchSendTarget.value == 0)
        {
            targetRoomId = 0; // 大厅id
        }
        else if (SwitchSendTarget.value == 1)
        {
            if (UserDataManager.Instance.room_id != ""){
                targetRoomId = int.Parse(UserDataManager.Instance.room_id);  // 房间id
            }
            else{
                DisplayChatMessage(new ChatResponse { responseType = "False", roomId = 0, content = "未进入房间,无法在房间中发送消息" });
                return;
            }
        }
        else if (SwitchSendTarget.value == 2)
        {
            DisplayChatMessage(new ChatResponse { responseType = "False", roomId = 0, content = "待开发功能,暂时无法向好友发送消息" });
            return;
        }
        else
        {
            Debug.LogError("未选择发送目标,无法发送消息");
            return;
        }

        var request = new ChatRequest
        {
            type = "sendChat",
            data = new ChatSendChatRequest { 
                content = message.Trim(), 
                roomId = targetRoomId 
            }
        };
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送聊天消息: {jsonMessage}");
        websocket.Send(jsonMessage);
        // 清空聊天框
        MessageInputField.text = "";
    }

    // 加入聊天房间
    public void JoinRoom(int roomId)
    {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open)
        {
            Debug.LogError("WebSocket连接未建立，无法发送加入房间消息");
            return;
        }
        
        var request = new ChatRequest
        {
            type = "joinRoom",
            data = new ChatJoinRoomRequest { 
                roomId = roomId 
            }
        };
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送聊天服务器加入房间消息: {jsonMessage}");
        websocket.Send(jsonMessage);
    }

    // 离开聊天房间
    public void LeaveRoom(int roomId)
    {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open)
        {
            Debug.LogError("WebSocket连接未建立，无法发送离开房间消息");
            return;
        }
        
        var request = new ChatRequest
        {
            type = "leaveRoom",
            data = new ChatLeaveRoomRequest { 
                roomId = roomId 
            }
        };
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送聊天服务器离开房间消息: {jsonMessage}");
        websocket.Send(jsonMessage);
    }

    // 将聊天窗口滚动到底部
    public void ScrollChatToBottom()
    {
        // 获取聊天容器的ScrollRect组件
        ScrollRect scrollRect = ChatTextContainer.GetComponentInParent<ScrollRect>();
        if (scrollRect != null)
        {
            // 将滚动位置设置为底部 (0, 0) 表示底部
            scrollRect.verticalNormalizedPosition = 0f;
        }
        else
        {
            Debug.LogWarning("未找到ScrollRect组件，无法滚动聊天窗口");
        }
    }
}

