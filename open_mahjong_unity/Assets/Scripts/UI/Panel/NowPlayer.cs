using System;
using UnityEngine;
using TMPro;

public class NowPlayer : MonoBehaviour {
    public static NowPlayer Instance { get; private set; }

    [Header("服务器统计信息UI")]
    [SerializeField] private TMP_Text onlinePlayerCountText;  // 在线人数文本
    [SerializeField] private TMP_Text waitingRoomCountText;   // 等待房间数文本
    [SerializeField] private TMP_Text playingRoomCountText;   // 正在进行的房间数文本
    [SerializeField] private TMP_Text matchPlayingGamesText;  // 正在进行的匹配对局数文本

    public bool HasServerStats { get; private set; }
    public int OnlinePlayerCount { get; private set; }
    public int WaitingRoomCount { get; private set; }
    public int VenueWaitingRoomCount { get; private set; }
    public int PlayingRoomCount { get; private set; }
    public int MatchPlayingGames { get; private set; }
    public float ServerStatsReceivedAt { get; private set; }
    public event Action ServerStatsChanged;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable() {
        HasServerStats = false;
        ServerStatsChanged?.Invoke();
        NetworkPollingManager.Instance?.StartServerStatsPolling();
    }

    private void OnDisable() {
        HasServerStats = false;
        ServerStatsChanged?.Invoke();
        NetworkPollingManager.Existing?.StopServerStatsPolling();
    }

    // 显示服务器统计信息
    public void DisplayServerStats(int onlinePlayerCount, int waitingRoomCount, int playingRoomCount, int matchPlayingGames, int venueWaitingRoomCount) {
        OnlinePlayerCount = onlinePlayerCount;
        WaitingRoomCount = waitingRoomCount;
        VenueWaitingRoomCount = venueWaitingRoomCount;
        PlayingRoomCount = playingRoomCount;
        MatchPlayingGames = matchPlayingGames;
        ServerStatsReceivedAt = Time.realtimeSinceStartup;
        HasServerStats = true;
        if (onlinePlayerCountText != null) {
            onlinePlayerCountText.text = $"在线人数:{onlinePlayerCount}";
        }
        if (waitingRoomCountText != null) {
            waitingRoomCountText.text = $"等待中房间数:{waitingRoomCount}";
        }
        if (playingRoomCountText != null) {
            playingRoomCountText.text = $"游戏中房间数:{playingRoomCount}";
        }
        if (matchPlayingGamesText != null) {
            matchPlayingGamesText.text = $"游戏中匹配数:{matchPlayingGames}";
        }
        ServerStatsChanged?.Invoke();
    }
}
