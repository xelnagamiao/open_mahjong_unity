using UnityEngine;

internal static class GuobiaoRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = GuobiaoGameState.RuleId,
            DefaultSubRule = "guobiao/standard",
            DisplayName = "国标麻将",
            LobbyOrder = 0,
            LobbySubRules = GuobiaoLobby.SubRules,
            CreateRoomDefaults = GuobiaoLobby.Defaults(),
            HasRankedStats = true,
            ShowsHepaiLimitInRoomList = true,
            StatsFanNames = RankConfig.GuobiaoFanTranslation,
            GameStateFactory = () => new GuobiaoGameState(),
            Tingpai = GuobiaoTips.Tingpai,
            DescribeWaitingTile = GuobiaoTips.Describe,
            FanNameText = GuobiaoFanText.FanName,
            FanValueText = GuobiaoFanText.FanValue,
            RoundName = RoundTextDictionary.WindSeatRoundName,
            PlaysGongHuSound = GuobiaoFanText.PlaysGongHu,
            SettlementFootnote = GuobiaoAngangCheck.Footnote,
            // 国标自摸也叫"和"，报声同
            ActionCaption = word => word == "hu_self" ? "和" : null,
            ActionVoice = word => word == "hu_self" || word == "hu_flower" ? "hu" : null,
            RonWinTileTravelsFromRiver = true,
            MidGameCuoheContinues = true,
            PreserveDragOnDraw = true,
        });
    }
}
