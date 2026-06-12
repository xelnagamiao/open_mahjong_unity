using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 段位匹配主面板：队列人数按钮等。排队中 UI 见 <see cref="MatchQueueingPanel"/>；匹配成功倒计时见 OverlayCanvas 上的 <see cref="MatchFoundedPanel"/>。
/// </summary>
public class MatchPanel : MonoBehaviour {
    public static MatchPanel Instance { get; private set; }

    public MatchButton[] matchButtons;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        matchButtons = GetComponentsInChildren<MatchButton>(true);
    }

    private void OnEnable() {
        if (matchButtons == null || matchButtons.Length == 0) {
            matchButtons = GetComponentsInChildren<MatchButton>(true);
        }
        RefreshAllButtons();
        NetworkPollingManager.Instance.StartMatchQueuePolling();
        // 切回匹配窗口时，若仍在排队则恢复排队面板（成功面板在 OverlayCanvas，不随本窗口显隐）
        MatchQueueingPanel.Instance?.RestoreIfQueueing();
    }

    private void OnDisable() {
        NetworkPollingManager.Instance.StopMatchQueuePolling();
        // 仅隐藏视图，不结束排队状态：排队计时由常驻的 MatchStateManager 继续维护
        MatchQueueingPanel.Instance?.HideImmediately();
    }

    private void RefreshAllButtons() {
        foreach (var btn in matchButtons) {
            btn.RefreshMask();
        }
    }

    /// <summary>
    /// 由 MatchNetworkManager 调用，更新所有按钮的人数显示
    /// </summary>
    public void UpdateQueueStatus(Dictionary<string, QueueStatusEntry> queueStatus) {
        if (queueStatus == null) return;
        if (matchButtons == null || matchButtons.Length == 0) {
            matchButtons = GetComponentsInChildren<MatchButton>(true);
        }
        foreach (var btn in matchButtons) {
            if (queueStatus.TryGetValue(btn.QueueType, out QueueStatusEntry entry)) {
                btn.UpdateCounts(entry.waiting, entry.playing);
            } else {
                btn.UpdateCounts(0, 0);
            }
        }
    }
}
