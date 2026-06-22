using System;
using System.Collections;
using UnityEngine;
using NativeWebSocket;

// 在 WebSocket 静默断开后，应用回到前台时自动重连登录并恢复对局
public static class AutoReconnect {
    private enum State {
        Idle,
        Background,
        Reconnecting,
    }

    private struct SessionSnapshot {
        public int UserId;
        public bool IsTourist;
        public bool WasLoggedIn;
        public bool WasInGame;
        public string Username;
        public string Password;
    }

    private class WaitState {
        public bool LoginDone;
        public bool LoginSuccess;
        public bool RoomSyncDone;
        public bool ReconnectAskReceived;
        public bool GameRestored;
    }

    private static State _state = State.Idle;
    private static SessionSnapshot _snapshot;
    private static WaitState _waitState;
    private static bool _backgroundDisconnectDetected;
    private static int _retryCycle;

    public static bool IsActive => _state == State.Reconnecting;
    public static bool ExpectGameRestore { get; private set; }

    public static bool IsEnabled {
        get {
#if UNITY_ANDROID && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    public static void OnEnterBackground() {
        if (!IsEnabled) return;
        if (_state != State.Idle) return;
        CaptureSnapshot();
        if (_snapshot.WasLoggedIn && !_snapshot.IsTourist) {
            _state = State.Background;
            Debug.Log(
                "[AutoReconnect] 进入后台，已保存会话快照"
                + $" (userId={_snapshot.UserId}, wasInGame={_snapshot.WasInGame})");
        }
    }

    public static void OnEnterForeground() {
        if (!IsEnabled) return;
        Debug.Log(
            $"[AutoReconnect] 回到前台"
            + $" (state={_state}, onCloseFlag={_backgroundDisconnectDetected})"
        );
        _backgroundDisconnectDetected = false;
        if (_state != State.Background) return;
        if (!_snapshot.WasLoggedIn || _snapshot.IsTourist) {
            _state = State.Idle;
            return;
        }
        StartAutoReconnect();
    }

    public static bool TryHandleForegroundDisconnect() {
        if (!IsEnabled) return false;
        if (_state != State.Background) return false;
        if (!_snapshot.WasLoggedIn || _snapshot.IsTourist) return false;
        Debug.Log("[AutoReconnect] 尝试自动重连");
        StartAutoReconnect();
        return true;
    }

    // OnClose 回调中调用，返回 true 表示已接管，调用方跳过正常断线 UI。
    public static bool TryHandleOnClose() {
        if (_state == State.Reconnecting) {
            Debug.Log("[AutoReconnect] 重连期间 WebSocket 再次关闭，由自动重连协程接管");
            return true;
        }
        if (_state == State.Background) {
            _backgroundDisconnectDetected = true;
            Debug.Log("[AutoReconnect] 后台期间 WebSocket 关闭，待前台恢复时自动重连");
            return true;
        }
        return false;
    }

    public static void OnLoginResponse(bool success) {
        if (_state != State.Reconnecting || _waitState == null) return;
        _waitState.LoginDone = true;
        _waitState.LoginSuccess = success;
        Debug.Log("[AutoReconnect] 登录" + (success ? "成功" : "失败"));
    }

    public static void OnRoomSyncDone() {
        if (_state != State.Reconnecting || _waitState == null) return;
        _waitState.RoomSyncDone = true;
        Debug.Log("[AutoReconnect] 房间状态已同步");
    }

    public static bool ShouldAutoAcceptReconnectAsk() {
        if (_state != State.Reconnecting || !ExpectGameRestore) return false;
        if (_waitState != null) _waitState.ReconnectAskReceived = true;
        Debug.Log("[AutoReconnect] 自动接受 reconnect_ask");
        return true;
    }

    public static void OnGameRestored() {
        if (_state != State.Reconnecting || _waitState == null) return;
        _waitState.GameRestored = true;
        Debug.Log("[AutoReconnect] 成功重回对局");
    }

    public static bool ShouldSuppressLoginTip() {
        return _state == State.Reconnecting;
    }

    private static void CaptureSnapshot() {
        var udm = UserDataManager.Instance;
        if (udm == null) {
            _snapshot = default;
            return;
        }

        string currentWindow = WindowsManager.Instance != null
            ? WindowsManager.Instance.GetCurrentWindow()
            : "";

        bool isInGame = !string.IsNullOrEmpty(udm.GamestateId) || currentWindow == "game";

        _snapshot = new SessionSnapshot {
            UserId = udm.UserId,
            IsTourist = udm.IsTourist,
            WasLoggedIn = udm.UserId != 0,
            WasInGame = isInGame,
            Username = udm.SavedLoginUsername ?? "",
            Password = udm.SavedLoginPassword ?? "",
        };
    }

    private static void StartAutoReconnect() {
        if (_state == State.Reconnecting) return;
        if (!_snapshot.WasLoggedIn || _snapshot.IsTourist) {
            _state = State.Idle;
            return;
        }

        _state = State.Reconnecting;
        ExpectGameRestore = _snapshot.WasInGame;

        if (ExpectGameRestore) {
            GameSceneTeardown.ResetToIdle();
            WindowsManager.Instance?.SwitchWindow("menu");
        }

        NotificationManager.Instance?.ShowTip("重连", true, "正在恢复连接…");

        CoroutineManager.Ensure();
        CoroutineManager.Instance.RunNamed(
            CoroutineKeys.NetworkAutoReconnect,
            AutoReconnectRoutine(),
            restartIfRunning: true
        );
    }

    private static IEnumerator AutoReconnectRoutine()
    {
        _waitState = new WaitState();
        Debug.Log($"[AutoReconnect] 开始重连 (expectGame={ExpectGameRestore})");

        var nm = NetworkManager.Instance;
        if (nm == null) { OnFailed("内部错误"); yield break; }

        bool skipReconnect = false;

        // Phase 0: 快速探活
        nm.ResetProbePongFlag();
        bool probeSent = nm.SendProbePing();
        Debug.Log($"[AutoReconnect] [0/4] 快速探活 pingSent={probeSent}");
        if (probeSent)
        {
            const float probeTimeout = 2f;
            float probeElapsed = 0f;
            while (probeElapsed < probeTimeout)
            {
                yield return null;
                if (_state != State.Reconnecting) yield break;
                if (nm.ProbePongReceived)
                {
                    if (!ExpectGameRestore)
                    {
                        Debug.Log("[AutoReconnect] 旧连接存活，跳过重连");
                        OnSucceeded(skipAllTips: true);
                        yield break;
                    }
                    Debug.Log("[AutoReconnect] 旧连接存活，仍需登录同步状态");
                    skipReconnect = true;
                    break;
                }
                if (_backgroundDisconnectDetected)
                {
                    _backgroundDisconnectDetected = false;
                    Debug.Log("[AutoReconnect] 检测到断线，继续重连");
                    break;
                }
                probeElapsed += Time.unscaledDeltaTime;
            }
        }

        // Phase 1: 重连 WebSocket（探活成功且需恢复对局时跳过）
        if (!skipReconnect)
        {
            const int maxAttempts = 2;
            bool connected = false;
            WebSocket newSocket = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (attempt > 1)
                {
                    float retryDelay = 1.5f;
                    Debug.Log($"[AutoReconnect] 重试前等待 {retryDelay}s");
                    while (retryDelay > 0f)
                    {
                        yield return null;
                        if (_state != State.Reconnecting) yield break;
                        retryDelay -= Time.unscaledDeltaTime;
                    }
                }
                Debug.Log($"[AutoReconnect] [1/4] 第{attempt}/{maxAttempts}次尝试连接");
                var oldWs = nm.GetWebSocket();
                if (oldWs != null && oldWs != newSocket)
                {
                    try { oldWs.Close(); } catch { }
                    yield return null;
                }
                newSocket = nm.BeginNewConnection();
                if (newSocket == null)
                {
                    OnFailed("无法创建连接");
                    yield break;
                }
                const float connectTimeout = 10f;
                float elapsed = 0f;
                while (elapsed < connectTimeout)
                {
#if !UNITY_WEBGL || UNITY_EDITOR
                    newSocket.DispatchMessageQueue();
#endif
                    if (newSocket.State == WebSocketState.Open)
                    {
                        connected = true;
                        break;
                    }
                    if (_state != State.Reconnecting) yield break;
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (connected) break;
                Debug.LogWarning($"[AutoReconnect] 第{attempt}/{maxAttempts}次连接失败(State={newSocket.State})");
            }
            if (!connected)
            {
                Debug.LogWarning("[AutoReconnect] 所有连接尝试均失败");
                OnFailed("无法连接服务器");
                yield break;
            }
        }
        Debug.Log("[AutoReconnect] WebSocket 已连接");

        // Phase 2: 重新登录
        Debug.Log("[AutoReconnect] [2/4] 尝试重新登录");
        nm.Login(_snapshot.Username, _snapshot.Password);
        const float loginTimeout = 12f;
        float loginElapsed = 0f;
        while (loginElapsed < loginTimeout && !_waitState.LoginDone)
        {
            if (_state != State.Reconnecting) yield break;
            loginElapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!_waitState.LoginSuccess)
        {
            OnFailed("登录失败");
            yield break;
        }
        Debug.Log("[AutoReconnect] 登录成功");

        // Phase 3: 同步房间状态
        Debug.Log("[AutoReconnect] [3/4] 同步房间状态");
        RoomNetworkManager.Instance?.SyncMyRoom();
        const float roomSyncTimeout = 5f;
        float roomSyncElapsed = 0f;
        while (roomSyncElapsed < roomSyncTimeout && !_waitState.RoomSyncDone)
        {
            if (_state != State.Reconnecting) yield break;
            roomSyncElapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (!_waitState.RoomSyncDone)
        {
            Debug.LogWarning("[AutoReconnect] 房间同步超时，清理残留房间状态");
            if (ExpectGameRestore) {
                RoomNetworkManager.Instance?.ClearStaleLobbyState();
            } else {
                RoomNetworkManager.Instance?.ApplyLeftRoomState(silent: true);
            }
            _waitState.RoomSyncDone = true;
        }

        // Phase 4: 恢复对局（仅收到 game_start 后才进桌）
        if (ExpectGameRestore)
        {
            Debug.Log("[AutoReconnect] [4/4] 等待对局");
            const float reconnectAskTimeout = 3f;
            const float gameRestoreTimeout = 12f;
            float waited = 0f;
            while (waited < reconnectAskTimeout && !_waitState.ReconnectAskReceived)
            {
                if (_state != State.Reconnecting) yield break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!_waitState.ReconnectAskReceived)
            {
                Debug.LogWarning("[AutoReconnect] 未收到 reconnect_ask，对局已不存在");
                UserDataManager.Instance?.SetGamestateId("");
                UserDataManager.Instance?.SetRoomId(UserDataManager.ROOM_ID_NONE);
                GameSceneTeardown.ResetToIdle();
                WindowsManager.Instance?.SwitchWindow("menu");
                NotificationManager.Instance?.ShowTip("重连", false, "对局已结束");
                OnSucceeded(skipAllTips: true);
                yield break;
            }
            waited = 0f;
            while (waited < gameRestoreTimeout && !_waitState.GameRestored)
            {
                if (_state != State.Reconnecting) yield break;
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!_waitState.GameRestored)
            {
                Debug.LogWarning("[AutoReconnect] 重回对局超时，清理残留会话状态");
                UserDataManager.Instance?.SetGamestateId("");
                UserDataManager.Instance?.SetRoomId(UserDataManager.ROOM_ID_NONE);
                GameSceneTeardown.ResetToIdle();
                WindowsManager.Instance?.SwitchWindow("menu");
                NotificationManager.Instance?.ShowTip("重连", false, "重回对局超时");
                OnSucceeded(skipAllTips: true);
                yield break;
            }
        }

        Debug.Log("[AutoReconnect] 完成重连");
        OnSucceeded();
    }

    private static void OnSucceeded(bool skipAllTips = false) {
        _state = State.Idle;
        ExpectGameRestore = false;
        _waitState = null;
        _backgroundDisconnectDetected = false;
        _retryCycle = 0;
        if (!skipAllTips) {
            NotificationManager.Instance?.ShowTip("重连", true, "已自动恢复连接");
        }
    }

    // 重连失败：延迟重试（最多 2 个周期），耗尽后回退到正常断线弹窗
    private static void OnFailed(string reason) {
        Debug.LogWarning($"[AutoReconnect] 重连失败: {reason} (周期 {_retryCycle})");
        if (_retryCycle < 2) {
            _retryCycle++;
            _state = State.Background;
            _waitState = null;
            _backgroundDisconnectDetected = false;
            CoroutineManager.Ensure();
            CoroutineManager.Instance.RunNamed(
                CoroutineKeys.NetworkAutoReconnect,
                DeferredRetryRoutine(_retryCycle),
                restartIfRunning: true
            );
            return;
        }
        _state = State.Idle;
        ExpectGameRestore = false;
        _waitState = null;
        _backgroundDisconnectDetected = false;
        _retryCycle = 0;
        var nm = NetworkManager.Instance;
        if (nm != null) {
            nm.ForceShowDisconnectDialog();
        }
    }

    private static IEnumerator DeferredRetryRoutine(int cycle) {
        float delay = cycle <= 1 ? 3f : 8f;
        Debug.Log($"[AutoReconnect] 延迟 {delay}s 后重试 (周期 {cycle})");
        NotificationManager.Instance?.ShowTip("重连", true, $"正在恢复连接…（第{cycle + 1}次尝试）");
        while (delay > 0f) {
            yield return null;
            if (_state == State.Idle) yield break;
            delay -= Time.unscaledDeltaTime;
        }
        if (_state == State.Background && _snapshot.WasLoggedIn) {
            StartAutoReconnect();
        }
    }
}
