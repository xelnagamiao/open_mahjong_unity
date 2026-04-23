using System;
using System.Collections.Generic;
using Riichi;

/// <summary>
/// 立直麻将对外统一接口（封装 Riichi 命名空间内部实现）。
/// 提供三类入口：
///   1) HepaiCheck：能否和牌的快速判断，仅返回可行役形（用于手牌 tips）
///   2) FullHepaiCheck：完整役/番/符/点数计算（本地）
///   3) TingpaiCheck：听牌搜索
/// 实际服务端对局仍以服务端 mahjong 库结果为准；本地完整计算用于 tips 分数预测 / 离线测试。
/// </summary>
public static class RiichiExternal {
    public static Tuple<int, List<string>> HepaiCheck(
        List<int> handList,
        List<string> tilesCombination,
        List<string> wayToHepai,
        int getTile,
        bool debug = false) {
        return new RiichiHepaiCheck().HepaiCheck(
            handList, tilesCombination, wayToHepai, getTile);
    }

    /// <summary>
    /// 完整役/番/符/点数计算。返回 <see cref="RiichiHandResult"/>。
    /// </summary>
    public static RiichiHandResult FullHepaiCheck(
        List<int> handList,
        List<string> tilesCombination,
        int winTile,
        RiichiHandContext context) {
        return new RiichiHandCalculator().Calculate(handList, tilesCombination, winTile, context);
    }

    public static HashSet<int> TingpaiCheck(
        List<int> handTileList,
        List<string> combinationList,
        bool debug = false) {
        return RiichiTingpaiCheck.TingpaiCheckStatic(handTileList, combinationList, debug);
    }
}
