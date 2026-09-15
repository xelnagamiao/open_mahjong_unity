using UnityEngine;

internal static class ChangshaRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = ChangshaGameState.RuleId,
            DefaultSubRule = "changsha/classic_double_bird",
            DisplayName = "长沙麻将",
            LobbyOrder = 4,
            LobbySubRules = ChangshaLobby.SubRules,
            CreateRoomDefaults = ChangshaLobby.Defaults(),
            GameStateFactory = () => new ChangshaGameState(),
            Tingpai = ChangshaTips.Tingpai,
            DescribeWaitingTile = ChangshaTips.Describe,
            FanNameText = ChangshaFanText.FanName,
            FanValueText = ChangshaFanText.FanValue,
            RoundName = ChangshaFanText.RoundName,
            MaxRoundText = ChangshaFanText.MaxRoundText,
            ScoreboardFanText = ChangshaFanText.ScoreboardFanText,
            SettlementTotal = ChangshaFanText.SettlementTotal,
            ActionCaption = ChangshaActionCaption,
            HasFlowerReplacement = false,
            FaceDownAnkan = true,
            DefaultHepaiLimit = 1,
        });
    }

    /// <summary>长沙：明杠/暗杠/加杠都叫"开杠"；补花不飘字。</summary>
    private static string ChangshaActionCaption(string word) {
        switch (word) {
            case "gang":
            case "angang":
            case "jiagang":
                return "开杠";
            case "buhua":
                return string.Empty;
            default:
                return null;
        }
    }
}
