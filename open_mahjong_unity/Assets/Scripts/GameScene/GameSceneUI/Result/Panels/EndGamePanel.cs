using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class EndGamePanel : MonoBehaviour {
    [SerializeField] private RankDisplay[] rankDisplays;
    [SerializeField] private CanvasGroupFadeIn fadeInEffect;

    [System.Serializable]
    public class RankDisplay {
        public TextMeshProUGUI username;
        public TextMeshProUGUI score;
        public TextMeshProUGUI rank;
        public TextMeshProUGUI pt;
    }

    [SerializeField] private TextMeshProUGUI gameRandomSeed;
    [SerializeField] private Button goHomeButton;

    public static EndGamePanel Instance { get; private set; }

    // 排位赛段位变动数据（当前玩家）
    private bool isRankedMatch;
    private string rankBefore;
    private float scoreBefore;
    private string rankAfter;
    private float scoreAfter;
    private float ptChange;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 参数 key 为玩家标识（座位索引），按 rank 升序、原始风位升序、分数降序显示。
    /// </summary>
    public void ShowGameEndPanel(
        long game_random_seed,
        Dictionary<string, Dictionary<string, object>> player_final_data) {
        gameObject.SetActive(true);
        fadeInEffect?.PlayFadeIn();

        var sorted = player_final_data
            .Select(kv => kv.Value)
            .OrderBy(v => System.Convert.ToInt32(v["rank"]))
            .ThenBy(v => v.ContainsKey("original_player_index") && v["original_player_index"] != null
                ? System.Convert.ToInt32(v["original_player_index"])
                : int.MaxValue)
            .ThenByDescending(v => System.Convert.ToInt32(v["score"]))
            .ToList();

        for (int i = 0; i < sorted.Count && i < rankDisplays.Length; i++) {
            var playerData = sorted[i];
            var display = rankDisplays[i];
            string username = playerData["username"].ToString();
            int userId = playerData.ContainsKey("user_id") && playerData["user_id"] != null
                ? System.Convert.ToInt32(playerData["user_id"])
                : 0;
            display.username.text = StreamerModeHelper.FormatGamestatePlayerName(username, null, userId);
            display.score.text = playerData["score"].ToString();
            display.rank.text = playerData["rank"].ToString();
            display.pt.text = $"{System.Convert.ToSingle(playerData["pt"]):F1}";
        }
        // 显示游戏随机种子
        gameRandomSeed.text = "游戏随机种子: " + game_random_seed.ToString();

        // 检测是否为排位赛（当前玩家有 rank_before 字段）
        isRankedMatch = false;
        string myUsername = UserDataManager.Instance.Username;
        foreach (var d in player_final_data.Values) {
            if (d["username"].ToString() != myUsername) {
                continue;
            }
            if (d.ContainsKey("rank_before") && d["rank_before"] != null) {
                isRankedMatch = true;
                rankBefore = d["rank_before"].ToString();
                scoreBefore = System.Convert.ToSingle(d["score_before"]);
                rankAfter = d["rank_after"].ToString();
                scoreAfter = System.Convert.ToSingle(d["score_after"]);
                ptChange = System.Convert.ToSingle(d["pt"]);
            }
            break;
        }

        // 设置按钮点击事件
        goHomeButton.onClick.RemoveAllListeners();
        goHomeButton.onClick.AddListener(OnGoHomeButtonClick);
    }

    private void OnGoHomeButtonClick() {
        gameObject.SetActive(false);
        if (isRankedMatch) {
            RankChangePanel.Instance.ShowRankChange(rankBefore, scoreBefore, rankAfter, scoreAfter, ptChange);
        } else {
            PostGameNavigator.NavigateAfterGameEnd();
        }
    }

    public void ClearEndGamePanel() {
        gameObject.SetActive(false);
    }
}
