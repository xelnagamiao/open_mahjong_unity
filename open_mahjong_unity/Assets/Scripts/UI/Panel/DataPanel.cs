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



    private void Awake() {

        if (Instance != null && Instance != this) {

            Destroy(gameObject);

            return;

        }

        Instance = this;

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

        if (UserDataManager.Instance == null || UserDataManager.Instance.UserId <= 0) {

            ClearLadderRecordItems();

            return;

        }

        DataNetworkManager.Instance?.GetRankRecordList(20);

    }



    private void OnSearchUseridButtonClick() {

        string userid = useridInputField != null ? useridInputField.text : "";

        if (string.IsNullOrEmpty(userid)) return;

        DataNetworkManager.Instance.GetGuobiaoStats(userid, need_player_info: true);

    }



    public void OnLeaderboardReceived(bool success, string message, LeaderboardEntry[] list) {

        if (!success) {

            Debug.LogError($"获取排行榜失败: {message}");

            NotificationManager.Instance?.ShowTip("排行榜", false, message);

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



    public void OnRankRecordListReceived(bool success, string message, RecordInfo[] recordList) {

        ClearLadderRecordItems();



        if (!success) {

            Debug.LogError($"获取天梯对局失败: {message}");

            NotificationManager.Instance?.ShowTip("天梯对局", false, message);

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

            Destroy(child.gameObject);

        }

    }



    private void ClearLadderRecordItems() {

        if (ladderRecordContainer == null) return;

        foreach (Transform child in ladderRecordContainer) {

            Destroy(child.gameObject);

        }

    }

}


