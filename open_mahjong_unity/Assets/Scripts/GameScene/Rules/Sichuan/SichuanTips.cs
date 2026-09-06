using System.Collections.Generic;

/// <summary>
/// 四川听牌与和牌张提示：任何合法牌型均可和，平和 0 番也直接展示；
/// 定缺花色的和牌张在听牌阶段就剔除。
/// </summary>
internal static class SichuanTips {
    public static HashSet<int> Tingpai(TingpaiQuery q) {
        HashSet<int> waiting = SichuanExternal.TingpaiCheck(q.Hand, q.Melds ?? new List<string>());
        int dingque = q.ExcludedSuit;
        if (dingque >= 1 && dingque <= 3) {
            waiting.RemoveWhere(w => (w / 10) == dingque);
        }
        return waiting;
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        var result = SichuanExternal.HepaiCheck(q.HandWithWin, q.Melds, new List<string>(), q.HepaiTile, q.ExcludedSuit, false);
        return WaitTileHint.Ron($"{result.Item1}番");
    }
}
