using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class MatchPanel : MonoBehaviour {
    public static MatchPanel Instance { get; private set; }

    [Header("匹配成功动画")]
    [SerializeField] private GameObject matchFoundPanel;
    [SerializeField] private PanelPopupTransition matchFoundPopup;
    [SerializeField] private TMP_Text matchFoundTypeText;
    [SerializeField] private TMP_Text matchFoundCountdownText;

    public MatchButton[] matchButtons;
    private Coroutine countdownCoroutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        matchButtons = GetComponentsInChildren<MatchButton>(true);
        if (matchFoundPanel != null) matchFoundPanel.SetActive(false);
    }

    private void OnEnable() {
        if (matchButtons == null || matchButtons.Length == 0) {
            matchButtons = GetComponentsInChildren<MatchButton>(true);
        }
        RefreshAllButtons();
        NetworkPollingManager.Instance.StartMatchQueuePolling();
    }

    private void OnDisable() {
        NetworkPollingManager.Instance.StopMatchQueuePolling();
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

    /// <summary>
    /// 匹配成功动画：头部显示类型，底部显示 5 秒倒计时
    /// </summary>
    public void ShowMatchFoundAnimation(string matchTypeName) {
        if (matchFoundPanel == null && matchFoundPopup == null) return;
        MatchingWidget.Instance?.Hide();
        if (matchFoundPopup != null) {
            matchFoundPopup.Show();
        } else {
            matchFoundPanel.SetActive(true);
        }
        matchFoundTypeText.text = matchTypeName;
        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        countdownCoroutine = StartCoroutine(MatchFoundCountdown());
    }

    private IEnumerator MatchFoundCountdown() {
        for (int i = 5; i > 0; i--) {
            matchFoundCountdownText.text = $"{i} 秒后进入游戏...";
            yield return new WaitForSeconds(1f);
        }
        if (matchFoundPopup != null) {
            matchFoundPopup.Hide();
        } else if (matchFoundPanel != null) {
            matchFoundPanel.SetActive(false);
        }
    }
}
