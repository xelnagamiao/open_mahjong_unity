using System;
using System.Collections.Generic;

internal static class SbrExchangeTips {
    public static int PassedFan(IEnumerable<string> tags) {
        int highest = -1;
        if (tags != null) foreach (string tag in tags)
            if (tag != null && tag.StartsWith("shunhe_", StringComparison.Ordinal)
                && int.TryParse(tag.Substring(7), out int fan)) highest = Math.Max(fan, highest);
        return highest;
    }

    public static int RecordedPassedFan(Round round, int seat, int node) {
        if (round?.sbrPassedFans == null || !round.sbrPassedFans.TryGetValue(seat, out var history)) return -1;
        int fan = -1;
        foreach (var entry in history) {
            if (entry.Key > node) break;
            fan = entry.Value;
        }
        return fan;
    }

    private static int Points(int fan) => 1 << Math.Min(fan, 5);

    public static WaitTileHint Describe(WaitHintQuery q) {
        if (!SichuanTips.HasMissingSuit(q.HandWithWin, q.Melds)) return WaitTileHint.None("未满足");
        var shape = SichuanExternal.HepaiCheck(q.HandWithWin, q.Melds ?? new List<string>(),
            new List<string>(), q.HepaiTile, q.ExcludedSuit, false);
        if (shape.Item2.Count == 0) return WaitTileHint.None("未满足");
        int ron = shape.Item1, draw = shape.Item1 + 1;
        var window = q.SbrWindow;
        if (window != null && window.tile == q.HepaiTile) {
            int extra = (window.kong ? 1 : 0) + (window.last ? 1 : 0) + (window.earth ? 4 : 0);
            if (window.draw) draw = shape.Item1 + (window.heaven ? 5 : 1) + extra;
            else ron += extra + (window.robbing ? 1 : 0);
        }
        bool canRon = ron >= q.HepaiLimit && ron > q.SbrPassedFan;
        bool canDraw = draw >= q.HepaiLimit;
        if (!canRon) return canDraw ? WaitTileHint.TsumoOnly($"仅自摸\n{Points(draw)}点") : WaitTileHint.None("未起和");
        if (!canDraw) return WaitTileHint.RonOnly($"仅点和\n{Points(ron)}点");
        return WaitTileHint.Ron(Points(ron) == Points(draw) ? $"{Points(ron)}点" : $"点和{Points(ron)}点\n自摸{Points(draw)}点");
    }
}
