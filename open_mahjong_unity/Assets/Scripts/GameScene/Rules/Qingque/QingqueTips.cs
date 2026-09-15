using System.Collections.Generic;

/// <summary>青雀听牌与和牌张提示：番数可为小数，起和 1 番。</summary>
internal static class QingqueTips {
    public static HashSet<int> Tingpai(TingpaiQuery q) {
        return Qingque13External.TingpaiCheck(q.Hand, q.Melds ?? new List<string>(), false);
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        var ron = Qingque13External.HepaiCheck(q.HandWithWin, q.Melds, q.MergedWay, q.HepaiTile, false);
        double ronFan = ron.Item1;
        if (ronFan - q.HuapaiCount >= 1) {
            return WaitTileHint.Ron(FormatFan(ronFan));
        }
        var zimo = Qingque13External.HepaiCheck(q.HandWithWin, q.Melds, q.BuildZimoWay(), q.HepaiTile, false);
        double zimoFan = zimo.Item1;
        return zimoFan - q.HuapaiCount >= 1
            ? WaitTileHint.TsumoOnly(FormatFan(zimoFan))
            : WaitTileHint.None("无番");
    }

    private static string FormatFan(double fan) {
        return System.Math.Abs(fan % 1) < 0.0001
            ? $"{fan:F0}番"
            : $"{fan:F2}番".TrimEnd('0').TrimEnd('.');
    }
}
