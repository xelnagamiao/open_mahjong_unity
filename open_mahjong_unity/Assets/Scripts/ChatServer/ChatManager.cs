using UnityEngine;
using System;
using WebSocketSharp;
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
        websocket.OnOpen += (sender, e) => Debug.Log("WebSocket To ChatServer连接已打开");
        websocket.OnMessage += (sender, e) => {
            lock(ConcurrentQueue) {
                ConcurrentQueue.Enqueue(e.RawData);
            }
        };
        websocket.OnError += (sender, e) => Debug.Log($"WebSocket To ChatServer错误: {e.Message}");
        websocket.OnClose += (sender, e) => Debug.Log($"WebSocket To ChatServer已关闭: {e.Code}");
    }

    // 连接到聊天服务器
    private void Start() {
        // 确保网络管理器唯一，并且没有在连接中
        if (Instance == this && !isConnecting) {
            isConnecting = true;
            try {
                Debug.Log($"开始连接聊天服务器，当前状态: {websocket.ReadyState}");
                websocket.ConnectAsync();
                Debug.Log($"连接聊天服务器完成，当前状态: {websocket.ReadyState}");
                ChatPanel.Instance.ShowChatMessage("serverMessage", 0, "欢迎使用open_mahjong_unity，在聊天室请友善交流，切勿发送侮辱性用语或非法言论，目前我们无法对发送违规信息的用户进行禁言处理，如发现有违规情况，可能会删除账户、封禁IP处理，在游戏过程中如果发现任何问题或想提交任何建议，请联系网站管理员或在交流群进行咨询，祝您游戏愉快！");
            }
            catch (Exception e) {
                Debug.Log($"连接聊天服务器错误: {e.Message}");
                NotificationManager.Instance.ShowTip("WebSocket To ChatServer错误", false, e.Message);
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
    public void LoginChatServer(string username, string userkey) {
        Debug.Log($"开始登录聊天服务器，连接ID: {connectId}, 用户名: {username}, 用户密钥: {userkey}");
        
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open) {
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
        websocket.Send(jsonMessage);
    }

    // 发送聊天消息（由 ChatPanel 调用，传入消息内容和目标房间ID）
    public void SendChatMessage(string message, int targetChannelId) {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open) {
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
        websocket.Send(jsonMessage);
    }

    // 加入聊天房间
    public void JoinRoom(int roomId) {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open) {
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
        websocket.Send(jsonMessage);
    }

    // 离开聊天房间
    public void LeaveRoom(int roomId) {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open) {
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
        websocket.Send(jsonMessage);
    }

    // 清理资源
    private void OnDestroy() {
        if (websocket != null && websocket.ReadyState == WebSocketState.Open) {
            websocket.Close();
        }
    }
}

