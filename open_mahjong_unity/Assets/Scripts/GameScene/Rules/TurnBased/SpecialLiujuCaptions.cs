/// <summary>
/// 非荒牌的特殊流局 hu_class → 标题文案。日麻途中流局四种 + 九种九牌（日麻）/ 九老峰回（古典）。
/// 对局内由 RiichiGameState / ClassicalGameState 通过钩子使用；牌谱回放（GameRecordManager）无族实例，直接按规则名查。
/// </summary>
public static class SpecialLiujuCaptions {
    public static bool IsRiichiAbort(string huClass) {
        return huClass == "four_kan_abort"
            || huClass == "four_wind_abort"
            || huClass == "four_riichi_abort"
            || huClass == "three_ron_abort";
    }

    public static bool IsSpecialLiuju(string huClass) {
        return huClass == "jiuzhongjiupai" || IsRiichiAbort(huClass);
    }

    public static string RiichiAbortCaption(string huClass) {
        return huClass switch {
            "four_wind_abort" => "四风连打",
            "four_kan_abort" => "四杠散了",
            "four_riichi_abort" => "四人立直",
            "three_ron_abort" => "三家和流局",
            _ => "流局",
        };
    }

    /// <summary>九种九牌（日麻）/ 九老峰回（古典）。</summary>
    public static string JiuzhongjiupaiCaption(string roomRule) {
        return roomRule == "riichi" ? "九种九牌" : "九老峰回";
    }

    /// <summary>牌谱回放用：按规则名解析标题。</summary>
    public static string Resolve(string huClass, string roomRule) {
        if (huClass == "jiuzhongjiupai") {
            return JiuzhongjiupaiCaption(roomRule);
        }
        return RiichiAbortCaption(huClass);
    }
}
