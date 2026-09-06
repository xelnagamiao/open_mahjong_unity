using UnityEngine;

internal static class QingqueRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = QingqueGameState.RuleId,
            DefaultSubRule = "qingque/standard",
            DisplayName = "青雀",
            LobbyOrder = 2,
            LobbySubRules = QingqueLobby.SubRules,
            CreateRoomDefaults = QingqueLobby.Defaults(),
            StatsFanNames = RankConfig.QingqueFanTranslation,
            GameStateFactory = () => new QingqueGameState(),
            Tingpai = QingqueTips.Tingpai,
            DescribeWaitingTile = QingqueTips.Describe,
            FanValueText = QingqueFanText.FanValue,
            RoundName = RoundTextDictionary.WindNumberRoundName,
        });
    }
}
