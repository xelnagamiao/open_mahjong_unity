using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json;

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
        string recordJson = JsonConvert.SerializeObject(detail.record);
        WindowsManager.Instance.SwitchWindow("recordscene");
        GameRecordManager.Instance.LoadRecord(recordJson, detail.players);
    }
}
