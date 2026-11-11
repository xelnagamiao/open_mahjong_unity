using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Displays the authoritative three-part ledger without recalculating Mahjong rules.</summary>
public sealed class NanchangSettlementDisplay {
    public int[] WinChanges { get; private set; }
    public int[] JingChanges { get; private set; }
    public int[] KongChanges { get; private set; }
    public int[] Changes { get; private set; }

    public static NanchangSettlementDisplay Build(NanchangInfo info) {
        JObject ledger = info?.ledger;
        int[] win = ReadSeats(ledger, "win_changes"), jing = ReadSeats(ledger, "jing_changes"),
            kong = ReadSeats(ledger, "kong_changes"), total = ReadSeats(ledger, "changes");
        if (win == null || jing == null || kong == null || total == null) return null;
        return new NanchangSettlementDisplay {
            WinChanges = win, JingChanges = jing, KongChanges = kong, Changes = total,
        };
    }

    private static int[] ReadSeats(JObject ledger, string key) {
        if (!(ledger?[key] is JArray values) || values.Count != 4
            || values.Any(value => value.Type != JTokenType.Integer)) return null;
        return values.ToObject<int[]>();
    }

    public SettlementTotalDisplay TotalDisplay(int seat) => new SettlementTotalDisplay {
        Layout = SettlementTotalDisplay.LayoutKind.Breakdown,
        FanText = $"和牌 {WinChanges[seat]:+#;-#;0}分",
        FuText = $"精分 {JingChanges[seat]:+#;-#;0}分  杠分 {KongChanges[seat]:+#;-#;0}分",
        ScoreText = $"本副合计 {Changes[seat]:+#;-#;0}分",
    };

    /// <summary>
    /// Old records put only the hand payment in hu_*. The preceding win_source already
    /// carries the final ledger. Project its total for playback and seeking alike;
    /// leave the saved tick untouched, and let the later state tick set absolute scores.
    /// </summary>
    public static List<string> NormalizeWinTick(NanchangInfo info, List<string> tick) {
        if (tick == null || tick.Count < 5 || (tick[0] != "hu_self" && tick[0] != "hu_first"
            && tick[0] != "hu_second" && tick[0] != "hu_third")) return tick;
        if (!int.TryParse(tick[1], out int winner) || winner < 0 || winner >= 4
            || info?.ledger?["winner"]?.Type != JTokenType.Integer
            || info.ledger["winner"].Value<int>() != winner) return tick;
        var settlement = Build(info);
        if (settlement == null) return tick;
        var projected = new List<string>(tick);
        projected[4] = JsonConvert.SerializeObject(settlement.Changes);
        return projected;
    }
}
