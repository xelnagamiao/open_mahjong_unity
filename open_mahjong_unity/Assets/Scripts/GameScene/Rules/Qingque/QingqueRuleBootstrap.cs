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
            StatsFanNames = RankConfig.QingqueFanTranslation,
            GameStateFactory = () => new QingqueGameState(),
            Tingpai = QingqueTips.Tingpai,
            DescribeWaitingTile = QingqueTips.Describe,
            FanValueText = QingqueFanText.FanValue,
            SettlementTotal = QingqueFanText.SettlementTotal,
            ScoreboardFanText = QingqueFanText.ScoreboardFanText,
            RoundName = RoundTextDictionary.WindNumberRoundName,
        });
    }
}
