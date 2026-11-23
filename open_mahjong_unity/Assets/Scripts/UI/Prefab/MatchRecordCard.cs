using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared data and replay entry for the paired-record card layout variants.</summary>
public sealed class MatchRecordCard : MonoBehaviour {
    [SerializeField] private TMP_Text ruleLabel;
    [SerializeField] private TMP_Text dateLabel;
    [SerializeField] private TMP_Text identityLabel;
    [SerializeField] private TMP_Text[] playerLabels = new TMP_Text[8];
    [SerializeField] private TMP_Text[] scoreLabels = new TMP_Text[8];
    [SerializeField] private TMP_Text[] teamLabels = new TMP_Text[8];
    [SerializeField] private Image[] teamBadges = new Image[8];
    [SerializeField] private Button replayButton;
    [SerializeField] private bool spectatorStyle;
    private RecordDetail detail;
    private bool localPlayback;

    private void Awake() => replayButton.onClick.AddListener(Replay);
    private void OnDestroy() => replayButton.onClick.RemoveListener(Replay);

    public void Bind(RecordDetail value, bool local = false) {
        detail = value;
        localPlayback = local;
        var match = value?.record;
        var personalImp = PersonalImpSummary.Build(match);
        var keys = RuleNameDictionary.RecordKeys(value);
        string ruleName = value == null ? "牌谱不可用" : RuleNameDictionary.GetRecordName(keys.rule, keys.subRule);
        if (ruleLabel.TryGetComponent<LocalizedPlatformLabel>(out var ruleBinding)) ruleBinding.SetSource(ruleName);
        else ruleLabel.text = PlatformText.Get(ruleName);
        dateLabel.text = value?.created_at ?? "";
        identityLabel.text = string.IsNullOrEmpty(value?.game_id) ? "" : "ID " + value.game_id;
        replayButton.interactable = match?["tables"] is JObject tables && tables.HasValues && value.record != null;
        for (int table = 0; table < 2; table++) {
            var players = match?["tables"]?[table == 0 ? "open" : "closed"]?["players"]?.ToObject<PlayerRecordInfo[]>()
                ?? Array.Empty<PlayerRecordInfo>();
            for (int seat = 0; seat < 4; seat++) {
                // Original seats, not rank order: the same columns remain comparable across rooms.
                var player = players.FirstOrDefault(p => p != null && p.original_player_index == seat);
                int index = table * 4 + seat;
                playerLabels[index].text = player?.username ?? "暂无数据";
                scoreLabels[index].text = player == null ? "—" : PersonalImpSummary.ScoreText(personalImp,table == 0 ? "open" : "closed",seat);
                int? team = player == null ? null : ResolveTeam(match, table == 0 ? "open" : "closed", seat);
                Color color = team == 0 ? new Color32(35,108,192,255) : team == 1 ? new Color32(198,45,59,255) : new Color32(104,116,132,255);
                if (teamLabels[index] != null) teamLabels[index].text = team == 0 ? "蓝" : team == 1 ? "红" : "—";
                teamBadges[index].color = spectatorStyle
                    ? team == 0 ? new Color32(38,60,83,255) : team == 1 ? new Color32(78,40,45,255) : new Color32(46,46,46,255)
                    : color;
                teamBadges[index].gameObject.SetActive(true);
                scoreLabels[index].color = spectatorStyle ? Color.white : color;
            }
        }
    }

    public static int? ResolveTeam(JObject match, string tableId, int seat) {
        if (match?["teams"] is JArray teams) {
            foreach (var team in teams) {
                if (!(team["members"] is JArray members)) continue;
                foreach (var member in members)
                    if ((string)member["table_id"] == tableId && (int?)member["original_player_index"] == seat) {
                        int? id = (int?)team["team_id"];
                        return id == 0 || id == 1 ? id : null;
                    }
            }
        }
        var roster = (match?["tables"]?[tableId]?["payload"] as JObject)?["game_title"]?["team_by_player_index"] as JArray;
        if (roster == null || seat < 0 || seat >= roster.Count || roster[seat].Type != JTokenType.Integer) return null;
        int value = (int)roster[seat];
        return value == 0 || value == 1 ? (int?)value : null;
    }

    public static string ResolveOutcome(JObject match, string tableId, int seat) {
        int? team = ResolveTeam(match, tableId, seat);
        if (!team.HasValue) return "—";
        string outcome = ResolveMatchOutcome(match);
        if (outcome == "平局") return "平";
        if (outcome == "蓝胜") return team == 0 ? "胜" : "负";
        if (outcome == "红胜") return team == 1 ? "胜" : "负";
        return "—";
    }

    public static string ResolveMatchOutcome(JObject match) {
        if (!(match?["result"] is JObject result)) return "—";
        var winner = result["winner_team_id"];
        if (winner?.Type == JTokenType.Integer) {
            int id = (int)winner;
            return id == 0 ? "蓝胜" : id == 1 ? "红胜" : "—";
        }
        var points = result["match_points"];
        var blue = points is JArray array && array.Count == 2 ? array[0] : (points as JObject)?["0"];
        var red = points is JArray list && list.Count == 2 ? list[1] : (points as JObject)?["1"];
        bool Number(JToken token) => token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float);
        return winner?.Type == JTokenType.Null && Number(blue) && Number(red) &&
            (double)blue == .5 && (double)red == .5 ? "平局" : "—";
    }

    public static string ResolveMatchOutcomeHeader(JObject match) {
        string outcome = ResolveMatchOutcome(match);
        if (outcome == "—") return outcome;
        var scores = match?["result"]?["imp_scores"];
        var blue = scores is JArray array && array.Count == 2 ? array[0] : (scores as JObject)?["0"];
        var red = scores is JArray list && list.Count == 2 ? list[1] : (scores as JObject)?["1"];
        if (blue?.Type != JTokenType.Integer || red?.Type != JTokenType.Integer) return outcome;
        long margin = outcome == "红胜" ? (long)red - (long)blue : (long)blue - (long)red;
        if (outcome == "平局") return margin == 0 ? outcome + " 0 IMP" : outcome;
        return outcome + " " + (margin >= 0 ? "+" : "") + margin.ToString(System.Globalization.CultureInfo.InvariantCulture) + " IMP";
    }

    public void Replay() {
        if (detail?.record == null) return;
        // The full match container enters the existing viewer; its table controls do the switching.
        RecordPanel.RequestOpenRecord(detail, localPlayback);
    }
}
