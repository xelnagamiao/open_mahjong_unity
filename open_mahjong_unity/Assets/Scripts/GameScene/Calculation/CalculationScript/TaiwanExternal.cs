using System;
using System.Collections.Generic;
using Taiwan;

/// <summary>
/// 台湾麻将客户端提示入口。
/// </summary>
public static class TaiwanExternal {
    public static int ResolveFanTai(
        string fanName,
        int fallback,
        IDictionary<string, object> ruleValues) {
        return TaiwanRuleConfig.FromDictionary(ruleValues).ResolveFanTai(fanName, fallback);
    }

    /// <summary>
    /// 七抢一与八仙独立花胡只把动作语义替换为“花胡”。
    /// 八仙仍按自摸进行手牌与得点处理。
    /// 普通胡牌加计模式不替换；可放弃模式清除八仙待选后也会自然恢复普通自摸/和。
    /// </summary>
    public static string ResolveHuPresentationAction(
        string huClass,
        IList<string> fanNames,
        IDictionary<string, object> ruleValues) {
        if (fanNames == null) return huClass;
        bool hasEightImmortals = false;
        for (int i = 0; i < fanNames.Count; i++) {
            if (fanNames[i] == "七抢一") return "hu_flower";
            if (fanNames[i] == "八仙过海") {
                hasEightImmortals = true;
            }
        }
        if (huClass != "hu_self" || !hasEightImmortals) return huClass;

        string mode = "optional_separate";
        if (ruleValues != null
            && ruleValues.TryGetValue("eight_immortals_mode", out object rawMode)
            && rawMode != null) {
            mode = rawMode.ToString();
        }
        return mode == "optional_separate" || mode == "forced_separate" || mode == "compound"
            ? "hu_flower"
            : huClass;
    }

    public static HashSet<int> TingpaiCheck(
        List<int> handTileList,
        List<string> combinationList,
        IDictionary<string, object> ruleValues) {
        return TaiwanCalc.TingpaiCheck(
            handTileList,
            combinationList,
            TaiwanRuleConfig.FromDictionary(ruleValues));
    }

    public static Tuple<int, List<string>> HepaiCheck(
        List<int> handList,
        List<string> combinationList,
        int winningTile,
        bool isSelfDraw,
        int seatWind,
        int roundWind,
        List<int> flowers,
        IDictionary<string, object> ruleValues,
        string readyQualification = null) {
        return TaiwanCalc.HepaiCheck(
            handList,
            combinationList,
            winningTile,
            isSelfDraw,
            seatWind,
            roundWind,
            flowers,
            TaiwanRuleConfig.FromDictionary(ruleValues),
            readyQualification);
    }
}
