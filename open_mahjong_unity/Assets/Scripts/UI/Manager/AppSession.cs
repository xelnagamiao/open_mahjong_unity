using UnityEngine;

/// <summary>
/// 应用会话边界。所有“回到登录页”的场景只允许从这里进入：
/// 退出公开牌谱、主动登出、断线后放弃当前会话。
/// </summary>
public static class AppSession {
    public static void ReturnToLogin() {
        Debug.Log("[AppSession] 结束当前会话并返回登录页");

        AutoReconnect.CancelForSessionReset();
        SharedRecordLink.EndPublicSharePlayback();

        // 先稳定到唯一的登录布局。后续清理即使遇到已失活的游戏对象，
        // 也不会把用户留在半退出的牌谱界面。
        WindowsManager.Instance?.ResetToLoginUI();
        LoginPanel.Instance?.ShowConnectingState();

        try {
            ResetLocalSessionState();
        } catch (System.Exception e) {
            // 即使游戏对象已在退出途中被销毁，也必须继续恢复登录与网络。
            Debug.LogWarning($"[AppSession] 清理本地会话时出错: {e.Message}");
        }

        UserDataManager.Instance?.ClearSessionState();
        UnreadBadgeStore.BindUser(0);
        HeaderPanel.Instance?.SetBackToGameVisible(false);
        HeaderPanel.Instance?.RefreshMatchButtonVisibility();

        NetworkManager.Instance?.RestartLoginConnection();
    }

    private static void ResetLocalSessionState() {
        NormalGameStateManager.Instance?.StopAsRealtimeSpectator();
        if (GameRecordManager.Instance != null) {
            GameRecordManager.Instance.ResetForSessionEnd();
        } else {
            GameRecordManager.ClearDelayedSpectatorSession();
        }
        GameSceneTeardown.ResetToIdle();
        MatchNetworkManager.Instance?.ClearLocalMatchState();
    }

    /// <summary>
    /// 断线面板 B 按钮：Web 与重连一致；Windows 独立版退出应用。
    /// </summary>
    public static void QuitOrReconnectOnDisconnectClose() {
#if UNITY_WEBGL && !UNITY_EDITOR
        ReturnToLogin();
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
        UnityEngine.Application.Quit();
#elif UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        UnityEngine.Application.Quit();
#endif
    }
}
