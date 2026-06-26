using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 统一管理网络轮询请求（房间列表、平台人数、主菜单匹配总人数、匹配页队列明细）
/// </summary>
public class NetworkPollingManager : MonoBehaviour {
    public static NetworkPollingManager Instance {
        get {
            if (_instance == null) {
                var go = new GameObject("NetworkPollingManager");
                _instance = go.AddComponent<NetworkPollingManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private static NetworkPollingManager _instance;

    private Coroutine roomListPollingCoroutine;
    private Coroutine serverStatsPollingCoroutine;
    private Coroutine menuMatchPlayerCountPollingCoroutine;
    private Coroutine matchPanelQueuePollingCoroutine;
    private Coroutine friendListPollingCoroutine;

    private void Awake() {
        if (_instance != null && _instance != this) {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    public void StartRoomListPolling(float intervalSeconds = 5f) {
        StopRoomListPolling();
        RoomNetworkManager.Instance?.GetRoomList(showTipOnSuccess: false);
        roomListPollingCoroutine = StartCoroutine(PollingRoutine(() => {
            RoomNetworkManager.Instance?.GetRoomList(showTipOnSuccess: false);
        }, intervalSeconds));
    }

    public void StopRoomListPolling() {
        if (roomListPollingCoroutine != null) {
            StopCoroutine(roomListPollingCoroutine);
            roomListPollingCoroutine = null;
        }
    }

    public void StartServerStatsPolling(float intervalSeconds = 5f) {
        StopServerStatsPolling();
        NetworkManager.Instance?.GetServerStats();
        serverStatsPollingCoroutine = StartCoroutine(PollingRoutine(() => {
            NetworkManager.Instance?.GetServerStats();
        }, intervalSeconds));
    }

    public void StopServerStatsPolling() {
        if (serverStatsPollingCoroutine != null) {
            StopCoroutine(serverStatsPollingCoroutine);
            serverStatsPollingCoroutine = null;
        }
    }

    /// <summary>
    /// 主菜单「匹配人数」汇总轮询，由 <see cref="MeunPanel"/> 独占启停。
    /// </summary>
    public void StartMenuMatchPlayerCountPolling(float intervalSeconds = 5f) {
        StopMenuMatchPlayerCountPolling();
        MatchNetworkManager.Instance?.RequestQueueStatusForMenu();
        menuMatchPlayerCountPollingCoroutine = StartCoroutine(PollingRoutine(() => {
            MatchNetworkManager.Instance?.RequestQueueStatusForMenu();
        }, intervalSeconds));
    }

    public void StopMenuMatchPlayerCountPolling() {
        if (menuMatchPlayerCountPollingCoroutine != null) {
            StopCoroutine(menuMatchPlayerCountPollingCoroutine);
            menuMatchPlayerCountPollingCoroutine = null;
        }
    }

    /// <summary>
    /// 匹配页各队列等待/游戏中人数轮询，由 <see cref="MatchPanel"/> 独占启停。
    /// </summary>
    public void StartMatchPanelQueuePolling(float intervalSeconds = 5f) {
        StopMatchPanelQueuePolling();
        MatchNetworkManager.Instance?.RequestQueueStatusForMatchPanel();
        matchPanelQueuePollingCoroutine = StartCoroutine(PollingRoutine(() => {
            MatchNetworkManager.Instance?.RequestQueueStatusForMatchPanel();
        }, intervalSeconds));
    }

    public void StopMatchPanelQueuePolling() {
        if (matchPanelQueuePollingCoroutine != null) {
            StopCoroutine(matchPanelQueuePollingCoroutine);
            matchPanelQueuePollingCoroutine = null;
        }
    }

    /// <summary>
    /// 好友/关注列表轮询：仅在 FriendPanel 可见时启动，由 FriendPanel 在 OnEnable 调用，
    /// OnDisable 调用 StopFriendListPolling 停止。每次轮询会带回最新的在线/对局中状态供按钮联动。
    /// </summary>
    public void StartFriendListPolling(float intervalSeconds = 5f) {
        StopFriendListPolling();
        FriendNetworkManager.Instance?.ListAllFriendPanels();
        friendListPollingCoroutine = StartCoroutine(PollingRoutine(() => {
            FriendNetworkManager.Instance?.ListAllFriendPanels();
        }, intervalSeconds));
    }

    public void StopFriendListPolling() {
        if (friendListPollingCoroutine != null) {
            StopCoroutine(friendListPollingCoroutine);
            friendListPollingCoroutine = null;
        }
    }

    private IEnumerator PollingRoutine(Action requestAction, float intervalSeconds) {
        while (true) {
            yield return new WaitForSeconds(intervalSeconds);
            requestAction?.Invoke();
        }
    }
}
