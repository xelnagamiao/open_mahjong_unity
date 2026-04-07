using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class MatchPanel : MonoBehaviour {
    public static MatchPanel Instance { get; private set; }

    [Header("匹配成功动画")]
    [SerializeField] private GameObject matchFoundPanel;
    [SerializeField] private TMP_Text matchFoundTypeText;
    [SerializeField] private TMP_Text matchFoundCountdownText;

    private MatchButton[] matchButtons;
    private Coroutine refreshCoroutine;
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
        RefreshAllButtons();
        MatchNetworkManager.Instance?.SendGetQueueStatus();
        if (refreshCoroutine != null) StopCoroutine(refreshCoroutine);
        refreshCoroutine = StartCoroutine(RefreshQueueStatusRoutine());
    }

    private void OnDisable() {
        if (refreshCoroutine != null) {
            StopCoroutine(refreshCoroutine);
            refreshCoroutine = null;
        }
    }

    private IEnumerator RefreshQueueStatusRoutine() {
        while (true) {
            yield return new WaitForSeconds(5f);
            MatchNetworkManager.Instance?.SendGetQueueStatus();
        }
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
        foreach (var btn in matchButtons) {
            if (queueStatus.TryGetValue(btn.QueueType, out QueueStatusEntry entry)) {
                btn.UpdateCounts(entry.waiting, entry.playing);
            }
        }
    }

    /// <summary>
    /// 匹配成功动画：头部显示类型，底部显示 5 秒倒计时
    /// </summary>
    public void ShowMatchFoundAnimation(string matchTypeName) {
        if (matchFoundPanel == null) return;
        matchFoundPanel.SetActive(true);
        matchFoundTypeText.text = matchTypeName;
        if (countdownCoroutine != null) StopCoroutine(countdownCoroutine);
        countdownCoroutine = StartCoroutine(MatchFoundCountdown());
    }

    private IEnumerator MatchFoundCountdown() {
        for (int i = 5; i > 0; i--) {
            matchFoundCountdownText.text = $"{i} 秒后进入游戏...";
            yield return new WaitForSeconds(1f);
        }
        matchFoundPanel.SetActive(false);
    }
}
