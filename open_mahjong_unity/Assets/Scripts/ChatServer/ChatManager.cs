using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NativeWebSocket;
using Newtonsoft.Json;
using UnityEngine;

public partial class ChatManager : MonoBehaviour {
    public static ChatManager Instance { get; private set; }
    const float ConnectTimeout = 12f, RequestTimeout = 12f, HeartbeatInterval = 10f, HeartbeatTimeout = 30f;
    readonly Queue<(WebSocket socket, byte[] bytes)> messageQueue = new Queue<(WebSocket, byte[])>();
    NetworkManager networkManager;
    WebSocket websocket, authenticatedGameSocket;
    bool isConnecting, loginSent, loginReady, sessionReplaced, heartbeatSupported;
    int desiredRoomId, joinedRoomId, retryAttempt;
    string username, userkey;
    float connectionDeadline, nextReconnect, nextHeartbeat, lastResponse;
    string roomRequestId, roomRequestType;
    int requestedRoomId;
    float roomRequestDeadline;
    sealed class PendingSend {
        public int room;
        public string content;
        public float deadline;
        public TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>();
    }
    readonly Dictionary<string, PendingSend> pendingSends = new Dictionary<string, PendingSend>();

    bool HasGameSession => !sessionReplaced && !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(userkey)
        && authenticatedGameSocket != null && NetworkManager.Instance != null
        && NetworkManager.Instance.GetWebSocket() == authenticatedGameSocket
        && NetworkManager.Instance.IsWebSocketOpen && UserDataManager.Instance != null
        && UserDataManager.Instance.UserId > 0 && UserDataManager.Instance.UserId == conversationUserId;
    public bool CanSend => HasGameSession && loginReady && websocket != null && websocket.State == WebSocketState.Open;
    public bool CanSendToRoom(int roomId) => CanSend && (roomId == 0 || roomId == desiredRoomId && roomId == joinedRoomId && roomRequestId == null);

    void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    IEnumerator Start() {
        while (NetworkManager.Instance == null) yield return null;
        if (Instance != this) yield break;
        networkManager = NetworkManager.Instance;
        networkManager.ConnectionAvailabilityChanged += OnGameConnectionChanged;
        OnGameConnectionChanged(networkManager.IsWebSocketOpen);
    }

    void OnGameConnectionChanged(bool connected) {
        if (!connected) EndSession(false);
        // A transport connection is anonymous until Python returns a login response.
        else if (HasGameSession) ConnectChatServer();
    }

    /// <summary>Invalidate credentials immediately on logout/kick, without waiting for the socket close callback.</summary>
    public void EndSession(bool clearHistory = true) {
        authenticatedGameSocket = null;
        username = userkey = null;
        retryAttempt = 0;
        DisconnectChatServer();
        FailConversationRequests();
        if (clearHistory) { desiredRoomId = 0; BindConversationUser(0); }
    }

    void ConnectChatServer() {
        if (!HasGameSession || isConnecting || websocket != null) return;
        var socket = new WebSocket($"{ConfigManager.chatUrl}/{Guid.NewGuid()}");
        websocket = socket;
        isConnecting = true;
        connectionDeadline = Time.realtimeSinceStartup + ConnectTimeout;
        socket.OnOpen += () => {
            if (socket != websocket) return;
            isConnecting = false;
            lastResponse = Time.realtimeSinceStartup;
            SendLoginIfReady();
        };
        socket.OnMessage += bytes => {
            if (socket != websocket) return;
            lock (messageQueue) messageQueue.Enqueue((socket, bytes));
        };
        socket.OnError += error => {
            if (socket != websocket) return;
            Debug.LogWarning($"聊天连接失败，将重试: {error}");
            RetryConnection(socket);
        };
        socket.OnClose += code => {
            if (socket != websocket) return;
            // NativeWebSocket dispatches buffered messages separately from OnClose.
            // Consume a pending kick before deciding whether a reconnect is allowed.
#if !UNITY_WEBGL || UNITY_EDITOR
            socket.DispatchMessageQueue();
#endif
            DrainMessages();
            if (socket != websocket) return;
            if (code == WebSocketCloseCode.PolicyViolation) {
                sessionReplaced = true;
                DisconnectChatServer();
            } else RetryConnection(socket);
        };
        ConnectSocket(socket);
    }

    async void ConnectSocket(WebSocket socket) {
        try { await socket.Connect(); }
        catch (Exception exception) {
            if (socket != websocket) return;
            Debug.LogWarning($"聊天连接失败: {exception.Message}");
            RetryConnection(socket);
        }
    }

    void RetryConnection(WebSocket socket) {
        if (socket != websocket) return;
        DisconnectChatServer();
        nextReconnect = Time.realtimeSinceStartup + Math.Min(30f, (float)Math.Pow(2, Math.Min(retryAttempt++, 5)));
    }

    void DisconnectChatServer() {
        var old = websocket;
        websocket = null;
        isConnecting = loginSent = loginReady = heartbeatSupported = false;
        joinedRoomId = 0;
        roomRequestId = roomRequestType = null;
        lock (messageQueue) messageQueue.Clear();
        var sends = new List<PendingSend>(pendingSends.Values);
        pendingSends.Clear();
        foreach (var send in sends) send.completion.TrySetResult(false);
        CloseSocket(old);
    }

    async void CloseSocket(WebSocket socket) {
        if (socket == null) return;
        try {
            if (socket.State == WebSocketState.Open) {
                var close = socket.Close();
                if (await Task.WhenAny(close, Task.Delay(1500)) == close) await close;
                else socket.CancelConnection();
            } else socket.CancelConnection();
        } catch (Exception exception) {
            socket.CancelConnection();
            Debug.LogWarning($"关闭聊天连接: {exception.Message}");
        }
    }

    void Update() {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif
        DrainMessages();
        TickConversationRequests();
        if (!HasGameSession) return;
        float now = Time.realtimeSinceStartup;
        if (websocket == null) { if (now >= nextReconnect) ConnectChatServer(); return; }
        if ((!loginReady && now >= connectionDeadline) || (roomRequestId != null && now >= roomRequestDeadline)) {
            RetryConnection(websocket); return;
        }
        if (heartbeatSupported && loginReady) {
            if (now - lastResponse >= HeartbeatTimeout) { RetryConnection(websocket); return; }
            if (now >= nextHeartbeat) {
                nextHeartbeat = now + HeartbeatInterval;
                SendControl(websocket, new ChatRequest { type = "ping" });
            }
        }
        List<string> expired = null;
        foreach (var pair in pendingSends) if (now >= pair.Value.deadline) {
            if (expired == null) expired = new List<string>();
            expired.Add(pair.Key);
        }
        if (expired != null) {
            foreach (var id in expired) CompleteSend(id, false);
            RetryConnection(websocket);
        }
    }

    void DrainMessages() {
        while (true) {
            (WebSocket socket, byte[] bytes) item;
            lock (messageQueue) {
                if (messageQueue.Count == 0) return;
                item = messageQueue.Dequeue();
            }
            if (item.socket == websocket) ProcessChatMessage(item.bytes);
        }
    }

    void ProcessChatMessage(byte[] bytes) {
        try {
            var response = JsonConvert.DeserializeObject<ChatResponse>(System.Text.Encoding.UTF8.GetString(bytes));
            if (response == null) return;
            lastResponse = Time.realtimeSinceStartup;
            if (response.responseType == "login_kickout") {
                sessionReplaced = true;
                DisconnectChatServer();
                return;
            }
            if (response.requestType == "login" || loginSent && !loginReady &&
                (response.content == "登录聊天大厅成功" || response.content == "登录聊天大厅失败,用户密钥错误")) {
                if (response.responseType == "False") {
                    ChatMessageStore.Instance?.ShowChatMessage("False", 0, response.content);
                    RetryConnection(websocket);
                    return;
                }
                if (!HasGameSession) return;
                loginReady = true;
                retryAttempt = 0;
                heartbeatSupported = response.protocolVersion >= 2;
                nextHeartbeat = lastResponse + HeartbeatInterval;
                ChatMessageStore.Instance?.ShowChatMessage("LoginTime", 0, DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                ChatMessageStore.Instance?.ShowChatMessage("serverMessage", 0,
                    "欢迎使用open_mahjong_unity，在聊天室请友善交流，切勿发送侮辱性用语或非法言论。");
                SynchronizeRoomChannel();
                return;
            }
            if (response.responseType == "pong") return;
            if (roomRequestId != null && (response.requestId == roomRequestId ||
                string.IsNullOrEmpty(response.requestId) && response.roomId == requestedRoomId &&
                (response.requestType == roomRequestType || response.content ==
                    (roomRequestType == "joinRoom" ? "加入房间" : "退出房间") + requestedRoomId + "成功"))) {
                if (response.responseType == "False") { RetryConnection(websocket); return; }
                ChatMessageStore.Instance?.ShowChatMessage(roomRequestType == "joinRoom" ? "RoomJoined" : "RoomLeft",
                    requestedRoomId, requestedRoomId.ToString());
                joinedRoomId = roomRequestType == "joinRoom" ? requestedRoomId : 0;
                roomRequestId = roomRequestType = null;
                SynchronizeRoomChannel();
                return;
            }
            if (response.responseType == "sendChatOk" || response.requestType == "sendChat") {
                string id = response.requestId;
                if (string.IsNullOrEmpty(id)) foreach (var pair in pendingSends)
                    if (pair.Value.room == response.roomId && pair.Value.content == response.content) { id = pair.Key; break; }
                CompleteSend(id, response.responseType == "sendChatOk");
                if (response.code == "not_joined") RetryConnection(websocket);
                return;
            }
            if (loginReady) ChatMessageStore.Instance?.ShowChatMessage(response.responseType, response.roomId, response.content);
        } catch (Exception exception) { Debug.LogWarning($"处理聊天消息失败: {exception.Message}"); }
    }

    public void LoginChatServer(string newUsername, string newUserkey) {
        var gameSocket = NetworkManager.Instance?.GetWebSocket();
        bool changed = username != newUsername || userkey != newUserkey || authenticatedGameSocket != gameSocket
            || sessionReplaced || conversationUserId != UserDataManager.Instance?.UserId;
        if (changed) DisconnectChatServer();
        BindConversationUser(UserDataManager.Instance != null ? UserDataManager.Instance.UserId : 0);
        username = newUsername;
        userkey = newUserkey;
        authenticatedGameSocket = gameSocket;
        sessionReplaced = false;
        retryAttempt = 0;
        nextReconnect = 0;
        string room = UserDataManager.Instance?.ChatRoomId;
        desiredRoomId = int.TryParse(room, out int channel) ? channel : 0;
        RequestConversations();
        RequestChatVenues();
        ConnectChatServer();
        SendLoginIfReady();
    }

    void SendLoginIfReady() {
        if (!HasGameSession || loginSent || websocket == null || websocket.State != WebSocketState.Open) return;
        loginSent = true;
        connectionDeadline = Time.realtimeSinceStartup + RequestTimeout;
        SendControl(websocket, new ChatRequest { type = "login", data = new ChatLoginRequest { username = username, userkey = userkey } });
    }

    async void SendControl(WebSocket socket, ChatRequest request) {
        try { await socket.SendText(JsonConvert.SerializeObject(request)); }
        catch (Exception exception) {
            if (socket != websocket) return;
            Debug.LogWarning($"聊天请求发送失败: {exception.Message}");
            RetryConnection(socket);
        }
    }

    public async void SendChatMessage(string message, int targetChannelId) {
        await SendChatMessageAsync(message, targetChannelId);
    }

    public async Task<bool> SendChatMessageAsync(string message, int targetChannelId) {
        if (string.IsNullOrWhiteSpace(message) || !CanSendToRoom(targetChannelId)) return false;
        var socket = websocket;
        string id = Guid.NewGuid().ToString("N");
        var send = new PendingSend { room = targetChannelId, content = message.Trim(), deadline = Time.realtimeSinceStartup + RequestTimeout };
        pendingSends.Add(id, send);
        try { await socket.SendText(JsonConvert.SerializeObject(new ChatRequest {
            type = "sendChat", requestId = id, data = new ChatSendChatRequest { roomId = targetChannelId, content = send.content }
        })); }
        catch (Exception exception) {
            Debug.LogWarning($"聊天消息发送失败: {exception.Message}");
            CompleteSend(id, false);
            if (socket == websocket) RetryConnection(socket);
        }
        return await send.completion.Task;
    }

    void CompleteSend(string id, bool success) {
        if (id == null || !pendingSends.TryGetValue(id, out var send)) return;
        pendingSends.Remove(id);
        send.completion.TrySetResult(success);
    }

    public void SetRoomChannel(int roomId) {
        desiredRoomId = roomId;
        SynchronizeRoomChannel();
    }

    void SynchronizeRoomChannel() {
        if (!CanSend || roomRequestId != null || joinedRoomId == desiredRoomId) return;
        roomRequestType = joinedRoomId != 0 ? "leaveRoom" : "joinRoom";
        requestedRoomId = joinedRoomId != 0 ? joinedRoomId : desiredRoomId;
        roomRequestId = Guid.NewGuid().ToString("N");
        roomRequestDeadline = Time.realtimeSinceStartup + RequestTimeout;
        SendControl(websocket, new ChatRequest { type = roomRequestType, requestId = roomRequestId,
            data = roomRequestType == "leaveRoom" ? (object)new ChatLeaveRoomRequest { roomId = requestedRoomId } : new ChatJoinRoomRequest { roomId = requestedRoomId } });
    }

    public void JoinRoom(int roomId) => SetRoomChannel(roomId);
    public void LeaveRoom(int roomId) { if (desiredRoomId == roomId) SetRoomChannel(0); }

    void OnDestroy() {
        if (Instance != this) return;
        if (networkManager != null) networkManager.ConnectionAvailabilityChanged -= OnGameConnectionChanged;
        EndSession();
        Instance = null;
    }
}
