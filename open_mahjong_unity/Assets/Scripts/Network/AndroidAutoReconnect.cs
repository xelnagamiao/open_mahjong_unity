using System;
using System.Collections;
using UnityEngine;
using NativeWebSocket;

/// <summary>
/// Android 切后台导致 WebSocket 断开时，回前台自动重连 WebSocket 并静默登录、回局。
/// 仅对已登录的非游客账户生效；游客账户服务端断线后会删除，无法恢复对局。
/// </summary>
public static class AndroidAutoReconnect {
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
        public bool ReconnectAskReceived;
        public bool GameRestored;
    }

    private static SessionSnapshot _snapshot;
    private static WaitState _waitState;
    private static bool _inBackground;
    private static bool _pendingDisconnect;

    public static bool IsActive { get; private set; }
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

    public static bool ShouldSuppressDisconnectUi() {
        return IsEnabled && (_inBackground || IsActive || _pendingDisconnect);
    }

    public static void OnEnterBackground() {
        if (!IsEnabled) return;
        if (_inBackground) return;
        _inBackground = true;
        CaptureSnapshot();
        Debug.Log("[AndroidAutoReconnect] 进入后台，已保存会话快照");
    }

    public static void OnEnterForeground() {
        if (!IsEnabled) return;
        _inBackground = false;
        ScheduleTryReconnect();
    }

    public static void OnBackgroundDisconnect() {
        if (!IsEnabled) return;
        _pendingDisconnect = true;
        Debug.Log("[AndroidAutoReconnect] 后台期间 WebSocket 已断开");
    }

    /// <summary>
    /// 前台恢复时检测到连接已断，尝试自动重连（替代弹窗断线提示）。
    /// </summary>
    public static bool TryStartOnForegroundDisconnect() {
        if (!IsEnabled) return false;
        if (!_snapshot.WasLoggedIn || _snapshot.IsTourist) return false;
        _pendingDisconnect = true;
        ScheduleTryReconnect();
        return true;
    }

    public static void OnLoginResponse(bool success) {
        if (!IsActive) return;
        _waitState.LoginDone = true;
        _waitState.LoginSuccess = success;
    }

    public static void OnReconnectAsk() {
        if (!IsActive) return;
        _waitState.ReconnectAskReceived = true;
    }

    public static void OnGameRestored() {
        if (!IsActive) return;
        _waitState.GameRestored = true;
    }

    public static bool ShouldSkipLoginUiChanges() {
        return IsActive && ExpectGameRestore;
    }

    public static bool ShouldAutoAcceptReconnectAsk() {
        return IsActive && ExpectGameRestore;
    }

    public static bool ShouldSuppressLoginTip() {
        return IsActive;
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

        _snapshot = new SessionSnapshot {
            UserId = udm.UserId,
            IsTourist = udm.IsTourist,
            WasLoggedIn = udm.UserId != 0,
            WasInGame = !string.IsNullOrEmpty(udm.GamestateId) || currentWindow == "game",
            Username = udm.SavedLoginUsername ?? "",
            Password = udm.SavedLoginPassword ?? "",
        };
    }

    private static void ScheduleTryReconnect() {
        if (!IsEnabled) return;
        if (!_pendingDisconnect && IsSocketOpen()) return;
        if (!_snapshot.WasLoggedIn || _snapshot.IsTourist) return;

        CoroutineManager.Ensure();
        if (CoroutineManager.Instance.IsNamedRunning(CoroutineKeys.NetworkAndroidAutoReconnect)) {
            return;
        }

        CoroutineManager.Instance.RunNamed(
            CoroutineKeys.NetworkAndroidAutoReconnect,
            AutoReconnectRoutine(),
            restartIfRunning: false
        );
    }

    private static bool IsSocketOpen() {
        var ws = NetworkManager.Instance?.GetWebSocket();
        return ws != null && ws.State == WebSocketState.Open;
    }

    private static IEnumerator AutoReconnectRoutine() {
        IsActive = true;
        ExpectGameRestore = _snapshot.WasInGame;
        _waitState = new WaitState();

        Debug.Log("[AndroidAutoReconnect] 开始自动重连");

        if (!IsSocketOpen()) {
            NetworkManager.Instance.RequestReconnectWebSocket();

            const float connectTimeout = 15f;
            float elapsed = 0f;
            while (elapsed < connectTimeout) {
                if (IsSocketOpen()) break;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!IsSocketOpen()) {
                Debug.LogWarning("[AndroidAutoReconnect] WebSocket 重连超时");
                Finish(false);
                yield break;
            }
        }

        _pendingDisconnect = false;

        NetworkManager.Instance.Login(_snapshot.Username, _snapshot.Password);

        const float loginTimeout = 12f;
        float loginElapsed = 0f;
        while (loginElapsed < loginTimeout && !_waitState.LoginDone) {
            loginElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!_waitState.LoginSuccess) {
            Debug.LogWarning("[AndroidAutoReconnect] 自动登录失败或超时");
            Finish(false);
            yield break;
        }

        if (ExpectGameRestore) {
            const float gameTimeout = 18f;
            float gameElapsed = 0f;
            while (gameElapsed < gameTimeout && !_waitState.GameRestored) {
                if (_waitState.ReconnectAskReceived) {
                    NetworkManager.Instance.ReconnectResponse(true);
                    _waitState.ReconnectAskReceived = false;
                }
                gameElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!_waitState.GameRestored) {
                Debug.LogWarning("[AndroidAutoReconnect] 回局超时");
                Finish(false);
                yield break;
            }
        }

        Debug.Log("[AndroidAutoReconnect] 自动重连成功");
        NotificationManager.Instance?.ShowTip("重连", true, "已自动重连");
        Finish(true);
    }

    private static void Finish(bool success) {
        IsActive = false;
        ExpectGameRestore = false;
        _pendingDisconnect = false;
        _waitState = null;

        if (!success) {
            NetworkManager.Instance?.ShowDisconnectDialogAfterAutoReconnectFailed();
        }
    }
}
