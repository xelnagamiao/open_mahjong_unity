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
        EnsureMatchEntryUi();
    }

    private void Start() {
        if (matchEntryButton != null) {
            matchEntryButton.onClick.RemoveListener(GoToMatch);
            matchEntryButton.onClick.AddListener(GoToMatch);
        }
    }

    private void OnEnable() {
        UpdateMatchPlayerCountText(0);
        NetworkPollingManager.Instance.StartMatchQueuePolling();
    }

    private void OnDisable() {
        NetworkPollingManager.Instance.StopMatchQueuePolling();
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
        if (LobbyStateGuard.BlockIfInRoomForMatch()) {
            return;
        }
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

    private void EnsureMatchEntryUi() {
        if (matchEntryButton != null && matchPlayerCountText != null) return;

        var root = new GameObject("MatchEntry", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        var rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 0.5f);
        rootRect.anchorMax = new Vector2(0f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = new Vector2(229f, -80f);
        rootRect.sizeDelta = new Vector2(388.88f, 120f);

        TMP_FontAsset font = null;
        var existingTmp = GetComponentInChildren<TMP_Text>(true);
        if (existingTmp != null) font = existingTmp.font;

        if (matchPlayerCountText == null) {
            var countGo = new GameObject("MatchPlayerCountText", typeof(RectTransform));
            countGo.transform.SetParent(root.transform, false);
            var countRect = countGo.GetComponent<RectTransform>();
            countRect.anchorMin = new Vector2(0f, 1f);
            countRect.anchorMax = new Vector2(1f, 1f);
            countRect.pivot = new Vector2(0.5f, 1f);
            countRect.anchoredPosition = Vector2.zero;
            countRect.sizeDelta = new Vector2(0f, 36f);
            matchPlayerCountText = countGo.AddComponent<TextMeshProUGUI>();
            matchPlayerCountText.font = font;
            matchPlayerCountText.fontSize = 28f;
            matchPlayerCountText.alignment = TextAlignmentOptions.Center;
            matchPlayerCountText.color = Color.white;
            matchPlayerCountText.text = "匹配人数(0)";
        }

        if (matchEntryButton == null) {
            var buttonGo = new GameObject("MatchEntryButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(root.transform, false);
            var buttonRect = buttonGo.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.offsetMin = new Vector2(0f, 0f);
            buttonRect.offsetMax = new Vector2(0f, -40f);

            var buttonImage = buttonGo.GetComponent<Image>();
            buttonImage.color = new Color(0.25f, 0.55f, 0.85f, 1f);

            matchEntryButton = buttonGo.GetComponent<Button>();
            matchEntryButton.targetGraphic = buttonImage;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(buttonGo.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = 36f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.text = "排位匹配";
            label.raycastTarget = false;
        }
    }
}
