using UnityEngine;

internal static class QingqueRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            ReplayFactory = record => record.CreateStandardReplayModule(),
            BuildRoom = QingqueRoomBuilder.Build,
            RuleId = QingqueGameState.RuleId,
            DefaultSubRule = "qingque/standard",
            DisplayName = "青雀",
            LobbyOrder = 2,
            LobbySubRules = QingqueLobby.SubRules,
            CreateRoomDefaults = QingqueLobby.Defaults(),
            DefaultHepaiLimit = 1,
            PeekAnkan = true,
            StatsFanNames = RankConfig.QingqueFanTranslation,
            GameStateFactory = () => new QingqueGameState(),
            Tingpai = QingqueTips.Tingpai,
            DescribeWaitingTile = QingqueTips.Describe,
            FanValueText = QingqueFanText.FanValue,
            PlaysGongHuSound = (rule, fans, score) => score > 18.5,
            SettlementTotal = QingqueFanText.SettlementTotal,
            ScoreboardFanText = QingqueFanText.ScoreboardFanText,
            ScoreboardMainFanRank = QingqueFanText.ScoreboardMainFanRank,
            RoundName = RoundTextDictionary.WindNumberRoundName,
            MaxRoundInfoText = info => info.unlimited_rounds ? "无固定时限" : RoundTextDictionary.GetMaxRoundText(info.max_round),
        });
    }
}
