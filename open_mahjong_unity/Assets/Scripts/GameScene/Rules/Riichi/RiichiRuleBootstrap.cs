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
            CreateRoomDefaults = RiichiLobby.Defaults(),
            RecordTracksRiichiField = true,
            PeekAnkan = true,
            DefaultHepaiLimit = 1,
            StatsFanNames = RankConfig.RiichiFanTranslation,
            GameStateFactory = () => new RiichiGameState(),
            Tingpai = RiichiTips.Tingpai,
            DescribeWaitingTile = RiichiTips.Describe,
            FanNameText = RiichiFanText.FanName,
            FanValueText = RiichiFanText.FanValue,
            RoundName = RoundTextDictionary.WindNumberRoundName,
            ScoreboardFanText = RiichiFanText.ScoreboardFanText,
            SettlementTotal = RiichiFanText.SettlementTotal,
            PlaysGongHuSound = RiichiFanText.PlaysGongHu,
            NormalizeTileId = RiichiTileUtil.Normalize,
            HasNotenDeclaration = true,
            ActionCaption = word => ActionWords.Is(word, ActionWordKind.Ron) ? "荣" : null,
            ActionVoice = word => ActionWords.Is(word, ActionWordKind.Ron) ? "rong" : null,
            UsesRiichiDragonOrder = true,
            RiichiRoundLayout = true,
        });
    }
}
