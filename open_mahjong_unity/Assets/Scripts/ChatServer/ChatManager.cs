using UnityEngine;
using System;
using NativeWebSocket;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Collections.Concurrent;

public class ChatManager : MonoBehaviour {
    public static ChatManager Instance { get; private set; }
    private bool isConnecting = false; // 定义连接状态
    private WebSocket websocket;
    private string connectId; // 唯一标识连接的会话id 不是userid哦
    private Queue<byte[]> ConcurrentQueue = new Queue<byte[]>(); // 定义消息队列

    // 初始化连接
    private void Awake() {
        if (Instance != null && Instance != this) {
            Debug.Log($"Destroying duplicate ChatManager. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        connectId = System.Guid.NewGuid().ToString(); // 生成一个不同机器唯一的连接id
        websocket = new WebSocket($"{ConfigManager.chatUrl}/{connectId}"); // 初始化WebSocket
        
        // 配置WebSocket事件处理器（NativeWebSocket格式）
        websocket.OnOpen += () => {
            Debug.Log("WebSocket To ChatServer连接已打开");
            isConnecting = false;
        };
        
        websocket.OnMessage += (bytes) => {
            lock(ConcurrentQueue) {
                ConcurrentQueue.Enqueue(bytes);
            }
        };
        
        websocket.OnError += (errorMsg) => {
            Debug.Log($"WebSocket To ChatServer错误: {errorMsg}");
            isConnecting = false;
        };
        
        websocket.OnClose += (code) => {
            Debug.Log($"WebSocket To ChatServer已关闭: {code}");
            isConnecting = false;
        };
    }

    // 连接到聊天服务器
    private async void Start() {
        // 确保网络管理器唯一，并且没有在连接中
        if (Instance == this && !isConnecting) {
            isConnecting = true;
            try {
                Debug.Log($"开始连接聊天服务器，当前状态: {websocket.State}");
                await websocket.Connect();
                Debug.Log($"连接聊天服务器完成，当前状态: {websocket.State}");
            }
            catch (Exception e) {
                Debug.Log($"连接聊天服务器错误: {e.Message}");
                if (NotificationManager.Instance != null) {
                    NotificationManager.Instance.ShowTip("WebSocket To ChatServer错误", false, e.Message);
                }
            }
            finally {
                isConnecting = false; // 连接失败，设置连接状态为false
            }
        }
    }

    // 收到聊天服务器消息
    private void GetMessage(byte[] bytes) {
        try {
            string jsonStr = System.Text.Encoding.UTF8.GetString(bytes);
            Debug.Log($"收到聊天服务器消息: {jsonStr}");
            ProcessChatMessage(jsonStr);
        }
        catch (Exception e) {
            Debug.Log($"解析聊天服务器消息错误: {e.Message}");
            NotificationManager.Instance.ShowTip("解析聊天服务器消息错误", false, e.Message);
        }
    }

    // 检测消息队列
    private void Update() {
        // 非WebGL平台需要调用DispatchMessageQueue来处理WebSocket消息
        #if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
        #endif
        
        if (ConcurrentQueue.Count > 0) {
            byte[] messageBytes;
            lock(ConcurrentQueue) {
                messageBytes = ConcurrentQueue.Dequeue();
            }
            GetMessage(messageBytes);
        }
    }

    // 处理聊天消息并触发事件
    private void ProcessChatMessage(string message) {
        try {
            // 解析消息
            ChatResponse response = JsonConvert.DeserializeObject<ChatResponse>(message);
            Debug.Log($"处理聊天消息: {response.content}");
            ChatPanel.Instance.ShowChatMessage(response.responseType, response.roomId, response.content);
        }
        catch (Exception e) {
            Debug.Log($"处理聊天消息错误: {e.Message}");
            NotificationManager.Instance.ShowTip("处理聊天消息错误", false, e.Message);
        }
    }

    // 登录聊天服务器
    public async void LoginChatServer(string username, string userkey) {
        Debug.Log($"开始登录聊天服务器，连接ID: {connectId}, 用户名: {username}, 用户密钥: {userkey}");
        
        // 检查WebSocket连接状态
        if (websocket == null || websocket.State != WebSocketState.Open) {
            Debug.Log("WebSocket连接未建立，无法发送登录消息");
            return;
        }
        
        var request = new ChatRequest {
            type = "login",
            data = new ChatLoginRequest { 
                username = username, 
                userkey = userkey 
            }
        };
        
        // 发送登录消息
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送登录聊天服务器消息: {jsonMessage}");
        await websocket.SendText(jsonMessage);
    }

    // 发送聊天消息（由 ChatPanel 调用，传入消息内容和目标房间ID）
    public async void SendChatMessage(string message, int targetChannelId) {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.State != WebSocketState.Open) {
            Debug.Log("WebSocket连接未建立，无法发送聊天消息");
            return;
        }

        var request = new ChatRequest {
            type = "sendChat",
            data = new ChatSendChatRequest { 
                content = message.Trim(), 
                roomId = targetChannelId 
            }
        };
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送聊天消息: {jsonMessage}");
        await websocket.SendText(jsonMessage);
    }

    // 加入聊天房间
    public async void JoinRoom(int roomId) {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.State != WebSocketState.Open) {
            Debug.Log("WebSocket连接未建立，无法发送加入房间消息");
            NotificationManager.Instance.ShowTip("WebSocket连接未建立，无法发送加入房间消息", false, "");
            return;
        }
        
        var request = new ChatRequest {
            type = "joinRoom",
            data = new ChatJoinRoomRequest { 
                roomId = roomId 
            }
        };
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送聊天服务器加入房间消息: {jsonMessage}");
        await websocket.SendText(jsonMessage);
    }

    // 离开聊天房间
    public async void LeaveRoom(int roomId) {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.State != WebSocketState.Open) {
            Debug.Log("WebSocket连接未建立，无法发送离开房间消息");
            NotificationManager.Instance.ShowTip("WebSocket连接未建立，无法发送离开房间消息", false, "");
            return;
        }
        
        var request = new ChatRequest {
            type = "leaveRoom",
            data = new ChatLeaveRoomRequest { 
                roomId = roomId 
            }
        };
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送聊天服务器离开房间消息: {jsonMessage}");
        await websocket.SendText(jsonMessage);
    }

    // 清理资源
    private async void OnDestroy() {
        if (websocket != null && websocket.State == WebSocketState.Open) {
            await websocket.Close();
        }
    }
}

