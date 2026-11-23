using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
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

    private sealed class Card {
        public RecordInfo source;
        public RecordInfo localDisplay;
        public bool metadataRequested;
    }
    private readonly List<RecordInfo> rawRows = new();
    private readonly Dictionary<string, Card> cards = new();
    private readonly HashSet<string> seenGames = new(), seenCards = new(), dirty = new();
    private readonly HashSet<string> pendingFavorites = new();
    private readonly List<string> orderedKeys = new();
    private readonly List<string> removedKeys = new();
    private ViewportCardList<string, RecordPrefab> viewportCards;
    private RectTransform viewport;
    private Transform inactiveCards;
    private RectTransform preparedCards;
    private NativeWebSocket.WebSocket favoriteSocket;
    private float rowHeight;
    private int lastWarmFrame = -1;
    private int navigationWarmVersion;
    private Coroutine presentation;
    private bool _hasMore = true;
    private bool fillShortPage;
    private int _listVersion;
    private int requestOffset = -1, boundUser = int.MinValue, scopeVersion, openLocalVersion;
    private ListMode boundMode;
    private readonly Queue<(Card card, RecordInfo record, int scope)> missingLocalMetadata = new();
    private bool readingLocalMetadata;
    private bool _loadMoreArmed;
    private bool _recordIdSubmitted;
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
        RecordIdInputField.lineType = TMP_InputField.LineType.SingleLine;
        RecordIdInputField.onSubmit.AddListener(SubmitRecordId);
        CancelSearchButton.onClick.AddListener(CloseRecordIdInput);
        OverviewButton.onClick.AddListener(ShowOverviewList);
        FavoriteFilterButton.onClick.AddListener(ShowFavoritesList);
        LocalRecordsButton.onClick.AddListener(ShowLocalList);

        recordIdInputPopup.gameObject.SetActive(false);
        NoRecordPanel.SetActive(false);
        EnsureViewport();
        recordScrollRect.onValueChanged.AddListener(OnRecordScrollChanged);
    }

    // Pointer intent may precede the first activation (and therefore Awake) of this page.
    private void EnsureViewport() {
        if (viewportCards != null) return;
        foreach (Transform child in dropdownContentTransform) {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        var content = (RectTransform)dropdownContentTransform;
        viewport = recordScrollRect.viewport ? recordScrollRect.viewport : (RectTransform)recordScrollRect.transform;
        var layout = RecordPrefab.GetComponent<LayoutElement>();
        rowHeight = layout && layout.preferredHeight > 0 ? layout.preferredHeight : ((RectTransform)RecordPrefab.transform).rect.height;
        // Keep moving card meshes in local coordinates while the surrounding UI stays pixel-aligned.
        var cardCanvas = content.TryGetComponent(out Canvas existingCanvas) ? existingCanvas : content.gameObject.AddComponent<Canvas>();
        cardCanvas.overridePixelPerfect = true;
        cardCanvas.pixelPerfect = false;
        if (!content.GetComponent<GraphicRaycaster>()) content.gameObject.AddComponent<GraphicRaycaster>();
        var staging = new GameObject("Inactive record cards", typeof(RectTransform));
        staging.SetActive(false);
        inactiveCards = staging.transform;
        inactiveCards.SetParent(transform, false);
        viewportCards = new ViewportCardList<string, RecordPrefab>(recordScrollRect, content, _ => CreateCard(), BindCard);
    }

    private void OnEnable() {
        if (viewportCards != null) {
            presentation = StartCoroutine(PresentList());
        }
    }

    private void OnDisable() {
        ++openLocalVersion;
        CancelNavigationPreparation();
        if (presentation != null) StopCoroutine(presentation);
        presentation = null;
    }

    private void OnDestroy() {
        if (RecordIdInputField) RecordIdInputField.onSubmit.RemoveListener(SubmitRecordId);
        recordScrollRect.onValueChanged.RemoveListener(OnRecordScrollChanged);
        viewportCards?.Dispose();
        if (preparedCards) Destroy(preparedCards.gameObject);
        if (Instance == this) Instance = null;
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
        int user = UserDataManager.Instance != null ? UserDataManager.Instance.UserId : 0;
        if (boundMode != _listMode || boundUser != user) {
            if (boundUser != user) pendingFavorites.Clear();
            ClearList();
            boundMode = _listMode;
            boundUser = user;
            recordScrollRect.verticalNormalizedPosition = 1f;
        } else if (requestOffset == 0 && (_listMode == ListMode.Local ||
            DataNetworkManager.Instance.HasPendingRecordFirstPage(_listVersion))) {
            return;
        }
        int version = ++_listVersion;
        requestOffset = 0;
        if (_listMode == ListMode.Local) {
            LoadLocalList(version);
        } else {
            DataNetworkManager.Instance.GetRecordList(0, _listMode == ListMode.Favorite, version);
        }
    }

    private void OpenRecordIdInput() {
        _recordIdSubmitted = false;
        recordIdInputPopup.Show(() => {
            RecordIdInputField.text = "";
            RecordIdInputField.ActivateInputField();
        });
    }

    private void CloseRecordIdInput() {
        recordIdInputPopup.Hide();
    }

    private void SubmitRecordId(string _) {
        if (recordIdInputPopup.gameObject.activeInHierarchy && ConfirmLoadButton.interactable)
            ConfirmLoadRecordById();
    }

    private void ConfirmLoadRecordById() {
        if (_recordIdSubmitted) return;
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
        _recordIdSubmitted = true;
        recordIdInputPopup.Hide();
        int version = _listVersion;
        LocalRecordStore.LoadAsync(gameId, local => {
            if (!this || !isActiveAndEnabled || version != _listVersion) return;
            if (local != null && local.record != null) {
                RequestOpenRecord(local, localPlayback: true);
                return;
            }
            DataNetworkManager.Instance.GetRecordById(gameId);
        });
    }

    private void OnRecordScrollChanged(Vector2 _) {
        if (!isActiveAndEnabled || !IsCurrentPage()) return;
        // ScrollRect also moves during LateUpdate. Bind the final viewport before this frame is drawn.
        if (viewportCards.FirstScreenShown) viewportCards.Tick();
        if (recordScrollRect.verticalNormalizedPosition > LoadMoreScrollThreshold) {
            _loadMoreArmed = true;
            return;
        }
        if (!_loadMoreArmed || !_hasMore || requestOffset >= 0 || !viewportCards.FirstScreenShown) return;
        _loadMoreArmed = false;
        RequestLoadMore();
    }

    private void RequestLoadMore() {
        if (requestOffset >= 0 || !_hasMore) return;
        fillShortPage = false;
        requestOffset = rawRows.Count;
        if (_listMode == ListMode.Local) {
            AppendLocalBatch();
            return;
        }
        DataNetworkManager.Instance.GetRecordList(requestOffset, _listMode == ListMode.Favorite, _listVersion);
    }

    public void ResetSessionCaches() {
        CancelNavigationPreparation();
        pendingFavorites.Clear();
        favoriteSocket = null;
        ClearList();
    }

    public bool TryBeginFavoriteRequest(string gameId) {
        if (string.IsNullOrEmpty(gameId) || _listMode == ListMode.Local) return false;
        var socket = NetworkManager.Instance?.GetWebSocket();
        if (socket != favoriteSocket) {
            pendingFavorites.Clear();
            favoriteSocket = socket;
        }
        return pendingFavorites.Add(gameId);
    }

    public void OpenLocalRecord(string gameId) {
        if (string.IsNullOrEmpty(gameId) || _listMode != ListMode.Local || !isActiveAndEnabled) return;
        int intent = ++openLocalVersion;
        LocalRecordStore.LoadAsync(gameId, detail => {
            if (!this || !isActiveAndEnabled || intent != openLocalVersion ||
                WindowsManager.Instance?.GetCurrentWindow() != "record") return;
            if (detail?.record != null) RequestOpenRecord(detail, localPlayback: true);
            else NotificationManager.Instance.ShowTip("牌谱", false, "本机没有这份牌谱");
        });
    }

    private void ClearList() {
        ++_listVersion;
        ++scopeVersion;
        ++openLocalVersion;
        boundUser = int.MinValue;
        requestOffset = -1;
        _hasMore = true;
        fillShortPage = false;
        _loadMoreArmed = false;
        _localRecords = System.Array.Empty<RecordInfo>();
        rawRows.Clear();
        missingLocalMetadata.Clear();
        seenGames.Clear();
        seenCards.Clear();
        orderedKeys.Clear();
        dirty.Clear();
        cards.Clear();
        viewportCards.Hide();
        viewportCards.SetRows(orderedKeys, _ => 0, _ => rowHeight, resetScroll: true);
        NoRecordPanel.SetActive(false);
    }

    private void LoadLocalList(int version) {
        LocalRecordStore.EnsureReady(() => {
            if (!this || _listMode != ListMode.Local || version != _listVersion) return;
            _localRecords = LocalRecordStore.ListAll() ?? new List<RecordInfo>();
            AppendLocalBatch();
        });
    }

    private void AppendLocalBatch() {
        int start = requestOffset;
        int end = System.Math.Min(_localRecords.Count, start == 0
            ? System.Math.Max(rawRows.Count, DataNetworkManager.RecordListPageSize)
            : start + DataNetworkManager.RecordListPageSize);
        if (start == 0) rawRows.Clear();
        for (int i = start; i < end; i++) rawRows.Add(_localRecords[i]);
        BeginPage(start, end < _localRecords.Count);
    }

    private void BeginPage(int start, bool hasMore) {
        if (start == 0) {
            seenGames.Clear();
            seenCards.Clear();
            orderedKeys.Clear();
        }
        dirty.Clear();
        for (int i = start; i < rawRows.Count; i++) IndexRecord(rawRows[i]);
        requestOffset = -1;
        _hasMore = hasMore;
        fillShortPage = hasMore;
        if (start == 0 && seenCards.Count != cards.Count) {
            removedKeys.Clear();
            foreach (var key in cards.Keys)
                if (!seenCards.Contains(key)) removedKeys.Add(key);
            foreach (var key in removedKeys) cards.Remove(key);
        }
        viewportCards.SetRows(orderedKeys, _ => 0, _ => rowHeight, dirty);
        bool empty = orderedKeys.Count == 0;
        if (empty) {
            viewportCards.Hide();
            HidePreparedShells();
        }
        if (NoRecordPanel.activeSelf != (empty && !hasMore)) NoRecordPanel.SetActive(empty && !hasMore);
    }

    private static bool IsCurrentPage() => WindowsManager.Instance == null || WindowsManager.Instance.GetCurrentWindow() == "record";

    private RecordPrefab CreateCard() {
        // A prewarmed prefab must not enable its TMP components with the sample roster.
        var card = Instantiate(RecordPrefab, inactiveCards);
        card.gameObject.SetActive(false);
        return card;
    }

    private void PrepareShell(RecordPrefab card) {
        if (!preparedCards) {
            var layer = new GameObject("Prepared record cards", typeof(RectTransform), typeof(CanvasGroup));
            preparedCards = (RectTransform)layer.transform;
            Transform host = transform.parent;
            while (host && !host.gameObject.activeInHierarchy) host = host.parent;
            var canvas = host ? host.GetComponentInParent<Canvas>() : null;
            preparedCards.SetParent(canvas ? canvas.rootCanvas.transform : host, false);
            preparedCards.anchorMin = preparedCards.anchorMax = new Vector2(0, 1);
            CanvasGroup group = layer.GetComponent<CanvasGroup>();
            group.alpha = 0;
            group.interactable = group.blocksRaycasts = false;
        }
        preparedCards.sizeDelta = new Vector2(((RectTransform)dropdownContentTransform).rect.width, rowHeight);
        var rect = (RectTransform)card.transform;
        rect.SetParent(preparedCards, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        card.PrepareEmptyShell();
        card.gameObject.SetActive(true);
    }

    private void HidePreparedShells() {
        if (!preparedCards) return;
        foreach (Transform child in preparedCards) {
            if (child.gameObject.activeSelf) child.gameObject.SetActive(false);
        }
    }

    private bool WarmFirstScreen() {
        if (lastWarmFrame == Time.frameCount) return false;
        lastWarmFrame = Time.frameCount;
        return viewportCards.PrepareFirstRows(0, rowHeight, PrepareShell);
    }

    public IEnumerator PrepareForNavigation() {
        EnsureViewport();
        int version = ++navigationWarmVersion;
        while (version == navigationWarmVersion && !viewportCards.FirstScreenShown && orderedKeys.Count == 0) {
            if (WarmFirstScreen()) yield break;
            yield return null;
        }
    }

    public void CancelNavigationPreparation() {
        ++navigationWarmVersion;
        HidePreparedShells();
    }

    private IEnumerator PresentList() {
        while (true) {
            if (!IsCurrentPage()) {
                yield return null;
                continue;
            }
            if (orderedKeys.Count == 0) {
                if (requestOffset == 0) WarmFirstScreen();
            } else {
                bool shown = viewportCards.FirstScreenShown;
                viewportCards.Tick();
                if (!shown && viewportCards.FirstScreenShown) HidePreparedShells();
                if (_listMode == ListMode.Local && !readingLocalMetadata) {
                    for (int i = Mathf.Max(0, viewportCards.FirstVisibleIndex); i <= viewportCards.LastVisibleIndex && i < orderedKeys.Count; i++)
                        QueueLocalMetadata(cards[orderedKeys[i]]);
                    if (missingLocalMetadata.Count != 0) ReadNextLocalMetadata();
                }
            }
            // A full server page can collapse to fewer visible matches than fit on screen.
            // Advance by raw rows even when there is no scrollbar the player could drag.
            if (fillShortPage && requestOffset < 0 && rawRows.Count > 0 && (viewportCards.FirstScreenShown || orderedKeys.Count == 0) &&
                viewport.rect.height > 0 && ((RectTransform)dropdownContentTransform).rect.height <= viewport.rect.height) RequestLoadMore();
            yield return null;
        }
    }

    private static string CardKey(RecordInfo record) {
        string match = (string)record.match_summary?["match_id"];
        return string.IsNullOrEmpty(match) ? "game:" + record.game_id : "match:" + match;
    }

    private void IndexRecord(RecordInfo record) {
        if (record == null || string.IsNullOrEmpty(record.game_id) || !seenGames.Add(record.game_id)) return;
        string key = CardKey(record);
        if (!seenCards.Add(key)) return;
        orderedKeys.Add(key);
        if (!cards.TryGetValue(key, out var card)) {
            card = new Card();
            cards.Add(key, card);
        }
        if (SameCard(card.source, record)) return;
        card.source = record;
        card.localDisplay = null;
        card.metadataRequested = false;
        dirty.Add(key);
    }

    private void BindCard(RecordPrefab item, int index) {
        var card = cards[orderedKeys[index]];
        bool local = _listMode == ListMode.Local;
        item.BindRecord(card.localDisplay ?? card.source, local, this);
        if (!item.gameObject.activeSelf) {
            item.gameObject.SetActive(true);
            item.FinishPendingLayout();
        }
    }

    private void QueueLocalMetadata(Card card) {
        if (card.metadataRequested) return;
        card.metadataRequested = true;
        if (NeedsLocalMetadata(card.source)) missingLocalMetadata.Enqueue((card, card.source, scopeVersion));
    }

    private static bool SameCard(RecordInfo a, RecordInfo b) {
        if (a == b) return true;
        if (a == null || a.game_id != b.game_id || a.rule != b.rule || a.sub_rule != b.sub_rule ||
            a.match_type != b.match_type || a.created_at != b.created_at || a.is_favorite != b.is_favorite ||
            !JToken.DeepEquals(a.match_summary, b.match_summary)) return false;
        var left = a.players;
        var right = b.players;
        if (left == right) return true;
        if (left == null || right == null || left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++) {
            var p = left[i];
            var q = right[i];
            if (p == q) continue;
            if (p == null || q == null || p.username != q.username || p.rank != q.rank || p.score != q.score ||
                p.original_player_index != q.original_player_index) return false;
        }
        return true;
    }

    private static bool NeedsLocalMetadata(RecordInfo record) {
        return (record.match_summary?["tables"]?["closed"] != null && record.match_summary["personal_imp"] == null)
            || string.IsNullOrWhiteSpace(RuleNameDictionary.RecordKeys(record.rule, record.sub_rule, record.match_summary).subRule);
    }

    // Keep the original index entry intact so a repeat index does not read the same archive again.
    private void ReadNextLocalMetadata() {
        var (card, record, scope) = missingLocalMetadata.Dequeue();
        bool StillCurrent() => this && scope == scopeVersion && card.source == record &&
            cards.TryGetValue(CardKey(record), out var current) && current == card;
        if (!StillCurrent()) return;
        if (!viewportCards.IsInViewport(CardKey(record))) {
            card.metadataRequested = false;
            return;
        }
        readingLocalMetadata = true;
        LocalRecordStore.LoadAsync(record.game_id, detail => {
            readingLocalMetadata = false;
            if (!StillCurrent() || detail == null) return;
            var stored = RuleNameDictionary.RecordKeys(detail);
            var summary = record.match_summary;
            if (summary?["tables"]?["closed"] != null && summary["personal_imp"] == null && detail.record != null) {
                summary = (JObject)summary.DeepClone();
                summary["personal_imp"] = PersonalImpSummary.Build(detail.record);
            }
            var display = new RecordInfo {
                game_id = record.game_id, created_at = record.created_at, match_type = record.match_type,
                rule = string.IsNullOrWhiteSpace(record.rule) ? stored.rule : record.rule,
                sub_rule = string.IsNullOrWhiteSpace(record.sub_rule) ? stored.subRule : record.sub_rule,
                players = record.players, is_favorite = record.is_favorite, match_summary = summary
            };
            if (SameCard(record, display)) return;
            card.localDisplay = display;
            viewportCards.Invalidate(CardKey(record));
        });
    }

    public void OnRecordFavoriteUpdated(bool success, string gameId, bool isFavorite, string message) {
        if (string.IsNullOrEmpty(gameId)) return;
        pendingFavorites.Remove(gameId);
        bool removed = false;
        for (int i = rawRows.Count - 1; i >= 0; i--) {
            var record = rawRows[i];
            if (record?.game_id != gameId || _listMode == ListMode.Local) continue;
            if (cards.TryGetValue(CardKey(record), out var card) && card.source?.game_id == gameId) {
                if (success) card.source.is_favorite = isFavorite;
                if (viewportCards.TryGetVisible(CardKey(record), out var item)) item.ApplyFavoriteResult(success, isFavorite);
            }
            if (!success) continue;
            record.is_favorite = isFavorite;
            if (_listMode == ListMode.Favorite && !isFavorite) { rawRows.RemoveAt(i); removed = true; }
        }
        if (removed) {
            bool refetch = requestOffset >= 0 || (rawRows.Count == 0 && _hasMore);
            BeginPage(0, _hasMore);
            if (refetch) ReloadCurrentList();
        }
        if (success) NotificationManager.Instance.ShowTip("牌谱", true,
            string.IsNullOrEmpty(message) ? (isFavorite ? "已收藏" : "已取消收藏") : message);
    }

    public void GetRecordListResponse(bool success, string message, RecordInfo[] recordList, int offset = 0,
        int listVersion = -1, bool? favoritesOnly = null) {
        if (_listMode == ListMode.Local || (listVersion >= 0 && listVersion != _listVersion) ||
            (favoritesOnly.HasValue && favoritesOnly.Value != (_listMode == ListMode.Favorite)) ||
            offset != requestOffset) return;
        requestOffset = -1;
        if (!success) {
            if (offset == 0 && orderedKeys.Count == 0) HidePreparedShells();
            Debug.LogError($"获取记录列表失败: {message}");
            if (offset == 0) NotificationManager.Instance.ShowTip("牌谱", false, message);
            return;
        }
        recordList ??= System.Array.Empty<RecordInfo>();
        bool hasMore = recordList.Length >= DataNetworkManager.RecordListPageSize;
        if (offset == 0) rawRows.Clear();
        int count = System.Math.Min(recordList.Length, DataNetworkManager.RecordListPageSize);
        for (int i = 0; i < count; i++) rawRows.Add(recordList[i]);
        BeginPage(offset, hasMore);
    }

    public void OnRecordDetailReceived(RecordDetail detail) {
        RequestOpenRecord(detail);
    }

    /// <summary>
    /// 打开牌谱回放（天梯列表、牌谱面板等入口共用）。
    /// </summary>
    public static bool OpenRecord(RecordDetail detail, bool localPlayback = false) => StartOpenRecord(detail, false);

    public static bool RequestOpenRecord(RecordDetail detail, bool localPlayback = false) => StartOpenRecord(detail, true);

    private static bool StartOpenRecord(RecordDetail detail, bool acrossFrames) {
        if (detail == null || detail.record == null) {
            SharedRecordLink.ClearPendingJump();
            NotificationManager.Instance?.ShowTip("牌谱", false, "牌谱数据为空");
            return false;
        }
        if (!MatchRecordCodec.IsUnified(detail.record)) {
            SharedRecordLink.ClearPendingJump();
            NotificationManager.Instance?.ShowTip("牌谱", false, "牌谱格式无效");
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
        var work = OpenRecordSteps(windows, detail, acrossFrames, SharedRecordLink.TakePendingPosition());
        if (acrossFrames) {
            ReplayFrameLoader.Begin(work, e => HandleOpenError(windows, e));
            return true;
        }
        ReplayFrameLoader.Cancel();
        try {
            ReplayFrameLoader.Finish(work);
            return true;
        } catch (System.Exception e) {
            HandleOpenError(windows, e);
            return false;
        }
    }

    private static System.Collections.IEnumerable OpenRecordSteps(WindowsManager windows, RecordDetail detail, bool acrossFrames, SharedRecordLink.Position jump) {
        if (GameSessionGuard.BlockIfExclusiveSession("阅览牌谱")) {
            SharedRecordLink.EndPublicSharePlayback();
            yield break;
        }
        var config = ConfigManager.Instance;
        if (acrossFrames && config != null && detail.rule != "hongque") {
            foreach (var operation in BuiltinTileSprites.PrepareReplayAtlas(config.StandardTilePackId)) yield return operation;
        }
        if (!windows.TryPrepareRecordScene(out GameRecordManager manager, out string error)) {
            throw new System.InvalidOperationException(error);
        }
        yield return null;
        RecordSetting.Instance?.SetShowCardsMode(!detail.perspective);
        // 是否可分享取决于云端保存标记，与从云端还是本地副本打开无关。
        foreach (var step in manager.LoadArchive(detail.record, jump?.Table ?? detail.selected_table_id,
            detail.legacy_cursor_map, detail.cloud_saved, detail.game_id, jump, copyTables: !acrossFrames)) yield return step;
    }

    private static void HandleOpenError(WindowsManager windows, System.Exception e) {
        SharedRecordLink.ClearPendingJump();
        Debug.LogError($"加载牌谱失败: {e}");
        if (windows.GetCurrentWindow() == "recordscene") {
            if (SharedRecordLink.IsPublicSharePlayback) {
                AppSession.ReturnToLogin();
            } else {
                GameSceneTeardown.ResetToIdle();
                windows.ExitGameToReturnWindow();
            }
        }
        SharedRecordLink.EndPublicSharePlayback();
        NotificationManager.Instance?.ShowTip("牌谱", false, $"无法打开牌谱: {e.Message}");
    }
}
