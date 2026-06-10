using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class RecordPanel : MonoBehaviour {

    public static RecordPanel Instance { get; private set; }
    [SerializeField] private RecordPrefab RecordPrefab;
    [SerializeField] private Transform dropdownContentTransform;

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
        foreach (Transform child in dropdownContentTransform) {
            Destroy(child.gameObject);
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

    public void GetRecordListResponse(bool success, string message, RecordInfo[] recordList) {
        if (!success) {
            Debug.LogError($"获取记录列表失败: {message}");
            return;
        }

        if (recordList.Length == 0) {
            Debug.Log("没有游戏记录");
            NoRecordPanel.SetActive(true);
            return;
        }

        NoRecordPanel.SetActive(false);

        foreach (Transform child in dropdownContentTransform) {
            Destroy(child.gameObject);
        }

        foreach (var record in recordList) {
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

        Debug.Log($"成功加载 {recordList.Length} 条游戏记录");
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
