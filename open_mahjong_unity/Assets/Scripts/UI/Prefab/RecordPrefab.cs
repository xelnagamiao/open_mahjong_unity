using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;


public class RecordPrefab : MonoBehaviour{
    [SerializeField] private TextMeshProUGUI Username1Text;
    [SerializeField] private TextMeshProUGUI Username2Text;
    [SerializeField] private TextMeshProUGUI Username3Text;
    [SerializeField] private TextMeshProUGUI Username4Text;
    [SerializeField] private TextMeshProUGUI Score1Text;
    [SerializeField] private TextMeshProUGUI Score2Text;
    [SerializeField] private TextMeshProUGUI Score3Text;
    [SerializeField] private TextMeshProUGUI Score4Text;
    [SerializeField] private TextMeshProUGUI HasRecordText;
    [SerializeField] private TextMeshProUGUI MainRuleText;

    [SerializeField] private TextMeshProUGUI SubRuleText;

    [SerializeField] private TextMeshProUGUI RecordedTimeText;
    [SerializeField] private Button LoadRecordButton;
    
    private string record_data_json;
    public void InitializeRecordItem(string username1, string username2, string username3, string username4,
    string score1, string score2, string score3, string score4,
    string hasRecord, string mainRule, string subRule, string recordedTime,
    string record_data_json
    )
    {
        Username1Text.text = username1;
        Username2Text.text = username2;
        Username3Text.text = username3;
        Username4Text.text = username4;
        Score1Text.text = score1;
        Score2Text.text = score2;
        Score3Text.text = score3;
        Score4Text.text = score4;
        HasRecordText.text = hasRecord;
        MainRuleText.text = mainRule;
        SubRuleText.text = subRule;
        RecordedTimeText.text = recordedTime;
        this.record_data_json = record_data_json;
    }

    private void Awake(){
        LoadRecordButton.onClick.AddListener(LoadRecord);
    }

    private void LoadRecord(){
        // 直接传递 JSON 字符串，由 LoadRecord 方法处理解析
        // 如果 LoadRecord 需要 Dictionary，可以在 GameRecordManager 中使用 SimpleJSON 或其他 JSON 库解析
        GameRecordManager.Instance.LoadRecord(record_data_json);
    }
}
