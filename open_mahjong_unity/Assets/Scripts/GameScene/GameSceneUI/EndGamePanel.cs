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
    /// 参数 key 为 username，按照 rank 值升序（1,2,3,4）排列后显示。
    /// </summary>
    public void ShowGameEndPanel(
        long game_random_seed,
        Dictionary<string, Dictionary<string, object>> player_final_data) {
        gameObject.SetActive(true);
        fadeInEffect?.PlayFadeIn();

        // 按 rank 值升序排列所有玩家数据
        var sorted = player_final_data.Values
            .OrderBy(d => System.Convert.ToInt32(d["rank"]))
            .ToList();

        for (int i = 0; i < sorted.Count && i < rankDisplays.Length; i++) {
            var playerData = sorted[i];
            var display = rankDisplays[i];
            display.username.text = playerData["username"].ToString();
            display.score.text = playerData["score"].ToString();
            display.rank.text = playerData["rank"].ToString();
            display.pt.text = $"{System.Convert.ToSingle(playerData["pt"]):F1}";
        }
        // 显示游戏随机种子
        gameRandomSeed.text = "游戏随机种子: " + game_random_seed.ToString();

        // 检测是否为排位赛（当前玩家有 rank_before 字段）
        isRankedMatch = false;
        string myUsername = UserDataManager.Instance.Username;
        if (player_final_data.TryGetValue(myUsername, out var myData)) {
            if (myData.ContainsKey("rank_before") && myData["rank_before"] != null) {
                isRankedMatch = true;
                rankBefore = myData["rank_before"].ToString();
                scoreBefore = System.Convert.ToSingle(myData["score_before"]);
                rankAfter = myData["rank_after"].ToString();
                scoreAfter = System.Convert.ToSingle(myData["score_after"]);
                ptChange = System.Convert.ToSingle(myData["pt"]);
            }
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
            WindowsManager.Instance.SwitchWindow("menu");
            Game3DManager.Instance.Clear3DTile();
        }
    }

    public void ClearEndGamePanel() {
        gameObject.SetActive(false);
    }
}
