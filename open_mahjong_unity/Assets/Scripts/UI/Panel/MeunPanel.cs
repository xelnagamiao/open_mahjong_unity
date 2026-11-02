using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MeunPanel : MonoBehaviour {
    public static MeunPanel Instance { get; private set; }

    public bool HasMatchPlayerCount { get; private set; }
    public int MatchPlayerCount { get; private set; }
    public float MatchPlayerCountReceivedAt { get; private set; }
    public event System.Action MatchPlayerCountChanged;

    [Header("规则阅读入口")]
    [SerializeField] private RulebookPanelController rulebookPanelController;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable() {
        HasMatchPlayerCount = false;
        MatchPlayerCountChanged?.Invoke();
        NetworkPollingManager.Instance?.StartMenuMatchPlayerCountPolling();
    }

    private void OnDisable() {
        HasMatchPlayerCount = false;
        MatchPlayerCountChanged?.Invoke();
        if (rulebookPanelController) rulebookPanelController.CloseAll();
        NetworkPollingManager.Existing?.StopMenuMatchPlayerCountPolling();
    }

    public void OpenFanTable() {
        rulebookPanelController.Open("guobiao");
    }
    public bool IsFanTableOpen => rulebookPanelController && rulebookPanelController.IsOpen;

    public void CloseFanTable() {
        if (rulebookPanelController) rulebookPanelController.CloseAll();
    }

    // 设置用户信息（通过UserContainer处理UI，UserDataManager管理数据）
    public void SetUserInfo(string username, string userkey, int user_id, bool isTourist = false) {
        UserContainer.Instance.SetUserInfo(username, userkey, user_id, isTourist);
    }

    // 显示服务器统计信息（通过NowPlayer显示）
    public void DisplayServerStats(int onlinePlayerCount, int waitingRoomCount, int playingRoomCount, int matchPlayingGames, int venueWaitingRoomCount) {
        NowPlayer.Instance.DisplayServerStats(onlinePlayerCount, waitingRoomCount, playingRoomCount, matchPlayingGames, venueWaitingRoomCount);
    }

    public void UpdateMatchPlayerCount(Dictionary<string, QueueStatusEntry> queueStatus, int? uniquePlayerCount = null) {
        MatchPlayerCount = uniquePlayerCount ?? CountTotalMatchPlayers(queueStatus);
        MatchPlayerCountReceivedAt = Time.realtimeSinceStartup;
        HasMatchPlayerCount = true;
        MatchPlayerCountChanged?.Invoke();
    }

    public static int CountTotalMatchPlayers(Dictionary<string, QueueStatusEntry> queueStatus) {
        if (queueStatus == null) return 0;
        int total = 0;
        foreach (var entry in queueStatus.Values) {
            total += entry.waiting + entry.playing;
        }
        return total;
    }


}
