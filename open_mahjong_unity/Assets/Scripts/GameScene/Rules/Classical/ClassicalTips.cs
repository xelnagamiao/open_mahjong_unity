using System.Collections.Generic;

/// <summary>古典听牌与和牌张提示：以副数比起和线；和牌条件用"门风"并含"和牌"底副。</summary>
internal static class ClassicalTips {
    public static HashSet<int> Tingpai(TingpaiQuery q) {
        return ClassicalExternal.TingpaiCheck(q.Hand, q.Melds ?? new List<string>(), false);
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        List<string> ronWay = ToClassicalWay(q.MergedWay);
        var ron = ClassicalExternal.HepaiCheck(q.HandWithWin, q.Melds, ronWay, q.HepaiTile, false);
        if (ron.Item2 >= q.HepaiLimit) {
            return WaitTileHint.Ron($"{ron.Item2}副");
        }
        List<string> zimoWay = ToClassicalWay(q.BuildZimoWay());
        var zimo = ClassicalExternal.HepaiCheck(q.HandWithWin, q.Melds, zimoWay, q.HepaiTile, false);
        return zimo.Item2 >= q.HepaiLimit
            ? WaitTileHint.TsumoOnly("仅自摸")
            : WaitTileHint.None("无番");
    }

    /// <summary>"自风X" → "门风X"，并在最前插入"和牌"底副。</summary>
    private static List<string> ToClassicalWay(List<string> way) {
        var result = new List<string> { "和牌" };
        foreach (string w in way) {
            result.Add(w.StartsWith("自风") ? "门风" + w.Substring(2) : w);
        }
        return result;
    }
}
