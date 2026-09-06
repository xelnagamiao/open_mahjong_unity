using UnityEngine;

internal static class TaiwanRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = TaiwanGameState.RuleId,
            DefaultSubRule = "taiwan/standard",
            DisplayName = "台湾麻将",
            LobbyOrder = 5,
            LobbySubRules = TaiwanLobby.SubRules,
            CreateRoomDefaults = TaiwanLobby.Defaults(),
            LobbyHasDetailedConfig = true,
            GameStateFactory = () => new TaiwanGameState(),
            Tingpai = TaiwanTips.Tingpai,
            DescribeWaitingTile = TaiwanTips.Describe,
            FanValueText = TaiwanFanText.FanValue,
            RoundName = RoundTextDictionary.WindNumberRoundName,
            ScoreboardFanText = TaiwanFanText.ScoreboardFanText,
            SettlementTotal = TaiwanFanText.SettlementTotal,
            MidGameCuoheContinues = true,
            SupportsRobbedAddedKongSource = true,
            HuPresentationAction = TaiwanExternal.ResolveHuPresentationAction,
            ActionCaption = TaiwanActionCaption,
        });
    }

    /// <summary>台麻的听牌声明叫"报听"（按钮、飘字、选切返回键共用）。</summary>
    private static string TaiwanActionCaption(string word) {
        switch (word) {
            case "riichi":
            case "riichi_cut":
                return "报听";
            case "riichi_cut_cancel": return "取消报听";
            default: return null;
        }
    }
}
