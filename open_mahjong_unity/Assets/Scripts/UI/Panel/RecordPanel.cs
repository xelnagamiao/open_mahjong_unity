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
    private int _nextOffset;
    private int _listVersion;
    private bool _loadMoreArmed;
    private IReadOnlyList<RecordInfo> _localRecords = System.Array.Empty<RecordInfo>();
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
        ReloadCurrentList();
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
        ReloadCurrentList();
    }

    /// <summary>
    /// 按当前标签刷新列表，供退出牌谱返回时保留原来的选择。
    /// </summary>
    public void ReloadCurrentList() {
        int version = ++_listVersion;
        ResetPaginationState();
        ClearRecordItems();
        NoRecordPanel.SetActive(false);
        recordScrollRect.verticalNormalizedPosition = 1f;
        _isLoadingMore = true;
        if (_listMode == ListMode.Local) {
            LoadLocalList(version);
            return;
        }
        DataNetworkManager.Instance.GetRecordList(0, _listMode == ListMode.Favorite, version);
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
                OpenRecord(local, localPlayback: true);
                return;
            }
            DataNetworkManager.Instance.GetRecordById(gameId);
        });
    }

    private void OnRecordScrollChanged(Vector2 _) {
        if (!isActiveAndEnabled) return;
        if (recordScrollRect.verticalNormalizedPosition > LoadMoreScrollThreshold) {
            _loadMoreArmed = true;
            return;
        }
        if (!_loadMoreArmed || !_hasMore || _isLoadingMore) return;
        _loadMoreArmed = false;
        RequestLoadMore();
    }

    private void RequestLoadMore() {
        if (_isLoadingMore || !_hasMore) return;
        _isLoadingMore = true;
        if (_listMode == ListMode.Local) {
            AppendLocalBatch();
            return;
        }
        DataNetworkManager.Instance.GetRecordList(_nextOffset, _listMode == ListMode.Favorite, _listVersion);
    }

    private void ResetPaginationState() {
        _loadedGameIds.Clear();
        _recordItems.Clear();
        _loadedCount = 0;
        _nextOffset = 0;
        _loadMoreArmed = false;
        _localRecords = System.Array.Empty<RecordInfo>();
        _hasMore = true;
        _isLoadingMore = false;
    }

    private void ClearRecordItems() {
        foreach (Transform child in dropdownContentTransform) {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        _recordItems.Clear();
    }

    private void LoadLocalList(int version) {
        LocalRecordStore.EnsureReady(() => {
            if (_listMode != ListMode.Local || version != _listVersion) return;
            _localRecords = LocalRecordStore.ListAll() ?? new List<RecordInfo>();
            AppendLocalBatch();
        });
    }

    private void AppendLocalBatch() {
        int end = System.Math.Min(_nextOffset + DataNetworkManager.RecordListPageSize, _localRecords.Count);
        for (; _nextOffset < end; _nextOffset++) {
            AppendRecordItem(_localRecords[_nextOffset], localPlayback: true);
        }
        _loadedCount = _loadedGameIds.Count;
        _hasMore = _nextOffset < _localRecords.Count;
        _isLoadingMore = false;
        _loadMoreArmed = false;
        NoRecordPanel.SetActive(_loadedCount == 0);
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
        if (_loadedGameIds.Remove(gameId)) _nextOffset = System.Math.Max(0, _nextOffset - 1);
        _loadedCount = _loadedGameIds.Count;
        NoRecordPanel.SetActive(_loadedCount == 0);
        if (_loadedCount == 0 && _hasMore) RequestLoadMore();
    }

    public void GetRecordListResponse(bool success, string message, RecordInfo[] recordList, int offset = 0,
        int listVersion = -1, bool? favoritesOnly = null) {
        if (_listMode == ListMode.Local || (listVersion >= 0 && listVersion != _listVersion)) return;
        if (favoritesOnly.HasValue && favoritesOnly.Value != (_listMode == ListMode.Favorite)) return;
        if (offset != _nextOffset) return;
        _isLoadingMore = false;

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
        int batchCount = System.Math.Min(recordList.Length, DataNetworkManager.RecordListPageSize);
        for (int i = 0; i < batchCount; i++) {
            int before = _loadedGameIds.Count;
            AppendRecordItem(recordList[i]);
            if (_loadedGameIds.Count > before) {
                appended++;
            }
        }

        _loadedCount = _loadedGameIds.Count;
        _nextOffset = offset + batchCount;
        _hasMore = recordList.Length >= DataNetworkManager.RecordListPageSize;
        _loadMoreArmed = false;
        if (offset == 0) recordScrollRect.verticalNormalizedPosition = 1f;

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
    public static bool OpenRecord(RecordDetail detail, bool localPlayback = false) {
        if (detail == null || detail.record == null) {
            SharedRecordLink.ClearPendingJump();
            NotificationManager.Instance?.ShowTip("牌谱", false, "牌谱数据为空");
            return false;
        }
        // 入口先守卫，避免 LoadRecord 拒绝加载后留下已经切换的空游戏窗口。
        if (GameSessionGuard.BlockIfExclusiveSession("阅览牌谱")) {
            SharedRecordLink.ClearPendingJump();
            return false;
        }
        WindowsManager windows = WindowsManager.Instance;
        if (windows == null) {
            SharedRecordLink.ClearPendingJump();
            NotificationManager.Instance?.ShowTip("牌谱", false, "场景管理器未就绪");
            return false;
        }
        bool enteredRecordScene = false;
        try {
            // Dictionary 内嵌 JArray 时不能直接 SerializeObject，需经 JToken 还原。
            string recordJson = JToken.FromObject(detail.record).ToString(Formatting.None);
            if (string.IsNullOrWhiteSpace(recordJson)) {
                throw new System.InvalidOperationException("牌谱内容为空");
            }
            enteredRecordScene = true;
            if (!windows.TryPrepareRecordScene(out GameRecordManager manager, out string error)) {
                throw new System.InvalidOperationException(error);
            }
            RecordSetting.Instance?.SetShowCardsMode(!detail.perspective);
            // 是否可分享取决于云端保存标记，与从云端还是本地副本打开无关。
            manager.LoadRecord(recordJson, detail.players, detail.cloud_saved ? detail.game_id : null);
            SharedRecordLink.ApplyPendingJumpIfAny();
            return true;
        } catch (System.Exception e) {
            SharedRecordLink.ClearPendingJump();
            Debug.LogError($"加载牌谱失败: {e}");
            if (enteredRecordScene && windows.GetCurrentWindow() == "recordscene") {
                GameRecordManager.Instance?.HideGameRecord();
                if (SharedRecordLink.IsPublicSharePlayback) {
                    AppSession.ReturnToLogin();
                } else {
                    windows.ExitGameToReturnWindow();
                }
            }
            NotificationManager.Instance?.ShowTip("牌谱", false, $"无法打开牌谱: {e.Message}");
            return false;
        }
    }
}
