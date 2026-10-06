using Riichi;
using UnityEngine;

internal static class RiichiRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = RiichiGameState.RuleId,
            DefaultSubRule = "riichi/standard",
            DisplayName = "日本麻将",
            LobbyOrder = 1,
            LobbyName = "立直麻将",
            LobbySubRules = RiichiLobby.SubRules,
            LobbyHasDetailedConfig = true,
            CreateRoomDefaults = RiichiLobby.Defaults(),
            RecordTracksRiichiField = true,
            ReplacementFromTailEnd = true,
            PeekAnkan = true,
            DefaultHepaiLimit = 1,
            StatsFanNames = RankConfig.RiichiFanTranslation,
            GameStateFactory = () => new RiichiGameState(),
            Tingpai = RiichiTips.Tingpai,
            DescribeWaitingTile = RiichiTips.Describe,
            FanNameText = RiichiFanText.FanName,
            FanValueText = RiichiFanText.FanValue,
            RoundName = RiichiRoundText.Name,
            ScoreboardFanText = RiichiFanText.ScoreboardFanText,
            SettlementTotal = RiichiFanText.SettlementTotal,
            PlaysGongHuSound = RiichiFanText.PlaysGongHu,
            NormalizeTileId = RiichiTileUtil.Normalize,
            HasNotenDeclaration = true,
            ActionCaption = word => word == "nuki" ? "拔北" : ActionWords.Is(word, ActionWordKind.Ron) ? "荣" : null,
            ActionVoice = word => ActionWords.Is(word, ActionWordKind.Ron) ? "rong" : null,
            UsesRiichiDragonOrder = true,
            RiichiRoundLayout = true,
        });
    }
}
