using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>Projects completed replays. Live spectators use released server scores.</summary>
public static class MatchReplayProjection {
    public sealed class Scores {
        public long? Blue, Red;
        public int SettledBoards;
        public string Reason;
        public bool Available => Blue.HasValue && Red.HasValue;
    }

    public static string SyncMode(MatchRecordSession session) {
        var values = new List<JToken> { session.Manifest?["sync_mode"] };
        foreach (string table in session.TableIds) {
            var payload = session.Table(table)?["payload"] as JObject;
            values.Add((payload?["game_title"] as JObject)?["duplicate_sync_mode"]);
            values.Add((payload?["duplicate_match"] as JObject)?["sync_mode"]);
        }
        var recorded = values.Where(v => v != null && v.Type != JTokenType.Null).ToArray();
        if (recorded.Any(v => v.Type != JTokenType.Boolean)) return "记录冲突";
        var modes = recorded.Select(v => (bool)v).Distinct().ToArray();
        return modes.Length > 1 ? "记录冲突" : modes.Length == 0 ? "未记录" : modes[0] ? "同步" : "非同步";
    }

    public static Scores ScoreAt(MatchRecordSession session, int roundIndex, int node) {
        Scores Missing(string reason) => new Scores { Reason = reason };
        if (session == null || session.IsLiveSession) return Missing("等待公开结算");
        var table = session.Table(session.ActiveTable);
        string roundKey = "round_index_" + roundIndex;
        string instance = (string)table?["round_meta"]?[roundKey]?["board_instance_id"];
        var catalog = session.Manifest?["board_catalog"] as JArray;
        var current = catalog?.OfType<JObject>().Where(b => (string)b["board_instance_id"] == instance).ToArray();
        if (string.IsNullOrEmpty(instance) || current?.Length != 1) return Missing("缺少副定位信息");
        int? number = (int?)current[0]["board_no"];
        if (!number.HasValue || number < 1) return Missing("副编号无效");
        var ticks = table?["payload"]?["game_round"]?[roundKey]?["action_ticks"] as JArray;
        if (ticks == null) return Missing("缺少结算记录");
        bool settled = ticks.Take(Math.Max(0, node)).OfType<JArray>().Any(IsSettlement);
        int through = number.Value - (settled ? 0 : 1);
        if (through == 0) return new Scores { Blue = 0, Red = 0, SettledBoards = 0 };
        var results = session.Manifest?["settlements"] as JArray;
        if (results == null) return Missing("牌谱未附 IMP");
        long blue = 0, red = 0;
        for (int board = 1; board <= through; board++) {
            var definitions = catalog.OfType<JObject>().Where(b => (int?)b["board_no"] == board).ToArray();
            if (definitions.Length != 1) return Missing("副定位信息冲突");
            string id = (string)definitions[0]["board_instance_id"];
            var rows = results.OfType<JObject>().Where(r => r["board_instance_id"]?.Type == JTokenType.String
                ? (string)r["board_instance_id"] == id : (int?)r["number"] == board).ToArray();
            if (rows.Length != 1) return Missing(rows.Length == 0 ? "牌谱未附 IMP" : "结算记录冲突");
            if (rows[0]["number"] != null && (int?)rows[0]["number"] != board) return Missing("结算副编号冲突");
            var scores = rows[0]["imp_scores"];
            JToken a = scores is JArray arr && arr.Count == 2 ? arr[0] : (scores as JObject)?["0"];
            JToken b = scores is JArray pair && pair.Count == 2 ? pair[1] : (scores as JObject)?["1"];
            if (a?.Type != JTokenType.Integer || b?.Type != JTokenType.Integer) return Missing("牌谱未附 IMP");
            try { checked { blue += (long)a; red += (long)b; } }
            catch (OverflowException) { return Missing("结算记录无效"); }
        }
        return new Scores { Blue = blue, Red = red, SettledBoards = through };
    }

    private static bool IsSettlement(JArray tick) => tick.Count > 0 && ((string)tick[0] == "end"
        || (tick.Count > 1 && ((string)tick[0] == "liuju" || (string)tick[0] == "blood") && (string)tick[1] == "board_summary"));

    public static string WallSource(JToken title) {
        string source = (string)title?["wall_source"];
        if (string.IsNullOrEmpty(source)) source = (string)title?["duplicate_wall_type"];
        switch (source) {
            case "training": return "训练牌山";
            case "key": return "复式密钥";
            case "seed": return "指定种子";
            case "manual": return "手动牌山";
            default: return "未记录";
        }
    }

    public static JObject Player(JObject table, int seat) => (table?["players"] as JArray)?.OfType<JObject>()
        .FirstOrDefault(p => (int?)p["original_player_index"] == seat);

    public static string PlayerName(JObject table, int seat, string missingName = "未记录") {
        string name = (string)Player(table, seat)?["username"];
        if (string.IsNullOrWhiteSpace(name)) name = (string)(table?["payload"] as JObject)?["game_title"]?[$"p{seat}_name"];
        return string.IsNullOrWhiteSpace(name) ? missingName : name;
    }

    public static int? Team(JObject table, int seat) {
        var teams = (table?["payload"] as JObject)?["game_title"]?["team_by_player_index"] as JArray;
        return teams != null && teams.Count == 4 && teams[seat].Type == JTokenType.Integer ? (int?)teams[seat] : null;
    }
}
