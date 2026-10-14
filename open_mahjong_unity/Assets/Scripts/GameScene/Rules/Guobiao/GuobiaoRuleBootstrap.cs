using UnityEngine;

internal static class GuobiaoRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            ReplayFactory = record => record.IsWashizuRealtimeRecord()
                ? record.CreateRealtimeTransparentReplayModule() : record.CreateStandardReplayModule(),
            BuildRoom = GuobiaoRoomBuilder.Build,
            RuleId = GuobiaoGameState.RuleId,
            DefaultSubRule = "guobiao/standard",
            RulebookText = (subRule, config) => subRule == GuobiaoGameState.HuahunSubRule ? HuahunRules.RulebookText : null,
            RulebookPath = (subRule, config) => subRule == GuobiaoGameState.SixteenSubRule ? "/rulebook/guobiao-sixteen" : subRule == GuobiaoGameState.HuahunSubRule ? "/rulebook/guobiao-huahun" : subRule == GuobiaoGameState.FourJokersSubRule ? "/rulebook/guobiao-four-jokers" : GuobiaoGameState.IsTransparentSubRule(subRule) ? "/rulebook/guobiao-transparent" : subRule == "guobiao/lanshi" ? "/rulebook/guobiao-lanshi" : "/rulebook/guobiao",
            DisplayName = "国标麻将",
            LobbyOrder = 0,
            LobbySubRules = GuobiaoLobby.SubRules,
            CreateRoomDefaults = GuobiaoLobby.Defaults(),
            HasRankedStats = true,
            ShowsHepaiLimitInRoomList = true,
            StatsFanNames = RankConfig.GuobiaoFanTranslation,
            GameStateFactory = () => new GuobiaoGameState(),
            NormalizeTileId = WashizuTile.Face,
            PrepareHandDisplay = info => GameCanvas.Instance.PrepareWashizuRealtimeHand(info),
            HandJokerTiles = sub => sub == GuobiaoGameState.HuahunSubRule ? new[] {51,52,53,54} : sub == GuobiaoGameState.FourJokersSubRule ? new[] {59} : System.Array.Empty<int>(),
            TileBadgeText = tile => RuleRegistry.CurrentSubRule == GuobiaoGameState.HuahunSubRule && HuahunRules.IsJoker(tile) ? "癞" : RuleRegistry.CurrentSubRule == GuobiaoGameState.FourJokersSubRule && tile == 59 ? "癞" : null,
            Tingpai = GuobiaoTips.Tingpai,
            DescribeWaitingTile = GuobiaoTips.Describe,
            ReplayConcealedKongMask = HuahunRules.ReplayKongMask,
            FanNameText = GuobiaoFanText.FanName,
            FanValueText = GuobiaoFanText.FanValue,
            RoundName = round => GameSession.Current.SubRule == GuobiaoGameState.SanmaSubRule
                ? RoundTextDictionary.ThreePlayerWindSeatRoundName(round) : RoundTextDictionary.WindSeatRoundName(round),
            PlaysGongHuSound = GuobiaoFanText.PlaysGongHu,
            SettlementFootnote = GuobiaoAngangCheck.Footnote,
            // 国标自摸也叫"和"，报声同
            ActionCaption = word => word == "hu_self" ? "和" : null,
            ActionVoice = word => word == "hu_self" || word == "hu_flower" ? "hu" : null,
            RonWinTileTravelsFromRiver = true,
            SupportsRobbedAddedKongSource = true,
            MidGameCuoheContinues = true,
            PreserveDragOnDraw = true,
        });
    }
}
