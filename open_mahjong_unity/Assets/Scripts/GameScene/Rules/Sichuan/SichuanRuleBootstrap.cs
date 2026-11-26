using UnityEngine;

internal static class SichuanRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        SichuanRecordSettlementCodec.RegisterRecordCodecs();
        RuleRegistry.Register(new RuleManifest {
            ReplayFactory = record => record.CreateSichuanReplayModule(),
            CreateSettlementCodec = () => new SichuanRecordSettlementCodec(isSichuanFamily: true),
            BuildRoom = SichuanRoomBuilder.Build,
            RuleId = SichuanGameState.RuleId,
            DefaultSubRule = "sichuan/standard",
            FirstTenTileDiscardRow = FirstTenTileDiscardRow,
            DisplayName = "四川麻将",
            RulebookPath = (subRule, config) => subRule == SichuanLobby.SbrExchange
                ? "/rulebook/sichuan?sub_rule=" + System.Uri.EscapeDataString(subRule) : "/rulebook/sichuan",
            LobbyOrder = 3,
            LobbySubRules = SichuanLobby.SubRules,
            CreateRoomDefaults = SichuanLobby.Defaults(),
            ShowsHepaiLimitInRoomList = true,
            DefaultHepaiLimit = 0,
            KongReplacementFromFront = true,
            RecordUsesInlineKongScores = true,
            SupportsRobbedAddedKongSource = true,
            PeekAnkan = true,
            InfersDingqueFromDiscards = true,
            GameStateFactory = () => new SichuanGameState(),
            Tingpai = SichuanTips.Tingpai,
            DescribeWaitingTile = SichuanTips.Describe,
            RecordDangerUsesWaitHint = true,
            FanValueText = SichuanFanText.FanValue,
            RoundName = SichuanFanText.RoundName,
            MaxRoundText = rounds => $"{rounds * 4}副",
            ScoreboardMainFanFromRoundLabel = true,
            ScoreboardHighlightsTsumoLoss = false,
            ScoreboardFanText = SichuanFanText.ScoreboardFanText,
            SettlementTotal = SichuanFanText.SettlementTotal,
            HasFlowerReplacement = false,
        });
    }

    private static int FirstTenTileDiscardRow(string subRule) {
        if (subRule == SichuanLobby.DoublesDuplicateSubRule) return 4;
        if (SichuanLobby.IsDoubles(subRule)) return 3;
        if (SichuanLobby.IsXueliuDoubles(subRule)) return 4;
        return 0;
    }
}
