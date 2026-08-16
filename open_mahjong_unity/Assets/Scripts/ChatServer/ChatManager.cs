using System;
using System.Collections;
using System.Collections.Generic;
using NativeWebSocket;
using Newtonsoft.Json;
using UnityEngine;

public class ChatManager : MonoBehaviour {
    public static ChatManager Instance { get; private set; }

    private readonly Queue<byte[]> messageQueue = new Queue<byte[]>();
    private NetworkManager networkManager;
    private WebSocket websocket;
    private bool isConnecting;
    private bool loginSent;
    private string username;
    private string userkey;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private IEnumerator Start() {
        while (NetworkManager.Instance == null) yield return null;
        if (Instance != this) yield break;

        networkManager = NetworkManager.Instance;
        networkManager.ConnectionAvailabilityChanged += OnGameConnectionChanged;
        OnGameConnectionChanged(networkManager.IsWebSocketOpen);
    }

    /// <summary>
    /// Go WebSocket 完全跟随 Python WebSocket：断开时关闭，连接时只连接一次。
    /// Go 自身关闭或失败时不会发起任何重连。
    /// </summary>
    private void OnGameConnectionChanged(bool connected) {
        if (connected) {
            ConnectChatServer();
        } else {
            // 重连后必须等待 Python 新的登录响应，再登录新的 Go 连接。
            username = null;
            userkey = null;
            DisconnectChatServer();
        }
    }

    private void ConnectChatServer() {
        if (isConnecting) return;
        if (websocket != null
            && (websocket.State == WebSocketState.Open
                || websocket.State == WebSocketState.Connecting)) return;

        WebSocket oldSocket = websocket;
        websocket = null;
        CloseSocket(oldSocket);

        string connectId = Guid.NewGuid().ToString();
        WebSocket newSocket = new WebSocket($"{ConfigManager.chatUrl}/{connectId}");
        websocket = newSocket;
        isConnecting = true;
        loginSent = false;

        newSocket.OnOpen += () => {
            if (newSocket != websocket) return;
            isConnecting = false;
            Debug.Log("Python WebSocket 已连接，同步连接 Go ChatServer 成功");
            SendLoginIfReady();
        };

        newSocket.OnMessage += bytes => {
            if (newSocket != websocket) return;
            lock (messageQueue) messageQueue.Enqueue(bytes);
        };

        newSocket.OnError += error => {
            if (newSocket != websocket) return;
            isConnecting = false;
            Debug.LogWarning($"Go ChatServer 连接错误: {error}");
        };

        newSocket.OnClose += code => {
            if (newSocket != websocket) return;
            websocket = null;
            isConnecting = false;
            loginSent = false;
            Debug.Log($"Go ChatServer 已关闭: {code}");
            // 不重连。下一次连接只能由 Python WebSocket 的 OnOpen 触发。
        };

        ConnectSocket(newSocket);
    }

    private async void ConnectSocket(WebSocket targetSocket) {
        try {
            await targetSocket.Connect();
        } catch (Exception exception) {
            if (targetSocket == websocket) {
                isConnecting = false;
                Debug.LogWarning($"连接 Go ChatServer 失败: {exception.Message}");
            }
        }
    }

    private void DisconnectChatServer() {
        WebSocket oldSocket = websocket;
        websocket = null;
        isConnecting = false;
        loginSent = false;
        lock (messageQueue) messageQueue.Clear();
        CloseSocket(oldSocket);
    }

    private async void CloseSocket(WebSocket targetSocket) {
        if (targetSocket == null) return;
        try {
            if (targetSocket.State == WebSocketState.Connecting) {
                targetSocket.CancelConnection();
            } else if (targetSocket.State == WebSocketState.Open) {
                await targetSocket.Close();
            }
        } catch (Exception exception) {
            Debug.LogWarning($"关闭 Go ChatServer 连接失败: {exception.Message}");
        }
    }

    private void Update() {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif
        byte[] bytes = null;
        lock (messageQueue) {
            if (messageQueue.Count > 0) bytes = messageQueue.Dequeue();
        }
        if (bytes != null) ProcessChatMessage(bytes);
    }

    private void ProcessChatMessage(byte[] bytes) {
        try {
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            ChatResponse response = JsonConvert.DeserializeObject<ChatResponse>(json);

            // Python 游戏服负责账号顶替提示；Go 只负责同步关闭连接。
            if (response.responseType == "login_kickout") return;

            ChatPanel.Instance?.ShowChatMessage(
                response.responseType,
                response.roomId,
                response.content
            );
        } catch (Exception exception) {
            Debug.LogWarning($"处理 Go ChatServer 消息失败: {exception.Message}");
        }
    }

    /// <summary>保存 Python 登录响应中的凭据；Go 打开后只发送一次。</summary>
    public void LoginChatServer(string newUsername, string newUserkey) {
        username = newUsername;
        userkey = newUserkey;
        SendLoginIfReady();
    }

    private async void SendLoginIfReady() {
        if (loginSent || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(userkey)) return;
        if (websocket == null || websocket.State != WebSocketState.Open) return;

        var request = new ChatRequest {
            type = "login",
            data = new ChatLoginRequest {
                username = username,
                userkey = userkey,
            },
        };
        loginSent = true;
        try {
            await websocket.SendText(JsonConvert.SerializeObject(request));
        } catch (Exception exception) {
            Debug.LogWarning($"登录 Go ChatServer 失败: {exception.Message}");
        }
    }

    public async void SendChatMessage(string message, int targetChannelId) {
        if (websocket == null || websocket.State != WebSocketState.Open) return;
        var request = new ChatRequest {
            type = "sendChat",
            data = new ChatSendChatRequest {
                content = message.Trim(),
                roomId = targetChannelId,
            },
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }

    public async void JoinRoom(int roomId) {
        if (websocket == null || websocket.State != WebSocketState.Open) return;
        var request = new ChatRequest {
            type = "joinRoom",
            data = new ChatJoinRoomRequest { roomId = roomId },
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }

    public async void LeaveRoom(int roomId) {
        if (websocket == null || websocket.State != WebSocketState.Open) return;
        var request = new ChatRequest {
            type = "leaveRoom",
            data = new ChatLeaveRoomRequest { roomId = roomId },
        };
        await websocket.SendText(JsonConvert.SerializeObject(request));
    }

    private void OnDestroy() {
        if (networkManager != null) {
            networkManager.ConnectionAvailabilityChanged -= OnGameConnectionChanged;
        }
        DisconnectChatServer();
        if (Instance == this) Instance = null;
    }
}
