using UnityEngine;

internal static class SichuanRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = SichuanGameState.RuleId,
            DefaultSubRule = "sichuan/standard",
            DisplayName = "四川麻将",
            LobbyOrder = 3,
            LobbySubRules = SichuanLobby.SubRules,
            CreateRoomDefaults = SichuanLobby.Defaults(),
            KongReplacementFromFront = true,
            PeekAnkan = true,
            InfersDingqueFromDiscards = true,
            GameStateFactory = () => new SichuanGameState(),
            Tingpai = SichuanTips.Tingpai,
            DescribeWaitingTile = SichuanTips.Describe,
            FanValueText = SichuanFanText.FanValue,
            RoundName = SichuanFanText.RoundName,
            ScoreboardMainFanFromRoundLabel = true,
            ScoreboardHighlightsTsumoLoss = false,
            ScoreboardFanText = SichuanFanText.ScoreboardFanText,
            SettlementTotal = SichuanFanText.SettlementTotal,
            HasFlowerReplacement = false,
        });
    }
}
