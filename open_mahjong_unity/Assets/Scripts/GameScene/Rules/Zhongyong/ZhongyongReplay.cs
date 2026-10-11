public partial class GameRecordManager {
    internal RuleReplayModule CreateZhongyongReplayModule() => CreateStandardReplayModule(
        ReadGameTitleString(gameRecord?.gameTitle, "sub_rule", "") == "zhongyong/standard" ? 14 : 0);
}
