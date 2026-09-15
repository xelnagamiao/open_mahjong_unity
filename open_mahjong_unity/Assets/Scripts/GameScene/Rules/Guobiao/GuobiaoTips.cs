using System;
using System.Collections.Generic;

/// <summary>国标听牌与和牌张提示：按子规则选番表，未起和时改按自摸重算。</summary>
internal static class GuobiaoTips {
    public static HashSet<int> Tingpai(TingpaiQuery q) {
        return GBtingpai.TingpaiCheck(q.Hand, q.Melds, false);
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        Tuple<int, List<string>> ron = Check(q.SubRule, q.HandWithWin, q.Melds, q.MergedWay, q.HepaiTile);
        if (ron.Item1 - q.HuapaiCount >= q.HepaiLimit) {
            return WaitTileHint.Ron($"{ron.Item1}番");
        }
        Tuple<int, List<string>> zimo = Check(q.SubRule, q.HandWithWin, q.Melds, q.BuildZimoWay(), q.HepaiTile);
        return zimo.Item1 - q.HuapaiCount >= q.HepaiLimit
            ? WaitTileHint.TsumoOnly("仅自摸")
            : WaitTileHint.None("未起和");
    }

    private static Tuple<int, List<string>> Check(string subRule, List<int> hand, List<string> melds, List<string> way, int tile) {
        switch (subRule) {
            case "guobiao/xiaolin": {
                var r = GBhepaiXiaolin.HepaiCheck(hand, melds, way, tile, false);
                return GBhepaiXiaolin.FilterZeroValueFans(r.Item1, r.Item2);
            }
            case "guobiao/kshen":
                return GBhepaiKshen.HepaiCheck(hand, melds, way, tile, false);
            case "guobiao/lanshi":
                return GBhepaiLanshi.HepaiCheck(hand, melds, way, tile, false);
            default:
                return GBhepai.HepaiCheck(hand, melds, way, tile, false);
        }
    }
}
