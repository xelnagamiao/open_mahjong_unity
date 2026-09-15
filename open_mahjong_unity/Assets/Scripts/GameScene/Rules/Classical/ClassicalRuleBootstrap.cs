using UnityEngine;

internal static class ClassicalRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = ClassicalGameState.RuleId,
            DefaultSubRule = "classical/standard",
            DisplayName = "古典麻将",
            LobbyOrder = 6,
            LobbySubRules = ClassicalLobby.SubRules,
            CreateRoomDefaults = ClassicalLobby.Defaults(),
            HuTickFollowsShuhewei = true,
            RecordHuTileTickIndex = 7,
            GameStateFactory = () => new ClassicalGameState(),
            Tingpai = ClassicalTips.Tingpai,
            DescribeWaitingTile = ClassicalTips.Describe,
            FanValueText = ClassicalFanText.FanValue,
            RoundName = RoundTextDictionary.WindNumberRoundName,
            ScoreboardFanText = ClassicalFanText.ScoreboardFanText,
            SettlementTotal = ClassicalFanText.SettlementTotal,
            FuNameText = ClassicalFanText.FuName,
            FuValueText = ClassicalFanText.FuValue,
        });
    }
}
