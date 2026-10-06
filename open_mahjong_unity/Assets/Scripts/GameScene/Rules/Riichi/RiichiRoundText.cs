internal static class RiichiRoundText {
    public static string Name(int round) {
        string subRule = GameSession.Current.SubRule;
        var record = GameRecordManager.Instance;
        if (record != null && record.gameObject.activeSelf && record.gameRecord?.gameTitle != null
            && record.gameRecord.gameTitle.TryGetValue("sub_rule", out object value)) subRule = value?.ToString();
        return subRule == "riichi/sanma" ? SanmaName(round) : RoundTextDictionary.WindNumberRoundName(round);
    }
    public static string SanmaName(int round) => round > 0
        ? new[] {"东", "南", "西", "北"}[((round - 1) / 3) % 4] + new[] {"一", "二", "三"}[(round - 1) % 3] + "局"
        : $"第{round}局";
}
