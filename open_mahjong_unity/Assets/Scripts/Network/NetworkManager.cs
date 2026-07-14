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

    /// <summary>
    /// 获取 websocket 连接（供其他管理器使用）
    /// </summary>
    public WebSocket GetWebSocket() {
        return websocket;
    }
    private string playerId; // 定义玩家ID
    private bool isConnecting = false; // 定义连接状态
    private Queue<byte[]> messageQueue = new Queue<byte[]>(); // 定义消息队列
    private readonly Queue<byte[]> priorityMessageQueue = new Queue<byte[]>(); // 需立即处理的消息（如 match/match_found）
    private const string MatchFoundTypeJson = "\"type\":\"match/match_found\"";

    /// <summary>网络消息队列是否真正积压（当前帧出队后仍有 ≥2 条未处理）。
    /// 用于判断客户端是否在“补帧/追赶”：后台降帧切回时会连续处理十几条。
    /// 显示层延迟（如鸣牌 claim_meld_followup_gap）在补帧时应跳过，避免逻辑已同步、3D 却逐条卡住。
    /// 阈值取 ≥2：仅剩 1 条（下一巡 cut 刚入队，属正常快打）时仍保留间隔，否则动态延迟几乎永远不生效。</summary>
    public bool IsBacklogged {
        get { lock(messageQueue) { return messageQueue.Count >= 2; } }
    }
    public GameEvent ErrorResponse = new GameEvent(); // 定义错误响应事件
    public GameEvent CreateRoomResponse = new GameEvent(); // 定义创建房间响应事件

    // 心跳/延迟测量
    private const float PingIntervalSeconds = 2f; // ping 发送间隔
    private const float PingTimeoutSeconds = 6f; // 超过该秒数未收到 pong 时视为高延迟
    private const int PingTimeoutMs = 9999; // 超时未收到 pong 时显示的延迟值
    private float _pingTimer = PingIntervalSeconds; // 初始为间隔值，连接后立刻 ping 一次
    private long _lastPingTs;
    private float _lastPongElapsed; // 距离上次收到 pong 的时间，用于检测超时
    private int _latencyMs = -1; // -1 表示尚未取得测量

    // AutoReconnect 探活
    public bool ProbePongReceived { get; private set; }

    /// <summary>最近一次 ping/pong 测得的延迟（毫秒）。-1 表示未测得，>=0 为有效值。</summary>
    public int LatencyMs => _latencyMs;
    /// <summary>当前 WebSocket 是否处于已连接状态。</summary>
    public bool IsWebSocketOpen => websocket != null && websocket.State == WebSocketState.Open;
    /// <summary>延迟变更事件。每次收到 pong 或 ping 超时时触发，参数为最新延迟（毫秒）。</summary>
    public event Action<int> OnLatencyChanged;

    // 主线程调度器
    private Queue<Action> mainThreadActions = new Queue<Action>();
    // 断线弹窗状态：仅 Disconnected 时弹窗；NoMatch 等状态不弹
    private enum DisconnectDialogState { Start, Connected, Disconnected, Shown, NoMatch }
    private DisconnectDialogState _disconnectDialogState = DisconnectDialogState.Start;
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
        BindWebSocketEvents(websocket);
    }

    private static bool IsMatchFoundMessage(byte[] bytes) {
        if (bytes == null || bytes.Length == 0) return false;
        return System.Text.Encoding.UTF8.GetString(bytes).Contains(MatchFoundTypeJson);
    }

    private void EnqueueIncomingMessage(byte[] bytes) {
        lock (messageQueue) {
            if (IsMatchFoundMessage(bytes)) {
                priorityMessageQueue.Enqueue(bytes);
            } else {
                messageQueue.Enqueue(bytes);
            }
        }
    }

    private bool TryDequeueNextMessage(out byte[] message) {
        lock (messageQueue) {
            if (priorityMessageQueue.Count > 0) {
                message = priorityMessageQueue.Dequeue();
                return true;
            }
            if (messageQueue.Count > 0) {
                message = messageQueue.Dequeue();
                return true;
            }
        }
        message = null;
        return false;
    }

    private void BindWebSocketEvents(WebSocket ws) {
        ws.OnMessage += EnqueueIncomingMessage;

        ws.OnOpen += () => {
            if (ws != websocket) return;
            Debug.Log("WebSocket连接成功");
            isConnecting = false;
            OnConnectionEstablished();
            ExecuteOnMainThread(() => {
                if (IsOnLoginPage()) {
                    LoginPanel.Instance?.ConnectOkText();
                }
            });
            SendReleaseVersion();
        };

        ws.OnError += (errorMsg) => {
            if (ws != websocket) return;
            Debug.LogError($"WebSocket连接失败: {errorMsg}");
            isConnecting = false;
            ExecuteOnMainThread(() => {
                if (IsOnLoginPage()) {
                    LoginPanel.Instance?.ConnectErrorText(errorMsg);
                }
                HandleConnectionLostUi();
            });
        };

        ws.OnClose += (code) => {
            if (ws != websocket) return;
            Debug.Log($"WebSocket已关闭: {code}");
            isConnecting = false;
            ExecuteOnMainThread(() => {
                if (AutoReconnect.TryHandleOnClose()) return;
                if (IsOnLoginPage()) {
                    LoginPanel.Instance?.ConnectErrorText("连接已关闭");
                }
                HandleConnectionLostUi();
            });
        };
    }

    private void OnConnectionEstablished() {
        _disconnectDialogState = DisconnectDialogState.Start;
    }

    private void MarkDisconnected() {
        if (_disconnectDialogState != DisconnectDialogState.Connected) return;
        _disconnectDialogState = DisconnectDialogState.Disconnected;
        TryShowDisconnectDialog();
    }

    private bool IsOnLoginPage() {
        var wm = WindowsManager.Instance;
        return wm != null && wm.GetCurrentWindow() == "login";
    }

    /// <summary>
    /// 已连上后再断开走 MarkDisconnected；登录页首次连接失败则弹断线重连面板（AutoReconnect 活跃时不介入）。
    /// </summary>
    private void HandleConnectionLostUi() {
        if (AutoReconnect.IsActive) return;
        if (_disconnectDialogState == DisconnectDialogState.Connected) {
            MarkDisconnected();
            return;
        }
        if (!IsOnLoginPage()) return;
        if (_disconnectDialogState == DisconnectDialogState.Shown
            || _disconnectDialogState == DisconnectDialogState.NoMatch) return;
        ForceShowDisconnectDialog();
    }

    public void ForceShowDisconnectDialog() {
        _disconnectDialogState = DisconnectDialogState.Disconnected;
        TryShowDisconnectDialog();
    }

    private void TryShowDisconnectDialog() {
        if (_disconnectDialogState != DisconnectDialogState.Disconnected) return;
        _disconnectDialogState = DisconnectDialogState.Shown;
        NotificationManager.Instance.ShowMessage(
            "连接已断开",
            "与服务器的连接已断开。如正处于一场游戏对局中，点击重连即可回到登录界面并尝试重新连接服务器；游客无法重连至游戏。",
            "disconnect"
        );
    }

    private void OnApplicationPause(bool pause) {
        if (pause) {
            AutoReconnect.OnEnterBackground();
            return;
        }
        // 先派发积压消息让 OnClose 尽早触发，再交 AutoReconnect 处理
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif
        AutoReconnect.OnEnterForeground();
        if (!AutoReconnect.IsActive) {
            CheckDisconnectOnForeground();
        }
    }

    private void OnApplicationFocus(bool hasFocus) {
        if (hasFocus) {
#if !UNITY_WEBGL || UNITY_EDITOR
            websocket?.DispatchMessageQueue();
#endif
            AutoReconnect.OnEnterForeground();
            if (!AutoReconnect.IsActive) {
                CheckDisconnectOnForeground();
            }
        } else {
            AutoReconnect.OnEnterBackground();
        }
    }

    private void CheckDisconnectOnForeground() {
        if (_disconnectDialogState != DisconnectDialogState.Connected) return;
        if (isConnecting) return;
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif
        if (websocket == null || websocket.State != WebSocketState.Open) {
            if (AutoReconnect.TryHandleForegroundDisconnect()) return;
            MarkDisconnected();
        }
    }

    public WebSocket BeginNewConnection() {
        if (websocket != null) {
            try { var _ = websocket.Close(); } catch { }
        }

        lock (messageQueue) {
            messageQueue.Clear();
            priorityMessageQueue.Clear();
        }

        _latencyMs = -1;
        _pingTimer = PingIntervalSeconds;
        _lastPongElapsed = 0f;
        _lastPingTs = 0;
        _disconnectDialogState = DisconnectDialogState.Start;
        isConnecting = false;

        playerId = System.Guid.NewGuid().ToString();
        string url = $"{ConfigManager.gameUrl}/{playerId}";
        WebSocket newSocket = new WebSocket(url);
        websocket = newSocket;
        BindWebSocketEvents(newSocket);

        isConnecting = true;
        Debug.Log($"[NetworkManager] 新建 WebSocket 连接：{url}");
        RunConnectLoop(newSocket);

        return newSocket;
    }

    /// <summary>
    /// 断线后软重置 WebSocket（主线程协程发起，不阻塞 Receive 循环）。
    /// </summary>
    public void RequestReconnectWebSocket() {
        if (!isActiveAndEnabled) {
            Debug.LogWarning("[NetworkManager] 无法重连：NetworkManager 未激活");
            return;
        }
        _disconnectDialogState = DisconnectDialogState.Start;
        CoroutineManager.Ensure();
        CoroutineManager.Instance.RunNamed(
            CoroutineKeys.NetworkReconnect,
            ReconnectWebSocketRoutine(),
            restartIfRunning: true
        );
    }

    private IEnumerator ReconnectWebSocketRoutine() {
        _disconnectDialogState = DisconnectDialogState.Start;
        isConnecting = false;

        if (websocket != null
            && (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.Connecting)) {
            Task closeTask = null;
            try {
                closeTask = websocket.Close();
            } catch (Exception e) {
                Debug.LogWarning($"[NetworkManager] 关闭旧 WebSocket 时出错: {e.Message}");
            }
            if (closeTask != null) {
                float closeWait = 0f;
                while (!closeTask.IsCompleted && closeWait < 3f) {
                    closeWait += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
        }

        lock (messageQueue) {
            messageQueue.Clear();
            priorityMessageQueue.Clear();
        }

        _latencyMs = -1;
        _pingTimer = PingIntervalSeconds;
        _lastPongElapsed = 0f;
        _lastPingTs = 0;

        playerId = System.Guid.NewGuid().ToString();
        string url = $"{ConfigManager.gameUrl}/{playerId}";
        WebSocket newSocket = new WebSocket(url);
        websocket = newSocket;
        BindWebSocketEvents(newSocket);

        isConnecting = true;
        Debug.Log($"[NetworkManager] 发起 WebSocket 重连: {url}, State={newSocket.State}");
        RunConnectLoop(newSocket);

        const float timeoutSeconds = 15f;
        float elapsed = 0f;
        while (elapsed < timeoutSeconds) {
            if (newSocket.State == WebSocketState.Open) {
                Debug.Log("[NetworkManager] WebSocket 重连成功");
                yield break;
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.LogError($"[NetworkManager] WebSocket 重连超时或失败，State={newSocket.State}");
        isConnecting = false;
        if (IsOnLoginPage()) {
            LoginPanel.Instance?.ConnectErrorText("无法连接至服务器，请稍后重试");
        }
        HandleConnectionLostUi();
    }

    /// <summary>
    /// 在后台维持 Connect/Receive 循环；Connect 建立后 OnOpen 会更新 UI。
    /// </summary>
    private async void RunConnectLoop(WebSocket ws) {
        if (ws == null) return;
        try {
            await ws.Connect();
        } catch (Exception e) {
            Debug.LogError($"[NetworkManager] WebSocket Connect 异常: {e.Message}");
            if (ws == websocket) {
                isConnecting = false;
                ExecuteOnMainThread(() => {
                    if (IsOnLoginPage()) {
                        LoginPanel.Instance?.ConnectErrorText(e.Message);
                    }
                    HandleConnectionLostUi();
                });
            }
        }
    }

    // 2.Start方法用于连接到服务器
    private void Start() {
        if (Instance == this && !isConnecting) {
            isConnecting = true;
            Debug.Log($"开始连接服务器，当前状态: {websocket.State}");
            RunConnectLoop(websocket);
        }
    }

    // 网络管理器实例在 Update 中处理消息队列
    private void Update(){
        // 非WebGL平台需要调用DispatchMessageQueue来处理WebSocket消息
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif

        // 处理消息队列（match/match_found 等优先消息先于普通队列）
        if (TryDequeueNextMessage(out byte[] message)) {
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

        // 周期性 ping：仅在已连接时发送，长时间未收到 pong 视为高延迟
        if (websocket != null && websocket.State == WebSocketState.Open) {
            _pingTimer += Time.unscaledDeltaTime;
            _lastPongElapsed += Time.unscaledDeltaTime;
            if (_pingTimer >= PingIntervalSeconds) {
                _pingTimer = 0f;
                SendPing();
            }
            // 超过 PingTimeoutSeconds 仍未收到 pong：视为高延迟，但不主动断开
            if (_lastPingTs > 0 && _lastPongElapsed > PingTimeoutSeconds && _latencyMs != PingTimeoutMs) {
                _latencyMs = PingTimeoutMs;
                OnLatencyChanged?.Invoke(_latencyMs);
            }
        }
    }

    private async void SendPing() {
        try {
            _lastPingTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var request = new PingRequest {
                type = "ping",
                client_ts = _lastPingTs,
            };
            await websocket.SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            Debug.LogWarning($"发送 ping 失败: {e.Message}");
            _latencyMs = PingTimeoutMs;
            OnLatencyChanged?.Invoke(_latencyMs);
        }
    }

    private void HandlePong(Response response) {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long sendTs = response.client_ts > 0 ? response.client_ts : _lastPingTs;
        int rtt = (int)Math.Max(0, now - sendTs);
        _latencyMs = rtt;
        _lastPongElapsed = 0f;
        ProbePongReceived = true;
        OnLatencyChanged?.Invoke(_latencyMs);
    }

    public void ResetProbePongFlag() {
        ProbePongReceived = false;
    }

    public bool SendProbePing() {
        if (websocket == null || websocket.State != WebSocketState.Open) return false;
        try {
            ProbePongReceived = false;
            _lastPingTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var request = new PingRequest {
                type = "ping",
                client_ts = _lastPingTs,
            };
            // fire-and-forget，异常由 SendPing 内部处理
            _ = websocket.SendText(JsonConvert.SerializeObject(request));
            return true;
        } catch {
            return false;
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
        AutoReconnect.OnLoginResponse(response.success);
        if (response.success) {
            WindowsManager.Instance.SwitchWindow("menu");
            // 设置用户信息
            MeunPanel.Instance.SetUserInfo(
                response.login_info.username,
                response.login_info.userkey,
                response.login_info.user_id,
                response.login_info.is_tourist
            );
            HeaderPanel.Instance?.RefreshMatchButtonVisibility();
            // 保存用户信息
            if (response.user_settings != null) {
                UserDataManager.Instance.SetUserSettings(
                    response.user_settings.title_id,
                    response.user_settings.profile_image_id,
                    response.user_settings.character_id,
                    response.user_settings.voice_id
                );
            }
            if (response.rank_data != null) {
                UserDataManager.Instance.SetRankData(
                    response.rank_data.guobiao_rank,
                    response.rank_data.guobiao_score,
                    response.rank_data.is_sponsor,
                    response.rank_data.is_mcrpl_qualified
                );
            }
            UserContainer.Instance.ShowUserSettings(response.user_settings);
            FriendNetworkManager.Instance.ListFriends();
            EventNetworkManager.Instance.ListMyActiveEvents();
        }
    }

    // 显示服务器统计信息（简化版，直接使用服务器返回的数据）
    private void DisplayServerStats(ServerStatsInfo stats) {
        if (stats != null) {
            MeunPanel.Instance.DisplayServerStats(
                stats.online_players,
                stats.waiting_rooms,
                stats.playing_rooms,
                stats.match_playing_games
            );
        }
    }

    // 3.Get_Message方法用于处理服务器返回的消息
    private void Get_Message(byte[] bytes){
        try{
            string jsonStr = System.Text.Encoding.UTF8.GetString(bytes);
            // 心跳响应频繁出现，跳过日志避免刷屏
            if (!jsonStr.Contains("\"type\":\"pong\"")) {
                Debug.Log($"收到服务器消息: {jsonStr}");
            }
            var response = JsonConvert.DeserializeObject<Response>(jsonStr);

            // 好友 / 实时观战相关消息统一交由 FriendNetworkManager 处理
            if (response.type != null && response.type.StartsWith("friend/")) {
                FriendNetworkManager.Instance.HandleFriendMessage(response);
                return;
            }

            // 赛事相关消息统一交由 EventNetworkManager 处理
            if (response.type != null && response.type.StartsWith("event/")) {
                EventNetworkManager.Instance.HandleEventMessage(response);
                return;
            }

            switch (response.type){
                case "login":
                    HandleLoginResponse(response);
                    if (!AutoReconnect.ShouldSuppressLoginTip()) {
                        NotificationManager.Instance.ShowTip("login",true,"登录成功");
                    }
                    break;
                case "get_server_stats": // 获取服务器统计信息
                    DisplayServerStats(response.server_stats);
                    break;
                case "pong": // 心跳回包，用于计算延迟
                    HandlePong(response);
                    break;
                case "error_message":
                    Debug.Log($"错误消息: {response.message}");
                    // 加入/创建失败时取消待进入，避免滞后 refresh_room_info 错误跳进房间页
                    RoomNetworkManager.Instance.CancelPendingRoomEntry();
                    ErrorResponse.Invoke(response.success, response.message);
                    NotificationManager.Instance.ShowTip("error_message",false,response.message);
                    break;
                // 房间相关消息交由 RoomNetworkManager 处理
                case "room/create_room_done":
                case "room/get_room_list":
                case "room/refresh_room_info":
                case "room/sync_not_in_room":
                case "room/join_room_done":
                case "room/leave_room_done":
                    RoomNetworkManager.Instance.HandleRoomMessage(response);
                    break;
                // 数据相关消息交由 DataNetworkManager 处理
                case "data/get_record_list":
                case "data/get_record_by_id":
                case "data/get_guobiao_stats":
                case "data/get_riichi_stats":
                case "data/get_qingque_stats":
                case "data/get_classical_stats":
                case "data/get_jiandan_stats":
                case "data/get_leaderboard":
                case "data/get_rank_record_list":
                    DataNetworkManager.Instance.HandleDataMessage(response);
                    break;
                // 观战系统：初始牌谱 / 增量更新 → GameRecordManager
                case "spectator/record_init":
                    HandleSpectatorRecordInit(response);
                    break;
                case "spectator/record_update":
                    HandleSpectatorRecordUpdate(response);
                    break;
                case "spectator/record_complete":
                    HandleSpectatorRecordComplete(response);
                    break;
                // 观战系统：加入/离开通知
                case "spectator/add_spectator":
                    HandleSpectatorAddResult(response);
                    break;
                case "spectator/remove_spectator":
                    HandleSpectatorRemoveResult(response);
                    break;
                // 游戏状态相关消息交由 GameStateNetworkManager 处理
                case "gamestate/get_spectator_list":
                case "gamestate/guobiao/game_start":
                case "gamestate/qingque/game_start":
                case "gamestate/classical/game_start":
                case "gamestate/riichi/game_start":
                case "gamestate/guobiao/broadcast_hand_action":
                case "gamestate/qingque/broadcast_hand_action":
                case "gamestate/classical/broadcast_hand_action":
                case "gamestate/riichi/broadcast_hand_action":
                case "gamestate/guobiao/ask_other_action":
                case "gamestate/qingque/ask_other_action":
                case "gamestate/classical/ask_other_action":
                case "gamestate/riichi/ask_other_action":
                case "gamestate/guobiao/do_action":
                case "gamestate/qingque/do_action":
                case "gamestate/classical/do_action":
                case "gamestate/riichi/do_action":
                case "gamestate/guobiao/show_result":
                case "gamestate/qingque/show_result":
                case "gamestate/classical/show_result":
                case "gamestate/riichi/show_result":
                case "gamestate/guobiao/game_end":
                case "gamestate/qingque/game_end":
                case "gamestate/classical/game_end":
                case "gamestate/riichi/game_end":
                case "gamestate/guobiao/ready_status":
                case "gamestate/qingque/ready_status":
                case "gamestate/classical/ready_status":
                case "gamestate/riichi/ready_status":
                case "gamestate/classical/show_shuhewei":
                case "gamestate/riichi/declare_riichi":
                case "gamestate/riichi/update_dora":
                case "gamestate/sichuan/game_start":
                case "gamestate/sichuan/broadcast_hand_action":
                case "gamestate/sichuan/ask_other_action":
                case "gamestate/sichuan/do_action":
                case "gamestate/sichuan/show_result":
                case "gamestate/sichuan/game_end":
                case "gamestate/sichuan/ready_status":
                case "gamestate/sichuan/ask_dingque":
                case "gamestate/sichuan/dingque_done":
                case "gamestate/changsha/game_start":
                case "gamestate/changsha/broadcast_hand_action":
                case "gamestate/changsha/ask_other_action":
                case "gamestate/changsha/do_action":
                case "gamestate/changsha/show_result":
                case "gamestate/changsha/game_end":
                case "gamestate/changsha/ready_status":
                case "gamestate/jiandan/game_start":
                case "gamestate/jiandan/broadcast_hand_action":
                case "gamestate/jiandan/ask_other_action":
                case "gamestate/jiandan/do_action":
                case "gamestate/jiandan/show_result":
                case "gamestate/jiandan/game_end":
                case "gamestate/jiandan/ready_status":
                case "switch_seat":
                case "refresh_player_tag_list":
                case "gamestate/broadcast_sticker":
                case "gamestate/vote_update":
                case "gamestate/vote_end":
                    GameStateNetworkManager.Instance.HandleGameStateMessage(response);
                    break;
                // 匹配系统消息交由 MatchNetworkManager 处理
                case "match/join_queue_done":
                case "match/leave_queue_done":
                case "match/queue_status":
                case "match/match_found":
                    MatchNetworkManager.Instance.HandleMatchMessage(response);
                    break;

                case "get_player_info":
                    Debug.Log($"收到玩家信息: {response.message}");
                    NotificationManager.Instance.OpenPlayerInfoPanel(response.success, response.message, response.player_info);
                    break;

                case "game_tip":
                    NotificationManager.Instance.ShowTip("验证", false, response.message);
                    break;

                case "tips":
                    // 服务端验证失败的提示并重置按钮
                    // AutoReconnect 时，视为登录失败信号
                    if (AutoReconnect.IsActive) {
                        AutoReconnect.OnLoginResponse(false);
                    }
                    NotificationManager.Instance.ShowTip("验证", false, response.message);
                    LoginPanel.Instance.ResetLoginButton();
                    break;

                case "message":
                    // 处理服务端发送的消息（版本不匹配、账户被顶替、重连提示等）
                    // AutoReconnect 时静默接受 reconnect_ask
                    if (response.message == "reconnect_ask" && AutoReconnect.ShouldAutoAcceptReconnectAsk()) {
                        ReconnectResponse(true);
                        break;
                    }
                    // 传入 title, content 以及 message 标识符 (error_version/login_kickout/reconnect_ask)
                    if (response.message == "error_version") {
                        _disconnectDialogState = DisconnectDialogState.NoMatch;
                    } else if (response.message == "login_kickout") {
                        _disconnectDialogState = DisconnectDialogState.Shown;
                    }
                    NotificationManager.Instance.ShowMessage(
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

    // ========== 观战消息处理 ==========

    /// <summary>
    /// 服务端下发初始牌谱（add_spectator 成功后的 record_init）：先切 game 窗，再 StartSpectating。
    /// 不在点击观战按钮时切页，仅由本消息驱动。
    /// </summary>
    private void HandleSpectatorRecordInit(Response response) {
        if (!response.success) {
            Debug.LogWarning($"观战初始数据失败: {response.message}");
            GameRecordManager.Instance?.ClearDelayedSpectatorSession();
            return;
        }
        if (!AcceptDelayedSpectatorPayload(response)) {
            Debug.Log("忽略非当前会话的延时观战 record_init");
            return;
        }
        string recordJson = response.message_info?.content;
        if (string.IsNullOrEmpty(recordJson)) {
            Debug.LogError("观战初始数据为空");
            GameRecordManager.Instance?.ClearDelayedSpectatorSession();
            return;
        }
        StartCoroutine(CoEnterDelayedSpectatorFromRecordInit(recordJson));
    }

    private IEnumerator CoEnterDelayedSpectatorFromRecordInit(string recordJson) {
        if (BlocksIncomingDelayedSpectatorSession()) {
            Debug.Log("忽略延时观战 record_init：当前已在匹配或对局/其它观战会话中");
            GameRecordManager.Instance?.AbandonDelayedSpectatorSessionOnServer();
            yield break;
        }

        WindowsManager.Instance.SwitchWindow("game");
        yield return null;

        var grm = GameRecordManager.Instance;
        if (grm == null) {
            Debug.LogError("观战 record_init：GameRecordManager 未就绪");
            yield break;
        }

        grm.StartSpectating(recordJson);
        if (!grm.IsSpectating) {
            grm.AbandonDelayedSpectatorSessionOnServer();
        }
    }

    private void HandleSpectatorRecordUpdate(Response response) {
        if (!response.success || !AcceptDelayedSpectatorPayload(response)) return;
        string updatesJson = response.message_info?.content;
        if (string.IsNullOrEmpty(updatesJson)) return;
        GameRecordManager.Instance?.AppendSpectatorTicks(updatesJson);
    }

    private void HandleSpectatorRecordComplete(Response response) {
        if (GameRecordManager.Instance == null) return;
        if (!GameRecordManager.Instance.IsSpectating || !AcceptDelayedSpectatorPayload(response)) {
            Debug.Log("忽略迟到/非本场的延时观战 record_complete");
            return;
        }

        string msg = string.IsNullOrEmpty(response.message) ? "游戏对局结束，已获取全部对局记录" : response.message;
        NotificationManager.Instance.ShowTip("观战", true, msg);

        string recordJson = response.message_info?.content;
        if (!string.IsNullOrEmpty(recordJson)) {
            try {
                GameRecordManager.Instance.StartSpectating(recordJson);
            } catch (Exception e) {
                Debug.LogError($"加载完整观战牌谱失败: {e.Message}");
            }
        }
        if (GameRecordManager.Instance.IsSpectating) {
            GameRecordManager.Instance.SwitchToRecordMode();
        }
        GameRecordManager.Instance.ClearDelayedSpectatorSession();
    }

    private void HandleSpectatorAddResult(Response response) {
        if (response.success) {
            NotificationManager.Instance.ShowTip("观战", true, response.message);
        } else {
            GameRecordManager.Instance?.ClearDelayedSpectatorSession();
            NotificationManager.Instance.ShowTip("观战", false, response.message);
        }
    }

    private void HandleSpectatorRemoveResult(Response response) {
        Debug.Log($"观战移除: {response.message}");
        // 客户端退出时已 StopSpectating；迟到的上一场 remove 回包不得踢掉当前观战
        if (GameRecordManager.Instance != null && GameRecordManager.Instance.IsSpectating) return;
        GameRecordManager.Instance?.ClearDelayedSpectatorSession();
    }

    /// <summary>message_info.title 必须为当前延时观战的 gamestate_id。</summary>
    private static bool AcceptDelayedSpectatorPayload(Response response) {
        var grm = GameRecordManager.Instance;
        if (grm == null) return false;
        return grm.IsCurrentDelayedSpectator(response.message_info?.title);
    }

    /// <summary>
    /// 是否应拒绝延时观战 record_init：不含「已发出 add_spectator、等待 record_init」的待加入状态。
    /// </summary>
    private static bool BlocksIncomingDelayedSpectatorSession() {
        if (LobbyStateGuard.IsInMatchQueue) return true;
        var gsm = NormalGameStateManager.Instance;
        if (gsm != null && (gsm.IsGameActive || gsm.IsRealtimeSpectator)) return true;
        var grm = GameRecordManager.Instance;
        if (grm != null && grm.IsSpectating) return true;
        return false;
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
            if (_disconnectDialogState == DisconnectDialogState.Start) {
                _disconnectDialogState = DisconnectDialogState.Connected;
            }
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

    // 房间相关方法已移至 RoomNetworkManager
    // 游戏状态相关方法已移至 GameStateNetworkManager

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
        if (websocket != null && websocket.State == WebSocketState.Open)
            await websocket.Close();
    }
    // 添加机器人方法已移至 RoomNetworkManager
}
