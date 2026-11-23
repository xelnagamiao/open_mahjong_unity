using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>AI decisions at the canonical cursor immediately before the player's choice.</summary>
internal sealed class RiichiReviewReport {
    internal sealed class Candidate {
        public string Key;
        public int Tile;
        public double Probability;
        public JObject Ukeire;
        public string Exact, Short;
        public int DiscardRank;
    }

    internal sealed class Decision {
        public int Round, Node;
        public string Phase, TriggerTile, Actual, Recommended;
        public Candidate[] Choices;
        public readonly Dictionary<int, Candidate> Discards = new Dictionary<int, Candidate>();
        public string PhaseLabel => Phase switch {
            "draw" => "摸牌后", "after_call" => "鸣牌后", "after_reach" => "立直宣言后",
            "response" => "应对" + TileLabel(TriggerTile), _ => Phase
        };
        public string ChoiceText(string key) {
            var candidate = Array.Find(Choices, c => c.Key == key);
            return ActionLabel(key) + (candidate == null ? "" : "  " + candidate.Exact);
        }
    }

    public int Perspective { get; private set; }
    public readonly List<Decision> Decisions = new List<Decision>();
    private readonly Dictionary<(int, int), List<Decision>> byPosition = new Dictionary<(int, int), List<Decision>>();
    public List<Decision> At(int round, int node) => byPosition.TryGetValue((round, node), out var list) ? list : null;

    public static RiichiReviewReport Read(JObject data, string id, GameRecord game) {
        var rounds = game.gameRound.rounds;
        int seat = data.Value<int?>("perspective") ?? -1;
        if ((string)data["gameId"] != id || (string)data["table"] != "main" || (string)data["rule"] != "riichi/standard"
            || (int?)data["schema"] != 1 || (int?)data["roundCount"] != rounds.Count || seat < 0 || seat >= 4
            || !(data["entries"] is JArray entries) || entries.Count == 0)
            throw new InvalidDataException("分析与当前牌谱不匹配");
        var report = new RiichiReviewReport { Perspective = seat };
        foreach (var item in entries) {
            int round = (int)item["round"] + 1, node = (int)item["node"];
            var actual = (JObject)item["actual"];
            var react = item["response"]?["react"];
            string phase = (string)item["phase"], trigger = (string)item["triggerTile"];
            if (!rounds.TryGetValue(round, out var hand) || hand.actionTicks == null || node < 0 || node > hand.actionTicks.Count
                || (int?)react?["actor"] != seat || ((int?)actual["actor"] ?? seat) != seat
                || !(react?["pi"] is JObject pi) || !MatchesCursor(hand, node, actual, phase, trigger))
                throw new InvalidDataException("分析与当前回放节点不匹配");
            var rows = (react["info"]?["ukeire"]?["rows"] as JArray)?.OfType<JObject>()
                .ToDictionary(row => (string)row["action_name"]);
            var d = new Decision { Round = round, Node = node, Phase = phase, TriggerTile = trigger,
                Actual = ActualKey(actual), Recommended = (string)react["action"] };
            d.Choices = pi.Properties().Select(p => {
                double weight = (double)p.Value;
                if (!(weight >= 0 && weight <= 1)) throw new InvalidDataException("推荐度超出范围");
                return new Candidate { Key = p.Name, Tile = TileId(p.Name), Probability = weight,
                    Exact = FormatPercent(weight, 2), Short = FormatPercent(weight, 1),
                    Ukeire = rows != null && rows.TryGetValue(p.Name, out var row) ? row : null };
            }).OrderByDescending(c => c.Probability).ThenBy(c => c.Key, StringComparer.Ordinal).ToArray();
            if (!d.Choices.Any(c => c.Key == d.Recommended)) throw new InvalidDataException("缺少推荐动作");
            if (phase != "response")
                foreach (var choice in d.Choices)
                    if (choice.Tile != 0) {
                        d.Discards.Add(choice.Tile, choice);
                        choice.DiscardRank = d.Discards.Count;
                    }
            report.Decisions.Add(d);
            if (!report.byPosition.TryGetValue((round, node), out var list))
                report.byPosition.Add((round, node), list = new List<Decision>());
            list.Add(d);
        }
        // A declaration and its discard can share a cursor. Keep their provider order.
        report.Decisions.Sort((a, b) => a.Round != b.Round ? a.Round.CompareTo(b.Round) : a.Node.CompareTo(b.Node));
        return report;
    }

    private static bool MatchesCursor(Round round, int node, JObject actual, string phase, string trigger) {
        bool IsDiscard(List<string> tick, string tile) => tick.Count >= 2 && tick[0] == "c"
            && int.TryParse(tick[1], out int id) && id == TileId(tile);
        if (phase == "response" && (node == 0 || !IsDiscard(round.actionTicks[node - 1], trigger))) return false;
        return (string)actual["type"] != "dahai"
            || (node < round.actionTicks.Count && IsDiscard(round.actionTicks[node], (string)actual["pai"]));
    }

    private static string ActualKey(JObject action) {
        string type = (string)action["type"];
        if (type == "dahai") return (string)action["pai"];
        if (type == "chi") {
            int rank = ((string)action["pai"])[0];
            int lower = action["consumed"].Count(t => ((string)t)[0] < rank);
            return new[] { "chi_low", "chi_mid", "chi_high" }[lower];
        }
        return type switch { "reach" => "riichi", "hora" => "agari", "ankan" or "kakan" or "daiminkan" => "kan", _ => type };
    }

    internal static int TileId(string key) {
        if (string.IsNullOrEmpty(key)) return 0;
        if (key.Length == 1) {
            int wind = "ESWNCPF".IndexOf(key[0]);
            return wind < 0 ? 0 : 41 + wind;
        }
        if (key.Length > 3 || key[0] < '1' || key[0] > '9') return 0;
        int suit = "mps".IndexOf(key[1]) + 1;
        if (suit == 0) return 0;
        if (key.Length == 3) return key[2] == 'r' && key[0] == '5' ? suit * 100 + 5 : 0;
        return suit * 10 + (key[0] - '0');
    }

    internal static string TileLabel(string key) {
        int id = TileId(key);
        if (id == 0) return key ?? "";
        if (id >= 41 && id <= 47) return "东南西北中白发"[id - 41].ToString();
        return (key.Length == 3 ? "赤" : "") + key[0] + "万筒索"["mps".IndexOf(key[1])];
    }

    internal static string ActionLabel(string key) => key switch {
        "chi_low" => "吃（低张）", "chi_mid" => "吃（中张）", "chi_high" => "吃（高张）",
        "pon" => "碰", "kan" => "杠", "riichi" => "立直", "agari" => "和牌", "pass" => "过",
        _ => TileId(key) == 0 ? key : "打" + TileLabel(key)
    };

    private static string FormatPercent(double probability, int digits) {
        double minimum = digits == 1 ? .001 : .0001;
        if (probability > 0 && probability < minimum) return digits == 1 ? "<0.1%" : "<0.01%";
        return (probability * 100).ToString(digits == 1 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + "%";
    }
}
