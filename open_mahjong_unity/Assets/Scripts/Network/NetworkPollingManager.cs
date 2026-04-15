using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 统一管理网络轮询请求（房间列表、平台人数、匹配队列状态）
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
    private Coroutine matchQueuePollingCoroutine;

    private void Awake() {
        if (_instance != null && _instance != this) {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
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

    public void StartMatchQueuePolling(float intervalSeconds = 5f) {
        StopMatchQueuePolling();
        MatchNetworkManager.Instance?.SendGetQueueStatus();
        matchQueuePollingCoroutine = StartCoroutine(PollingRoutine(() => {
            MatchNetworkManager.Instance?.SendGetQueueStatus();
        }, intervalSeconds));
    }

    public void StopMatchQueuePolling() {
        if (matchQueuePollingCoroutine != null) {
            StopCoroutine(matchQueuePollingCoroutine);
            matchQueuePollingCoroutine = null;
        }
    }

    private IEnumerator PollingRoutine(Action requestAction, float intervalSeconds) {
        while (true) {
            yield return new WaitForSeconds(intervalSeconds);
            requestAction?.Invoke();
        }
    }
}
