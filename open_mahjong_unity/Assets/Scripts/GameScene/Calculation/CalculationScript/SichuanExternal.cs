using System;
using System.Collections.Generic;
using Sichuan;

/// <summary>
/// 四川麻将（血战到底）对外统一接口，封装 Sichuan 命名空间内部实现。
/// 与服务端 game_calculation/sichuan 对齐，供 tips（听牌/番数提示）本地计算使用。
/// </summary>
public static class SichuanExternal {
    /// <summary>
    /// 和牌检查。handList 含和牌张；返回 (总番数, 番名列表)。
    /// dingqueSuit ∈ {1:万,2:饼,3:条,0:不校验}；含定缺花色返回 (0, [])。
    /// </summary>
    public static Tuple<int, List<string>> HepaiCheck(
        List<int> handList,
        List<string> tilesCombination,
        List<string> wayToHepai,
        int getTile,
        int dingqueSuit = 0,
        bool includeSituational = true) {
        return new SichuanHepaiCheck().HepaiCheck(
            handList, tilesCombination, wayToHepai, getTile, dingqueSuit, includeSituational);
    }

    /// <summary>听牌检查。handTileList 为不含和牌张的手牌；不做定缺过滤。</summary>
    public static HashSet<int> TingpaiCheck(
        List<int> handTileList,
        List<string> combinationList) {
        return new SichuanTingpaiCheck().TingpaiCheck(handTileList, combinationList);
    }

    /// <summary>基本分 = 2^min(番数,3)；仅平和(0番)时基本分为 1。</summary>
    public static int BaseFromFan(int fan, List<string> fanList = null) {
        return SichuanHepaiCheck.BaseFromFan(fan, fanList);
    }
}
