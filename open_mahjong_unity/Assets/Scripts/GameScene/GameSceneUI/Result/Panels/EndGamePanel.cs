using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public partial class EndGamePanel : MonoBehaviour {
    [SerializeField] private RankDisplay[] rankDisplays;
    [SerializeField] private CanvasGroupFadeIn fadeInEffect;

    [System.Serializable]
    public class RankDisplay {
        public TextMeshProUGUI username;
        public TextMeshProUGUI score;
        public TextMeshProUGUI rank;
        public TextMeshProUGUI pt;
        public TextMeshProUGUI currency;
        public Image currencyIcon;
    }

    [SerializeField] private TextMeshProUGUI gameRandomSeed;
    [SerializeField] private Button goHomeButton;
    [SerializeField] private Button copyMasterSeedButton;
    [SerializeField] private Button returnToTableButton;

    public static EndGamePanel Instance { get; private set; }

    private string normalizedMasterSeed = "-";

    // 排位赛段位变动数据（当前玩家）
    private bool isRankedMatch;
    private string rankBefore;
    private float scoreBefore;
    private string rankAfter;
    private float scoreAfter;
    private float ptChange;
    private string ratingRule, ratingSystem;
    private float eloBefore, eloAfter;
    private float? ratingMatchPoints;
    private float ratingGameMultiplier, ratingTierMultiplier, ratingRankCost;
    private string ratingPointDetails;

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
        string master_seed,
        string commitment,
        string salt,
        Dictionary<string, Dictionary<string, object>> player_final_data, SichuanMatchSettlementInfo sichuanMatch = null) {
        SetRecordReturnAction(null);
        ShowGameEndPanelContent(master_seed, commitment, salt, player_final_data, sichuanMatch);
    }

    private void ShowGameEndPanelContent(string master_seed, string commitment, string salt,
        Dictionary<string, Dictionary<string, object>> player_final_data, SichuanMatchSettlementInfo sichuanMatch) {
        RestoreSichuanMatchChildren();
        gameObject.SetActive(true);
        if (!IsRecordResult) AutoAction.Instance?.DismissTimeoutReturnIfAny();
        fadeInEffect?.PlayFadeIn();


        var sorted = player_final_data
            .Select(kv => kv.Value)
            .OrderBy(v => System.Convert.ToInt32(v["rank"]))
            .ThenBy(v => v.TryGetValue("team_id", out object team) && team != null ? System.Convert.ToInt32(team) : -1)
            .ThenBy(v => v.TryGetValue("team_role", out object role) && role?.ToString() == "assistant" ? 1 : 0)
            .ThenBy(v => v.ContainsKey("original_player_index") && v["original_player_index"] != null
                ? System.Convert.ToInt32(v["original_player_index"])
                : int.MaxValue)
            .ThenByDescending(v => System.Convert.ToInt32(v["score"]))
            .ToList();

        for (int i = 0; i < rankDisplays.Length; i++) {
            var display = rankDisplays[i];
            bool visible = i < sorted.Count;
            display.username.transform.parent.gameObject.SetActive(visible);
            if (!visible) {
                display.username.text = "";
                display.score.text = "";
                display.rank.text = "";
                display.pt.text = "";
                if (display.currency) display.currency.text = "";
            }
        }

        for (int i = 0; i < sorted.Count && i < rankDisplays.Length; i++) {
            var playerData = sorted[i];
            var display = rankDisplays[i];
            string username = playerData["username"].ToString();
            int userId = playerData.ContainsKey("user_id") && playerData["user_id"] != null
                ? System.Convert.ToInt32(playerData["user_id"])
                : 0;
            display.username.text = StreamerModeHelper.FormatGamestatePlayerName(username, null, userId);
            display.score.text = FinalScoreText(playerData["score"]);
            display.rank.text = playerData["rank"].ToString();
            display.pt.text = RatingPointMarkup(FinalRatingText(playerData));
            if (display.currency) {
                bool saved = playerData.TryGetValue("currency_delta_minor", out object amount) && amount != null;
                display.currency.text = saved ? "+" + ShopPanel.Money(System.Convert.ToInt64(amount)) : "—";
            }
            if (playerData.TryGetValue("competition_points", out object competitionPoints) && competitionPoints != null)
                display.pt.text = $"{System.Convert.ToSingle(competitionPoints):0.#}盘分";
            if (playerData.TryGetValue("team_id", out object teamId) && teamId != null) {
                string scoring = playerData.TryGetValue("team_result_reason", out object resultReason) ? resultReason?.ToString() : null;
                bool paired = scoring == "imp" || scoring == "raw_difference";
                string teamName = System.Convert.ToInt32(teamId) == 0 ? (paired ? "蓝队" : "A队") : (paired ? "红队" : "B队");
                float points = System.Convert.ToSingle(playerData["team_match_points"]);
                string role = playerData.TryGetValue("team_role", out object roleValue) ? roleValue?.ToString() : null;
                bool captain = role == "captain" || role == "assistant";
                display.username.text = (captain ? WashizuDoubles.RoleLabel(System.Convert.ToInt32(teamId), role) : teamName) + " · " + display.username.text;
                display.rank.text = points == .5f ? "平" : points == 1 ? "胜" : "负";
                display.score.text = FinalScoreText(playerData["score"]);
                string scoreUnit = scoring == "imp" ? "IMP" : scoring == "raw_difference" ? "副差分" : "队分";
                display.pt.text = paired
                    ? $"全队 {playerData["team_score"]} {scoreUnit} · 两室合计"
                    : $"队分 {playerData["team_score"]} · {points:0.#}盘分";
                if (captain) display.pt.text = $"主将 {playerData["team_score"]}点 · {points:0.#}比赛分";
                if (paired && playerData.TryGetValue("team_small_score", out object smallScore) && smallScore != null
                    && playerData.TryGetValue("team_imp_score", out object impScore) && impScore != null)
                    display.pt.text = $"全队 {smallScore} 副分 · {impScore} IMP";
            }
        }
        normalizedMasterSeed = CommitmentSaltDisplay.NormalizeCommitment(master_seed);
        SetVerificationText(normalizedMasterSeed, commitment, salt);
        var teamResult = sorted.FirstOrDefault(v => v.ContainsKey("team_result_reason"));
        string resultNotice = null;
        if (teamResult != null) {
            string reason = teamResult["team_result_reason"]?.ToString();
            bool isDraw = reason == "draw" || (teamResult.TryGetValue("team_match_points", out object teamMatchPoints)
                && teamMatchPoints != null && System.Convert.ToSingle(teamMatchPoints) == .5f);
            resultNotice = isDraw ? "平局：各0.5盘分"
                : reason == "last_positive_board" ? "追平：同分追平队胜" : null;
        }
        if (!IsRecordResult && GameSession.Current.IsDuplicate) {
            string scoring = teamResult?["team_result_reason"]?.ToString();
            string title = scoring == "imp" ? "血战双打复式\n按 IMP 判定" : scoring == "raw_difference" ? "血流双打复式\n按副分判定" : "复式 · " + DuplicateWallDisplay.TypeName(GameSession.Current.DuplicateWallType);
            gameRandomSeed.text = title
                + "\n牌谱须在密钥解除锁定后到数据站查看，不保存本地牌谱。";
        }
        if (!string.IsNullOrEmpty(resultNotice)) {
            gameRandomSeed.text = resultNotice + "\n\n" + gameRandomSeed.text;
        }
        if (copyMasterSeedButton != null) copyMasterSeedButton.gameObject.SetActive(IsRecordResult ? normalizedMasterSeed != "-" : !GameSession.Current.IsDuplicate);

        // 检测是否为排位赛（当前玩家有 rank_before 字段）
        isRankedMatch = false;
        ratingRule=ratingSystem=null;
        eloBefore=eloAfter=0;
        ratingMatchPoints=null;
        ratingPointDetails=null;
        ratingGameMultiplier=ratingTierMultiplier=1;
        ratingRankCost=0;
        string myUsername = PlayerSession.Current.Username;
        if (!IsRecordResult) foreach (var d in player_final_data.Values) {
            bool samePlayer=d.TryGetValue("user_id",out var uid)&&uid!=null
                ? System.Convert.ToInt32(uid)==PlayerSession.Current.UserId : d["username"].ToString()==myUsername;
            if (!samePlayer) {
                continue;
            }
            if (d.ContainsKey("rank_before") && d["rank_before"] != null) {
                isRankedMatch = true;
                rankBefore = d["rank_before"].ToString();
                scoreBefore = System.Convert.ToSingle(d["score_before"]);
                rankAfter = d["rank_after"].ToString();
                scoreAfter = System.Convert.ToSingle(d["score_after"]);
                ptChange = System.Convert.ToSingle(d["pt"]);
                if(d.TryGetValue("rating_rule",out var rule)&&rule!=null){
                    ratingRule=rule.ToString();ratingSystem=d["rating_system"].ToString();
                    if(ratingSystem=="elo") {
                        eloBefore=System.Convert.ToSingle(d["elo_before"]);eloAfter=System.Convert.ToSingle(d["elo_after"]);
                    }
                    ptChange=System.Convert.ToSingle(d["rating_pt"]);
                    if (d.TryGetValue("rating_match_points",out var points) && points != null) {
                        ratingMatchPoints=System.Convert.ToSingle(points);
                        ratingGameMultiplier=System.Convert.ToSingle(d["rating_game_multiplier"]);
                        ratingTierMultiplier=System.Convert.ToSingle(d["rating_tier_multiplier"]);
                        ratingRankCost=System.Convert.ToSingle(d["rating_rank_cost"]);
                    }
                    PlayerSession.Current.UpdateRating(ratingRule,rankAfter,scoreAfter,eloAfter,System.Convert.ToInt32(d["rating_games"]));
                    if(ratingRule=="qingque"&&d.TryGetValue("rating_completed_hands",out var hands)&&hands!=null) {
                        double net=System.Convert.ToDouble(d["rating_net_points"]), avg=System.Convert.ToDouble(d["rating_average_points"]);
                        double ordinary=System.Convert.ToDouble(d["rating_base_delta"]), bonus=System.Convert.ToDouble(d["rating_protection_bonus"]);
                        ratingPointDetails=$"净素点 {net:+0;-0;0} · {hands}局 · 局均 {avg:+0.00;-0.00;0.00}\n表现 {ordinary:+0.00;-0.00;0.00} · 保护 {bonus:+0.00;-0.00;0.00}";
                    }
                }
            }
            break;
        }

        ShowSichuanMatch(sichuanMatch);

        // 设置按钮点击事件
        goHomeButton.onClick.RemoveAllListeners();
        goHomeButton.onClick.AddListener(OnGoHomeButtonClick);
        if (returnToTableButton) {
            returnToTableButton.onClick.RemoveAllListeners();
            returnToTableButton.onClick.AddListener(() => recordReturnAction?.Invoke());
            returnToTableButton.gameObject.SetActive(IsRecordResult && sichuanMatch == null);
        }
        if (copyMasterSeedButton != null) {
            copyMasterSeedButton.onClick.RemoveAllListeners();
            copyMasterSeedButton.onClick.AddListener(OnCopyVerificationClick);
        }
    }

    private void OnCopyVerificationClick() {
        ClipboardUtility.Copy(gameRandomSeed.text);
        GameHost.Current.ShowTip("牌局校验", true, "已复制校验码");
    }

    private void OnGoHomeButtonClick() {
        if (IsRecordResult) {
            PostGameNavigator.ExitToLobby(forceTeardown: true);
            return;
        }
        gameObject.SetActive(false);
        if (isRankedMatch) {
            if(ratingRule!=null)RankChangePanel.Instance.ShowRatedChange(ratingRule,ratingSystem,rankBefore,scoreBefore,rankAfter,scoreAfter,ptChange,eloBefore,eloAfter,
                ratingMatchPoints,ratingGameMultiplier,ratingTierMultiplier,ratingRankCost,ratingPointDetails);
            else RankChangePanel.Instance.ShowRankChange(rankBefore, scoreBefore, rankAfter, scoreAfter, ptChange);
        } else {
            PostGameNavigator.ExitToLobby(forceTeardown: true);
        }
    }

    public void ClearEndGamePanel() {
        RestoreSichuanMatchChildren();
        SetRecordReturnAction(null);
        gameObject.SetActive(false);
    }
}
