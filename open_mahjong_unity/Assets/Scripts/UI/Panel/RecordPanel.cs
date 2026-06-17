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
    [SerializeField] private Button BackMenuButton;
    [SerializeField] private Button SearchRecordButton;

    [Header("无牌谱提示面板")]
    [SerializeField] private GameObject NoRecordPanel;

    [Header("牌谱ID搜索面板（根节点挂 PanelPopupTransition + CanvasGroup）")]
    [SerializeField] private PanelPopupTransition recordIdInputPopup;
    [SerializeField] private TMP_InputField RecordIdInputField;
    [SerializeField] private Button ConfirmLoadButton;
    [SerializeField] private Button CancelSearchButton;

    private const float LoadMoreScrollThreshold = 0.02f;

    private readonly HashSet<string> _loadedGameIds = new HashSet<string>();
    private bool _isLoadingMore;
    private bool _hasMore = true;
    private int _loadedCount;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
            return;
        }
        BackMenuButton.onClick.AddListener(BackMenu);
        SearchRecordButton.onClick.AddListener(OpenRecordIdInput);
        ConfirmLoadButton.onClick.AddListener(ConfirmLoadRecordById);
        CancelSearchButton.onClick.AddListener(CloseRecordIdInput);

        if (recordIdInputPopup != null) {
            recordIdInputPopup.gameObject.SetActive(false);
        }
        NoRecordPanel.SetActive(false);
        ClearRecordItems();

        if (recordScrollRect == null && dropdownContentTransform != null) {
            recordScrollRect = dropdownContentTransform.GetComponentInParent<ScrollRect>();
        }
        if (recordScrollRect != null) {
            recordScrollRect.onValueChanged.AddListener(OnRecordScrollChanged);
        }
    }

    private void OnDestroy() {
        if (recordScrollRect != null) {
            recordScrollRect.onValueChanged.RemoveListener(OnRecordScrollChanged);
        }
    }

    private void BackMenu() {
        WindowsManager.Instance.SwitchWindow("menu");
    }

    private void OpenRecordIdInput() {
        if (recordIdInputPopup == null) return;
        recordIdInputPopup.Show(() => {
            RecordIdInputField.text = "";
            RecordIdInputField.ActivateInputField();
        });
    }

    private void CloseRecordIdInput() {
        recordIdInputPopup?.Hide();
    }

    private void ConfirmLoadRecordById() {
        string id = RecordIdInputField.text.Trim();
        if (string.IsNullOrEmpty(id)) {
            NotificationManager.Instance.ShowTip("牌谱", false, "请输入牌谱ID");
            return;
        }
        recordIdInputPopup?.Hide();
        DataNetworkManager.Instance.GetRecordById(id);
    }

    private void OnRecordScrollChanged(Vector2 _) {
        if (!_hasMore || _isLoadingMore) return;
        if (recordScrollRect == null) return;
        if (recordScrollRect.verticalNormalizedPosition > LoadMoreScrollThreshold) return;
        RequestLoadMore();
    }

    private void RequestLoadMore() {
        if (_isLoadingMore || !_hasMore) return;
        _isLoadingMore = true;
        DataNetworkManager.Instance?.GetRecordList(_loadedCount);
    }

    private void ResetPaginationState() {
        _loadedGameIds.Clear();
        _loadedCount = 0;
        _hasMore = true;
        _isLoadingMore = false;
    }

    private void ClearRecordItems() {
        if (dropdownContentTransform == null) return;
        foreach (Transform child in dropdownContentTransform) {
            Destroy(child.gameObject);
        }
    }

    private void AppendRecordItem(RecordInfo record) {
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
            record.players
        );
    }

    public void GetRecordListResponse(bool success, string message, RecordInfo[] recordList, int offset = 0) {
        _isLoadingMore = false;

        if (!success) {
            Debug.LogError($"获取记录列表失败: {message}");
            if (offset == 0) {
                NotificationManager.Instance?.ShowTip("牌谱", false, message);
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
            Debug.Log("没有游戏记录");
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

        Debug.Log($"牌谱列表 offset={offset} 追加 {appended} 条，当前共 {_loadedCount} 条，hasMore={_hasMore}");
    }

    public void OnRecordDetailReceived(RecordDetail detail) {
        OpenRecord(detail);
    }

    /// <summary>
    /// 打开牌谱回放（天梯列表、牌谱面板等入口共用）。
    /// </summary>
    public static void OpenRecord(RecordDetail detail) {
        if (detail == null || detail.record == null) {
            NotificationManager.Instance?.ShowTip("牌谱", false, "牌谱数据为空");
            return;
        }

        // Dictionary 内嵌 JArray 时不能直接 SerializeObject，需经 JToken 还原
        string recordJson = JToken.FromObject(detail.record).ToString(Formatting.None);

        if (string.IsNullOrWhiteSpace(recordJson)) {
            NotificationManager.Instance?.ShowTip("牌谱", false, "牌谱内容为空");
            return;
        }

        WindowsManager.Instance?.SwitchWindow("recordscene");
        if (GameRecordManager.Instance == null) {
            NotificationManager.Instance?.ShowTip("牌谱", false, "牌谱场景未就绪");
            return;
        }

        try {
            GameRecordManager.Instance.LoadRecord(recordJson, detail.players);
        } catch (System.Exception e) {
            Debug.LogError($"加载牌谱失败: {e.Message}");
            NotificationManager.Instance?.ShowTip("牌谱", false, $"解析牌谱失败: {e.Message}");
        }
    }
}
