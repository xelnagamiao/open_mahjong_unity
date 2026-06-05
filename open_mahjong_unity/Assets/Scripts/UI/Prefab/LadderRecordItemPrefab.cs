using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 天梯对局列表条目：一行摘要 + 阅览牌谱按钮。
/// </summary>
public class LadderRecordItemPrefab : MonoBehaviour {
    [SerializeField] private TextMeshProUGUI summaryText;
    [SerializeField] private Button viewRecordButton;

    private string _gameId;

    private void Awake() {
        if (viewRecordButton != null) {
            viewRecordButton.onClick.AddListener(OnViewRecord);
        }
    }

    public void Bind(RecordInfo record) {
        if (record == null) return;
        _gameId = record.game_id;
        if (summaryText != null) {
            summaryText.text = LadderRecordDisplayText.FormatLine(record);
        }
    }

    private void OnViewRecord() {
        if (string.IsNullOrEmpty(_gameId)) return;
        DataNetworkManager.Instance?.GetRecordById(_gameId);
    }
}
