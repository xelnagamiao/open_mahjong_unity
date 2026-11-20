using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Linq;

public class SpectatorPrefab : MonoBehaviour {
    public enum ColorStyle { AccentRail, TierHeader, SoftCard }
    [SerializeField] private TextMeshProUGUI RuleText;
    [SerializeField] private TextMeshProUGUI Player1NameText;
    [SerializeField] private TextMeshProUGUI Player2NameText;
    [SerializeField] private TextMeshProUGUI Player3NameText;
    [SerializeField] private TextMeshProUGUI Player4NameText;
    [SerializeField] private TextMeshProUGUI GamestateIdText;
    [SerializeField] private TextMeshProUGUI RoundProgressText;
    [SerializeField] private Button SpectateButton;
    [SerializeField] private ColorStyle colorStyle;
    [SerializeField] private Image cardBackground;
    [SerializeField] private Image headerBackground;
    [SerializeField] private Image playersBackground;
    [SerializeField] private Image accentRail;
    [SerializeField] private Image[] playerTeamBackgrounds = new Image[4];
    [SerializeField] private GameObject pairedPlayersRow;
    [SerializeField] private TextMeshProUGUI[] closedPlayerNames = new TextMeshProUGUI[4];
    [SerializeField] private Image[] closedTeamBackgrounds = new Image[4];
    [SerializeField] private TextMeshProUGUI openRoomLabel;
    [SerializeField] private TextMeshProUGUI closedRoomLabel;
    private static readonly Color OriginalCard = new Color32(82, 82, 82, 255);

    private string gamestate_id;
    private string match_id;
    private string initial_table;

    public void InitializeSpectatorItem(string rule, string subRule, string player1_name, string player2_name, string player3_name, string player4_name, string gamestate_id) {
        InitializeSpectatorItem(new SpectatorInfo { rule = rule, sub_rule = subRule,
            player1_name = player1_name, player2_name = player2_name, player3_name = player3_name,
            player4_name = player4_name, gamestate_id = gamestate_id });
    }

    public void InitializeSpectatorItem(SpectatorInfo info) {
        match_id = info.match_id;
        initial_table = info.table_id ?? "open";
        string displayRule = string.IsNullOrEmpty(info.sub_rule) ? info.rule : info.sub_rule;
        string ruleDisplay = RuleNameDictionary.GetWholeName(displayRule);
        if (RuleText != null) RuleText.text = ruleDisplay;
        string[] names = { info.player1_name, info.player2_name, info.player3_name, info.player4_name };
        TextMeshProUGUI[] labels = { Player1NameText, Player2NameText, Player3NameText, Player4NameText };
        bool teams = SpectatorRoomPresentation.HasTeams(info);
        for (int i = 0; i < labels.Length; i++) {
            if (labels[i] != null) {
                labels[i].richText = false;
                labels[i].text = string.IsNullOrEmpty(names[i]) ? "—" : names[i];
            }
            if (i < playerTeamBackgrounds.Length && playerTeamBackgrounds[i] != null) {
                var background = playerTeamBackgrounds[i];
                background.gameObject.SetActive(teams);
                if (teams) background.color = info.team_by_player_index[i] == 0
                    ? new Color32(38, 60, 83, 255) : new Color32(78, 40, 45, 255);
            }
        }
        if (GamestateIdText != null) {
            GamestateIdText.richText = false;
            GamestateIdText.text = $"游戏 ID  {info.gamestate_id}";
        }
        if (RoundProgressText != null) RoundProgressText.text = SpectatorRoomPresentation.RoundProgress(info);
        ApplyColor(info);
        if (SpectateButton != null) SpectateButton.interactable = !string.IsNullOrWhiteSpace(info.gamestate_id);
        gamestate_id = info.gamestate_id;
        if (pairedPlayersRow != null) pairedPlayersRow.SetActive(!string.IsNullOrEmpty(match_id));
        if (!string.IsNullOrEmpty(match_id) && pairedPlayersRow != null) {
            var available = info.paired_tables ?? new[] { info };
            var open = available.FirstOrDefault(t => t.table_id == "open");
            var closed = available.FirstOrDefault(t => t.table_id == "closed");
            ApplyMatchRow(open, labels, playerTeamBackgrounds);
            ApplyMatchRow(closed, closedPlayerNames, closedTeamBackgrounds);
            if (openRoomLabel != null) openRoomLabel.text = "开室";
            if (closedRoomLabel != null) closedRoomLabel.text = "闭室";
            if (RoundProgressText != null) RoundProgressText.text = SpectatorRoomPresentation.RoundProgress(open) + "  " + SpectatorRoomPresentation.RoundProgress(closed);
            if (GamestateIdText != null) GamestateIdText.text = "比赛 " + match_id.Substring(0, System.Math.Min(match_id.Length, 8));
        }
    }

    private static void ApplyMatchRow(SpectatorInfo info, TextMeshProUGUI[] labels, Image[] backgrounds) {
        var names = new[] { info?.player1_name, info?.player2_name, info?.player3_name, info?.player4_name };
        for (int i = 0; i < 4; i++) {
            if (i < labels.Length && labels[i] != null) { labels[i].richText = false; labels[i].text = names[i] ?? "暂不可用"; }
            if (i < backgrounds.Length && backgrounds[i] != null) {
                bool teams = info != null && SpectatorRoomPresentation.HasTeams(info);
                backgrounds[i].gameObject.SetActive(teams);
                if (teams) backgrounds[i].color = info.team_by_player_index[i] == 0 ? new Color32(38,60,83,255) : new Color32(78,40,45,255);
            }
        }
    }

    private void ApplyColor(SpectatorInfo info) {
        bool ranked = SpectatorRoomPresentation.IsRanked(info);
        Color accent = SpectatorRoomPresentation.Accent(info);
        Color darkAccent = Color.Lerp(new Color32(35, 35, 35, 255), accent, .34f);
        if (accentRail != null) {
            accentRail.gameObject.SetActive(ranked);
            accentRail.color = accent;
        }
        if (cardBackground != null) cardBackground.color = ranked && colorStyle == ColorStyle.SoftCard
            ? Color.Lerp(OriginalCard, accent, .2f) : OriginalCard;
        if (headerBackground != null) headerBackground.color = ranked && colorStyle != ColorStyle.AccentRail
            ? darkAccent : OriginalCard;
        if (playersBackground != null) playersBackground.color = ranked && colorStyle == ColorStyle.SoftCard
            ? Color.Lerp(new Color32(46, 46, 46, 255), accent, .17f) : new Color32(46, 46, 46, 255);
    }

    private void Awake() {
        if (SpectateButton != null) SpectateButton.onClick.AddListener(OnSpectateButtonClick);
    }

    private void OnDestroy() {
        if (SpectateButton != null) SpectateButton.onClick.RemoveListener(OnSpectateButtonClick);
    }

    private void OnSpectateButtonClick() {
        if (!string.IsNullOrEmpty(match_id)) { NetworkManager.Instance?.BeginMatchSpectating(match_id, initial_table); return; }
        if (!string.IsNullOrWhiteSpace(gamestate_id) && GameStateNetworkManager.Instance != null) {
            GameStateNetworkManager.Instance.AddSpectator(gamestate_id);
        }
    }
}
