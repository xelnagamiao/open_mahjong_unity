using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 玩家数据面板：UID 查询 + 排行榜 + 最近天梯对局列表。
/// </summary>
public class DataPanel : MonoBehaviour {
    public static DataPanel Instance { get; private set; }

    [SerializeField] private TMP_InputField useridInputField;
    [SerializeField] private Button searchUseridButton;

    [Header("排行榜")]
    [SerializeField] private Transform leaderboardContainer;
    [SerializeField] private LeaderboardItem leaderboardItemPrefab;

    [Header("最近天梯对局")]
    [SerializeField] private Transform ladderRecordContainer;
    [SerializeField] private LadderRecordItemPrefab ladderRecordItemPrefab;

    [SerializeField] private TMP_Dropdown ruleDropdown;
    [SerializeField] private TMP_Text leaderboardTitle, recordsTitle, leaderboardStatus, recordsStatus;
    [SerializeField] private Button refreshButton;
    public string CurrentRule { get; private set; } = "guobiao";
    private string requestId;
    private bool awaitingLeaderboard, awaitingRecords;
    private float requestTime;

    private void Update() {
        if (Time.unscaledTime-requestTime < 15) return;
        if(awaitingLeaderboard){awaitingLeaderboard=false;SetStatus(leaderboardStatus,"排行榜请求超时，请刷新重试");}
        if(awaitingRecords){awaitingRecords=false;SetStatus(recordsStatus,"对局请求超时，请刷新重试");}
    }
    private static void SetStatus(TMP_Text label,string message){if(label==null)return;label.text=message;label.gameObject.SetActive(!string.IsNullOrEmpty(message));}
    public void SelectRule(int index) {
        if(index<0||index>=RankedRules.Ids.Length)return;
        CurrentRule=RankedRules.Ids[index];
        ruleDropdown?.SetValueWithoutNotify(index);
        RefreshLadderRecords();
    }
    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ruleDropdown?.onValueChanged.AddListener(SelectRule);
        refreshButton?.onClick.AddListener(RefreshLadderRecords);
    }

    private void Start() {
        if (searchUseridButton != null) {
            searchUseridButton.onClick.AddListener(OnSearchUseridButtonClick);
        }
    }

    private void OnEnable() {
        RefreshLadderRecords();
    }

    /// <summary>
    /// 切换到本面板时拉取全服最近 20 局天梯对局。
    /// </summary>
    public void RefreshLadderRecords() {
        requestId=System.Guid.NewGuid().ToString("N");
        ClearLeaderboardItems();ClearLadderRecordItems();
        if(leaderboardTitle!=null)leaderboardTitle.text=RankedRules.Name(CurrentRule)+(RankedRules.IsGrade(CurrentRule)?" · 段位排行榜":" · Elo 排行榜");
        if(recordsTitle!=null)recordsTitle.text=RankedRules.Name(CurrentRule)+" · 最近对局";
        awaitingLeaderboard=awaitingRecords=false;
        if (UserDataManager.Instance==null||UserDataManager.Instance.UserId <= 0) {
            SetStatus(leaderboardStatus,"登录后查看排行榜");SetStatus(recordsStatus,"登录后查看最近对局");return;
        }
        awaitingLeaderboard=awaitingRecords=true;requestTime=Time.unscaledTime;
        SetStatus(leaderboardStatus,"正在加载排行榜…");SetStatus(recordsStatus,"正在加载最近对局…");
        DataNetworkManager.Instance?.GetRankRecordList(20,CurrentRule,requestId);
        DataNetworkManager.Instance?.GetLeaderboard(CurrentRule,requestId);
    }

    private void OnSearchUseridButtonClick() {
        string userid = useridInputField != null ? useridInputField.text : "";
        if (string.IsNullOrEmpty(userid)) return;
        if(int.TryParse(userid,out int id))ProfileOnClick.OpenPlayerInfo(id,CurrentRule);
        else NotificationManager.Instance?.ShowTip("查询玩家",false,"请输入有效的玩家 UID");
    }

    public void OnLeaderboardReceived(bool success, string message, LeaderboardEntry[] list, string rule = null, string responseId = null) {
        if(rule!=CurrentRule||responseId!=requestId)return;
        awaitingLeaderboard=false;
        SetStatus(leaderboardStatus,!success ? "排行榜加载失败，请刷新重试" : list==null||list.Length==0 ? "本规则暂无上榜玩家" : "");
        if (!success) {
            Debug.LogError($"获取排行榜失败: {message}");
            NotificationManager.Instance.ShowTip("排行榜", false, message);
            ClearLeaderboardItems();
            return;
        }

        ClearLeaderboardItems();

        if (list == null || list.Length == 0) return;
        if (leaderboardContainer == null || leaderboardItemPrefab == null) {
            Debug.LogWarning("DataPanel: 请在 Inspector 绑定 leaderboardContainer 与 leaderboardItemPrefab");
            return;
        }

        foreach (var entry in list) {
            LeaderboardItem item = Instantiate(leaderboardItemPrefab, leaderboardContainer);
            item.Bind(entry);
        }
    }

    public void OnRankRecordListReceived(bool success, string message, RecordInfo[] recordList, string rule = null, string responseId = null) {
        if(rule!=CurrentRule||responseId!=requestId)return;
        awaitingRecords=false;
        SetStatus(recordsStatus,!success ? "最近对局加载失败，请刷新重试" : recordList==null||recordList.Length==0 ? "本规则暂无最近对局" : "");
        ClearLadderRecordItems();

        if (!success) {
            Debug.LogError($"获取天梯对局失败: {message}");
            NotificationManager.Instance.ShowTip("天梯对局", false, message);
            return;
        }

        if (recordList == null || recordList.Length == 0) {
            return;
        }

        if (ladderRecordContainer == null || ladderRecordItemPrefab == null) {
            Debug.LogWarning("DataPanel: 请在 Inspector 绑定 ladderRecordContainer 与 ladderRecordItemPrefab");
            return;
        }

        foreach (var record in recordList) {
            LadderRecordItemPrefab item = Instantiate(ladderRecordItemPrefab, ladderRecordContainer);
            item.Bind(record);
        }
    }

    private void ClearLeaderboardItems() {
        if (leaderboardContainer == null) return;
        foreach (Transform child in leaderboardContainer) {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    private void ClearLadderRecordItems() {
        if (ladderRecordContainer == null) return;
        foreach (Transform child in ladderRecordContainer) {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }
}
