using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MeunPanel : MonoBehaviour {
    public static MeunPanel Instance { get; private set; }

    [Header("排位匹配入口")]
    [SerializeField] private Button matchEntryButton;
    [SerializeField] private TMP_Text matchPlayerCountText;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start() {
        if (matchEntryButton != null) {
            matchEntryButton.onClick.RemoveListener(GoToMatch);
            matchEntryButton.onClick.AddListener(GoToMatch);
        }
    }

    private void OnEnable() {
        UpdateMatchPlayerCountText(0);
        NetworkPollingManager.Instance.StartMenuMatchPlayerCountPolling();
    }

    private void OnDisable() {
        NetworkPollingManager.Instance.StopMenuMatchPlayerCountPolling();
    }

    // 设置用户信息（通过UserContainer处理UI，UserDataManager管理数据）
    public void SetUserInfo(string username, string userkey, int user_id, bool isTourist = false) {
        UserContainer.Instance.SetUserInfo(username, userkey, user_id, isTourist);
    }

    // 显示服务器统计信息（通过NowPlayer显示）
    public void DisplayServerStats(int onlinePlayerCount, int waitingRoomCount, int playingRoomCount, int matchPlayingGames) {
        NowPlayer.Instance.DisplayServerStats(onlinePlayerCount, waitingRoomCount, playingRoomCount, matchPlayingGames);
    }

    public void GoToMatch() {
        // 对局/房间中允许查阅匹配页人数；真正加入队列时再由 MatchNetworkManager 拦截
        WindowsManager.Instance.SwitchWindow("match");
    }

    public void UpdateMatchPlayerCount(Dictionary<string, QueueStatusEntry> queueStatus) {
        UpdateMatchPlayerCountText(CountTotalMatchPlayers(queueStatus));
    }

    public static int CountTotalMatchPlayers(Dictionary<string, QueueStatusEntry> queueStatus) {
        if (queueStatus == null) return 0;
        int total = 0;
        foreach (var entry in queueStatus.Values) {
            total += entry.waiting + entry.playing;
        }
        return total;
    }

    private void UpdateMatchPlayerCountText(int count) {
        if (matchPlayerCountText != null) {
            matchPlayerCountText.text = $"匹配人数({count})";
        }
    }

}
