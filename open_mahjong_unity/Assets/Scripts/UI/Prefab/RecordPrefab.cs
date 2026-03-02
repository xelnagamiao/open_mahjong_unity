using UnityEngine;
using TMPro;
using UnityEngine.UI;


public class RecordPrefab : MonoBehaviour{
    [Header("基本信息")]
    [SerializeField] private TextMeshProUGUI RecordIdText;
    [SerializeField] private TextMeshProUGUI MainRuleText;
    [SerializeField] private TextMeshProUGUI SubRuleText;
    [SerializeField] private TextMeshProUGUI RecordedTimeText;

    [Header("排名位次")]
    [SerializeField] private TextMeshProUGUI Rank1Text;
    [SerializeField] private TextMeshProUGUI Rank2Text;
    [SerializeField] private TextMeshProUGUI Rank3Text;
    [SerializeField] private TextMeshProUGUI Rank4Text;

    [Header("玩家名")]
    [SerializeField] private TextMeshProUGUI Username1Text;
    [SerializeField] private TextMeshProUGUI Username2Text;
    [SerializeField] private TextMeshProUGUI Username3Text;
    [SerializeField] private TextMeshProUGUI Username4Text;

    [Header("分数")]
    [SerializeField] private TextMeshProUGUI Score1Text;
    [SerializeField] private TextMeshProUGUI Score2Text;
    [SerializeField] private TextMeshProUGUI Score3Text;
    [SerializeField] private TextMeshProUGUI Score4Text;

    [Header("按钮")]
    [SerializeField] private Button LoadRecordButton;
    [SerializeField] private Button CopyIdButton;
    
    private string gameId;
    private PlayerRecordInfo[] playersInfo;

    public void InitializeRecordItem(string gameId, string mainRule, string subRule, string recordedTime, PlayerRecordInfo[] players)
    {
        this.gameId = gameId;
        this.playersInfo = players;

        RecordIdText.text = gameId;
        MainRuleText.text = mainRule;
        SubRuleText.text = RuleNameDictionary.GetDisplayName(subRule, mainRule);
        RecordedTimeText.text = recordedTime;

        TextMeshProUGUI[] rankTexts = { Rank1Text, Rank2Text, Rank3Text, Rank4Text };
        TextMeshProUGUI[] usernameTexts = { Username1Text, Username2Text, Username3Text, Username4Text };
        TextMeshProUGUI[] scoreTexts = { Score1Text, Score2Text, Score3Text, Score4Text };

        if (players != null) {
            System.Array.Sort(players, (a, b) => a.rank.CompareTo(b.rank));
            for (int i = 0; i < 4; i++) {
                if (i < players.Length) {
                    rankTexts[i].text = $"{players[i].rank}位";
                    usernameTexts[i].text = players[i].username;
                    scoreTexts[i].text = players[i].score >= 0 ? $"+{players[i].score}" : players[i].score.ToString();
                } else {
                    rankTexts[i].text = "";
                    usernameTexts[i].text = "";
                    scoreTexts[i].text = "";
                }
            }
        }
    }

    private void Awake(){
        LoadRecordButton.onClick.AddListener(LoadRecord);
        CopyIdButton.onClick.AddListener(CopyRecordId);
    }

    private void LoadRecord(){
        DataNetworkManager.Instance.GetRecordById(gameId);
    }

    private void CopyRecordId(){
        GUIUtility.systemCopyBuffer = gameId;
        NotificationManager.Instance.ShowTip("牌谱", true, $"已复制牌谱ID: {gameId}");
    }
}
