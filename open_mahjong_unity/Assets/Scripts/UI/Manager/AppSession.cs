using System;
using UnityEngine;

/// <summary>
/// 会话级软重置：清理对局/UI 状态，回到登录界面并重新建立 WebSocket。
/// </summary>
public static class AppSession {
    public static void ResetToLogin() {
        try {
            Debug.Log("[AppSession] 开始软重置到登录界面");
            WindowsManager.Instance?.ResetToLoginUI();
            LoginPanel.Instance?.PrepareForReconnect();

            // 优先发起 WebSocket 重连，避免后续清理逻辑异常导致跳过
            NetworkManager.Instance?.RequestReconnectWebSocket();

            try {
                NormalGameStateManager.Instance?.StopAsRealtimeSpectator();

                if (GameRecordManager.Instance != null && GameRecordManager.Instance.IsSpectating) {
                    GameRecordManager.Instance.StopSpectating();
                } else {
                    GameRecordManager.Instance?.AbandonDelayedSpectatorSessionOnServer();
                    GameSceneTeardown.ResetToIdle();
                }

                UserDataManager.Instance?.ClearSessionState();
                HeaderPanel.Instance?.SetBackToGameVisible(false);
                HeaderPanel.Instance?.RefreshMatchButtonVisibility();
            } catch (Exception cleanupEx) {
                Debug.LogWarning($"[AppSession] 清理会话状态时出错（重连已发起）: {cleanupEx.Message}");
            }
        } catch (Exception e) {
            Debug.LogError($"[AppSession] ResetToLogin 失败: {e.Message}\n{e.StackTrace}");
        }
    }

    /// <summary>
    /// 断线面板 B 按钮：Web 与重连一致；Windows 独立版退出应用。
    /// </summary>
    public static void QuitOrReconnectOnDisconnectClose() {
#if UNITY_WEBGL && !UNITY_EDITOR
        ResetToLogin();
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
        UnityEngine.Application.Quit();
#elif UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        UnityEngine.Application.Quit();
#endif
    }
}
