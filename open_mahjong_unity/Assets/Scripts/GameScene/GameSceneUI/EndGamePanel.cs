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
    /// 参数 key 为顺位字符串 "1"～"4"，按顺位升序显示。
    /// </summary>
    public void ShowGameEndPanel(
        long game_random_seed,
        Dictionary<string, Dictionary<string, object>> player_final_data) {
        gameObject.SetActive(true);
        fadeInEffect?.PlayFadeIn();

        var sorted = player_final_data
            .OrderBy(kv => System.Convert.ToInt32(kv.Key))
            .Select(kv => kv.Value)
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
        HeaderPanel.Instance?.SetBackToGameVisible(false);
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
