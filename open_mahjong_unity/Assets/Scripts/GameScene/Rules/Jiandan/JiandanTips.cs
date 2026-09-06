using System.Collections.Generic;

/// <summary>
/// 简单麻将听牌与和牌张提示：只算静态手牌番；海底/岭上/抢杠等情境番以服务端结算为准。
/// </summary>
internal static class JiandanTips {
    public static HashSet<int> Tingpai(TingpaiQuery q) {
        return JiandanExternal.TingpaiCheck(q.Hand, q.Melds ?? new List<string>());
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        var result = JiandanExternal.HepaiCheck(q.HandWithWin, q.Melds, q.HepaiTile);
        return WaitTileHint.Ron($"{result.Item1}番");
    }
}
