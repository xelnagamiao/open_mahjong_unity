using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class RecordPanel : MonoBehaviour {

    public static RecordPanel Instance { get; private set; }
    [SerializeField] private RecordPrefab RecordPrefab;
    [SerializeField] private Transform dropdownContentTransform;
    [SerializeField] private ScrollRect recordScrollRect;

    [Header("导航按钮")]
    [SerializeField] private Button SearchRecordButton;
    [SerializeField] private Button OverviewButton;
    [SerializeField] private Button FavoriteFilterButton;
    [SerializeField] private Button LocalRecordsButton;

    [Header("无牌谱提示面板")]
    [SerializeField] private GameObject NoRecordPanel;

    [Header("牌谱ID搜索面板（根节点挂 PanelPopupTransition + CanvasGroup）")]
    [SerializeField] private PanelPopupTransition recordIdInputPopup;
    [SerializeField] private TMP_InputField RecordIdInputField;
    [SerializeField] private Button ConfirmLoadButton;
    [SerializeField] private Button CancelSearchButton;

    private const float LoadMoreScrollThreshold = 0.02f;

    private readonly HashSet<string> _loadedGameIds = new HashSet<string>();
    private readonly Dictionary<string, RecordPrefab> _recordItems = new Dictionary<string, RecordPrefab>();
    private bool _isLoadingMore;
    private bool _hasMore = true;
    private int _loadedCount;
    private enum ListMode { Overview, Favorite, Local }
    private ListMode _listMode = ListMode.Overview;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
            return;
        }
        SearchRecordButton.onClick.AddListener(OpenRecordIdInput);
        ConfirmLoadButton.onClick.AddListener(ConfirmLoadRecordById);
        CancelSearchButton.onClick.AddListener(CloseRecordIdInput);
        OverviewButton.onClick.AddListener(ShowOverviewList);
        FavoriteFilterButton.onClick.AddListener(ShowFavoritesList);
        LocalRecordsButton.onClick.AddListener(ShowLocalList);

        recordIdInputPopup.gameObject.SetActive(false);
        NoRecordPanel.SetActive(false);
        ClearRecordItems();

        recordScrollRect.onValueChanged.AddListener(OnRecordScrollChanged);
    }

    private void OnDestroy() {
        recordScrollRect.onValueChanged.RemoveListener(OnRecordScrollChanged);
    }

    /// <summary>
    /// 打开牌谱面板时调用：重置为全部列表并重新拉取。
    /// </summary>
    public void OpenAndReload() {
        _listMode = ListMode.Overview;
        ReloadList();
    }

    private void ShowOverviewList() {
        SetListMode(ListMode.Overview);
    }

    private void ShowFavoritesList() {
        SetListMode(ListMode.Favorite);
    }

    private void ShowLocalList() {
        SetListMode(ListMode.Local);
    }

    private void SetListMode(ListMode mode) {
        _listMode = mode;
        ReloadList();
    }

    private void ReloadList() {
        if (_listMode == ListMode.Local) {
            LoadLocalList();
            return;
        }
        _isLoadingMore = true;
        DataNetworkManager.Instance.GetRecordList(0, _listMode == ListMode.Favorite);
    }

    private void OpenRecordIdInput() {
        recordIdInputPopup.Show(() => {
            RecordIdInputField.text = "";
            RecordIdInputField.ActivateInputField();
        });
    }

    private void CloseRecordIdInput() {
        recordIdInputPopup.Hide();
    }

    private void ConfirmLoadRecordById() {
        string raw = RecordIdInputField.text.Trim();
        if (string.IsNullOrEmpty(raw)) {
            NotificationManager.Instance.ShowTip("牌谱", false, "请输入牌谱ID");
            return;
        }
        if (!SharedRecordLink.TryExtractGameId(raw, out string gameId)) {
            SharedRecordLink.ClearPendingJump();
            NotificationManager.Instance.ShowTip(
                "牌谱",
                false,
                SharedRecordLink.LooksLikeShareLink(raw) ? "无法从链接中识别牌谱ID" : "请输入牌谱ID"
            );
            return;
        }
        SharedRecordLink.CapturePosition(raw);
        recordIdInputPopup.Hide();
        LocalRecordStore.LoadAsync(gameId, local => {
            if (local != null && local.record != null) {
                OpenRecord(local);
                return;
            }
            DataNetworkManager.Instance.GetRecordById(gameId);
        });
    }

    private void OnRecordScrollChanged(Vector2 _) {
        if (!_hasMore || _isLoadingMore) return;
        if (recordScrollRect.verticalNormalizedPosition > LoadMoreScrollThreshold) return;
        RequestLoadMore();
    }

    private void RequestLoadMore() {
        if (_listMode == ListMode.Local) return;
        if (_isLoadingMore || !_hasMore) return;
        _isLoadingMore = true;
        DataNetworkManager.Instance.GetRecordList(_loadedCount, _listMode == ListMode.Favorite);
    }

    private void ResetPaginationState() {
        _loadedGameIds.Clear();
        _recordItems.Clear();
        _loadedCount = 0;
        _hasMore = true;
        _isLoadingMore = false;
    }

    private void ClearRecordItems() {
        foreach (Transform child in dropdownContentTransform) {
            Destroy(child.gameObject);
        }
        _recordItems.Clear();
    }

    private void LoadLocalList() {
        LocalRecordStore.EnsureReady(() => {
            if (_listMode != ListMode.Local) return;
            _isLoadingMore = false;
            ResetPaginationState();
            ClearRecordItems();
            _hasMore = false;
            List<RecordInfo> local = LocalRecordStore.ListAll();
            if (local != null) {
                foreach (RecordInfo record in local) {
                    AppendRecordItem(record, localPlayback: true);
                }
            }
            _loadedCount = _loadedGameIds.Count;
            NoRecordPanel.SetActive(_loadedCount == 0);
        });
    }

    private void AppendRecordItem(RecordInfo record, bool localPlayback = false) {
        if (record == null || string.IsNullOrEmpty(record.game_id)) return;
        if (_loadedGameIds.Contains(record.game_id)) return;

        _loadedGameIds.Add(record.game_id);
        string subRule = record.sub_rule ?? "";
        string matchType = record.match_type ?? "";
        string recordedTime = record.created_at;

        RecordPrefab item = Instantiate(RecordPrefab, dropdownContentTransform);
        item.InitializeRecordItem(
            record.game_id,
            subRule,
            matchType,
            recordedTime,
            record.players,
            record.is_favorite,
            localPlayback
        );
        _recordItems[record.game_id] = item;
    }

    public void OnRecordFavoriteUpdated(bool success, string gameId, bool isFavorite, string message) {
        if (string.IsNullOrEmpty(gameId)) return;

        if (success && _listMode == ListMode.Favorite && !isFavorite) {
            RemoveRecordItem(gameId);
        } else if (_recordItems.TryGetValue(gameId, out RecordPrefab item)) {
            item.ApplyFavoriteResult(success, isFavorite);
        }

        if (success) {
            NotificationManager.Instance.ShowTip(
                "牌谱",
                true,
                string.IsNullOrEmpty(message) ? (isFavorite ? "已收藏" : "已取消收藏") : message
            );
        }
    }

    private void RemoveRecordItem(string gameId) {
        if (_recordItems.TryGetValue(gameId, out RecordPrefab item)) {
            Destroy(item.gameObject);
        }
        _recordItems.Remove(gameId);
        _loadedGameIds.Remove(gameId);
        _loadedCount = _loadedGameIds.Count;
        if (_loadedCount == 0) {
            NoRecordPanel.SetActive(true);
            _hasMore = false;
        }
    }

    public void GetRecordListResponse(bool success, string message, RecordInfo[] recordList, int offset = 0) {
        _isLoadingMore = false;
        if (_listMode == ListMode.Local) return;

        if (!success) {
            Debug.LogError($"获取记录列表失败: {message}");
            if (offset == 0) {
                NotificationManager.Instance.ShowTip("牌谱", false, message);
            }
            return;
        }

        if (recordList == null) {
            recordList = System.Array.Empty<RecordInfo>();
        }

        if (offset == 0) {
            ResetPaginationState();
            ClearRecordItems();
        }

        if (offset == 0 && recordList.Length == 0) {
            Debug.Log(_listMode == ListMode.Favorite ? "没有收藏的牌谱" : "没有游戏记录");
            NoRecordPanel.SetActive(true);
            _hasMore = false;
            return;
        }

        NoRecordPanel.SetActive(false);

        int appended = 0;
        foreach (var record in recordList) {
            int before = _loadedGameIds.Count;
            AppendRecordItem(record);
            if (_loadedGameIds.Count > before) {
                appended++;
            }
        }

        _loadedCount = _loadedGameIds.Count;
        if (recordList.Length < DataNetworkManager.RecordListPageSize) {
            _hasMore = false;
        }

        Debug.Log(
            $"牌谱列表 mode={_listMode} offset={offset} 追加 {appended} 条，" +
            $"当前共 {_loadedCount} 条，hasMore={_hasMore}"
        );
    }

    public void OnRecordDetailReceived(RecordDetail detail) {
        OpenRecord(detail);
    }

    /// <summary>
    /// 打开牌谱回放（天梯列表、牌谱面板等入口共用）。
    /// </summary>
    public static void OpenRecord(RecordDetail detail) {
        if (detail == null || detail.record == null) {
            NotificationManager.Instance.ShowTip("牌谱", false, "牌谱数据为空");
            return;
        }

        // Dictionary 内嵌 JArray 时不能直接 SerializeObject，需经 JToken 还原
        string recordJson = JToken.FromObject(detail.record).ToString(Formatting.None);

        if (string.IsNullOrWhiteSpace(recordJson)) {
            NotificationManager.Instance.ShowTip("牌谱", false, "牌谱内容为空");
            return;
        }

        WindowsManager.Instance.SwitchWindow("recordscene");
        if (GameRecordManager.Instance == null) {
            NotificationManager.Instance.ShowTip("牌谱", false, "牌谱场景未就绪");
            return;
        }

        try {
            if (detail.perspective) {
                RecordSetting.Instance.SetShowCardsMode(false);
            } else {
                RecordSetting.Instance.SetShowCardsMode(true);
            }
            GameRecordManager.Instance.LoadRecord(recordJson, detail.players);
            SharedRecordLink.ApplyPendingJumpIfAny();
        } catch (System.Exception e) {
            SharedRecordLink.ClearPendingJump();
            Debug.LogError($"加载牌谱失败: {e.Message}");
            NotificationManager.Instance.ShowTip("牌谱", false, $"解析牌谱失败: {e.Message}");
        }
    }
}
