using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Signed same-seat comparisons, converted per board independently of team standings.</summary>
public static class PersonalImpSummary {
    static readonly int[] SbrBounds = {0,2,5,8,11,15,19,25,31,39};
    static readonly int[] JjBounds = {0,9,29,59};

    public static JObject Build(JObject match) {
        var unavailable = new JObject { ["version"] = 1, ["status"] = "unavailable" };
        try {
            var profile = match?["rule_profile"];
            string scale = (string)profile?["imp_scale"];
            if ((string)match?["completeness"] != "complete" || (string)profile?["dealer_policy"] != "fixed_ESWN"
                || (scale != "sbr" && scale != "jj")) return unavailable;
            var opened = ReadBoards(match["tables"]?["open"]);
            var closed = ReadBoards(match["tables"]?["closed"]);
            if (opened == null || closed == null || opened.Count == 0 || opened.Count != closed.Count
                || opened.Keys.Any(key => !closed.ContainsKey(key))) return unavailable;
            var bounds = scale == "sbr" ? SbrBounds : JjBounds;
            var scores = new int[4];
            foreach (var board in opened) for (int seat = 0; seat < 4; seat++) {
                long difference = board.Value[seat] - closed[board.Key][seat];
                long magnitude = Math.Abs(difference);
                int value = Array.FindIndex(bounds, upper => magnitude <= upper);
                scores[seat] += Math.Sign(difference) * (value < 0 ? bounds.Length : value);
            }
            return new JObject { ["version"] = 1, ["status"] = "complete", ["method"] = "same_seat_per_board",
                ["unit"] = "IMP", ["imp_scale"] = scale, ["board_count"] = opened.Count,
                ["tables"] = new JObject { ["open"] = new JArray(scores), ["closed"] = new JArray(scores.Select(n => -n)) } };
        } catch (Exception error) when (error is JsonException || error is InvalidCastException || error is ArgumentException || error is OverflowException) {
            return unavailable;
        }
    }

    static Dictionary<string,long[]> ReadBoards(JToken table) {
        if ((string)table?["record_state"] != "complete" || !(table?["payload"]?["game_round"] is JObject rounds)) return null;
        var boards = new Dictionary<string,long[]>();
        foreach (var rd in rounds.Properties()) {
            var meta = table["round_meta"]?[rd.Name];
            string board = (string)meta?["board_instance_id"];
            var seats = rd.Value["seats"] as JArray;
            if (string.IsNullOrEmpty(board) || boards.ContainsKey(board) || (string)meta?["alignment_status"] != "exact"
                || seats == null || seats.Count != 4 || seats.Any(s => s.Type != JTokenType.Integer)
                || !seats.Select(s => (int)s).OrderBy(s => s).SequenceEqual(new[]{0,1,2,3})) return null;
            if (!(rd.Value["action_ticks"] is JArray ticks)) return null;
            var summaries = ticks.OfType<JArray>().Where(t => t.Count >= 3 &&
                ((string)t[0] == "liuju" || (string)t[0] == "blood") && (string)t[1] == "board_summary").ToArray();
            if (summaries.Length != 1) return null;
            var summary = summaries[0][2].Type == JTokenType.String ? JObject.Parse((string)summaries[0][2]) : summaries[0][2] as JObject;
            var values = new long[4];
            for (int seat = 0; seat < 4; seat++) {
                var change = summary?["changes"]?[(string)seats[seat]];
                if (change?.Type != JTokenType.Integer) return null;
                values[seat] = (int)change;
            }
            boards.Add(board, values);
        }
        return boards;
    }

    public static string ScoreText(JObject comparison, string table, int seat) {
        if ((int?)comparison?["version"] != 1 || (string)comparison?["status"] != "complete"
            || (string)comparison?["method"] != "same_seat_per_board" || (string)comparison?["unit"] != "IMP"
            || !(comparison?["tables"]?[table] is JArray scores) || scores.Count != 4 || seat < 0 || seat >= 4
            || scores[seat].Type != JTokenType.Integer) return "—";
        int value = (int)scores[seat];
        return (value > 0 ? "+" : "") + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " IMP";
    }
}
