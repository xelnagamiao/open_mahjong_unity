using UnityEngine;

internal static class JiandanRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = JiandanGameState.RuleId,
            DefaultSubRule = "jiandan/standard",
            DisplayName = "简单麻将",
            LobbyOrder = 7,
            LobbyName = "南雀",
            LobbySubRules = JiandanLobby.SubRules,
            CreateRoomDefaults = JiandanLobby.Defaults(),
            StatsFanNames = JiandanFanText.FanNameToDisplayJiandan,
            GameStateFactory = () => new JiandanGameState(),
            OutboundChannel = "jiandan",
            Tingpai = JiandanTips.Tingpai,
            DescribeWaitingTile = JiandanTips.Describe,
            FanNameText = JiandanFanText.FanName,
            FanValueText = JiandanFanText.FanValue,
            RoundName = RoundTextDictionary.WindNumberRoundName,
            ScoreboardFanText = JiandanFanText.ScoreboardFanText,
        });
    }
}
