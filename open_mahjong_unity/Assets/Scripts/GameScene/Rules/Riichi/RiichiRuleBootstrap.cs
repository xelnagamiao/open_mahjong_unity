using Riichi;
using UnityEngine;

internal static class RiichiRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RiichiRecordSettlementCodec.RegisterRecordCodecs();
        RuleRegistry.Register(new RuleManifest {
            ReplayFactory = record => record.CreateRiichiReplayModule(),
            CreateSettlementCodec = () => new RiichiRecordSettlementCodec(),
            BuildRoom = RiichiRoomBuilder.Build,
            RuleId = RiichiGameState.RuleId,
            DefaultSubRule = "riichi/standard",
            DisplayName = "日本麻将",
            RulebookPath = (subRule, config) => WashizuTile.IsWashizu(subRule) ? "/rulebook/washizu" : "/rulebook/riichi",
            PrepareHandDisplay = info => GameCanvas.Instance.PrepareWashizuRealtimeHand(info),
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
            LiveVisibleIndicators = () => RiichiGameState.Active != null ? RiichiGameState.Active.LiveVisibleIndicatorTiles() : System.Array.Empty<int>(),
            CollectLivePlayerVisibleTiles = (p, isSelf, add) => {
                if (GameSession.Current.SubRule == "riichi/sanma" && p.huapai_list != null)
                    foreach (int tile in p.huapai_list) add(tile);
                if (!WashizuTile.UsesTransparentHands(GameSession.Current.SubRule) || p.combination_masks == null) return false;
                foreach (var mask in p.combination_masks)
                    foreach (int tile in WashizuTile.VisibleMeldTiles(mask)) add(tile);
                return true;
            },
            FanNameText = RiichiFanText.FanName,
            FanValueText = RiichiFanText.FanValue,
            RoundName = RiichiRoundText.Name,
            ScoreboardFanText = RiichiFanText.ScoreboardFanText,
            SettlementTotal = RiichiFanText.SettlementTotal,
            PlaysGongHuSound = RiichiFanText.PlaysGongHu,
            NormalizeTileId = RiichiTileUtil.Normalize,
            HasNotenDeclaration = true,
            ActionCaption = word => word == "nuki" ? "拔北" : ActionWords.Is(word, ActionWordKind.Ron) ? "荣" : null,
            ActionVoice = word => word == "nuki" ? "nuki" : ActionWords.Is(word, ActionWordKind.Ron) ? "rong" : null,
            UsesRiichiDragonOrder = true,
            RoundIndicators = info => RoundIndicatorDisplay.Riichi(info?.honba ?? 0, info?.riichi_sticks ?? 0,
                info?.dora_indicators, info?.kan_dora_indicators),
        });
    }
}
