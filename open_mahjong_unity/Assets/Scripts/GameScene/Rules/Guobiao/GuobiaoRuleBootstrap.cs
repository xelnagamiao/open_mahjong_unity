using UnityEngine;

internal static class GuobiaoRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            ReplayFactory = record => record.CreateGuobiaoReplayModule(),
            CreateSettlementCodec = () => new SichuanRecordSettlementCodec(isSichuanFamily: false),
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
            OpeningHandHasDrawSlot = info => GuobiaoGameState.OpeningSelfPlayer(info)?.has_draw_slot == true,
            HandJokerTiles = sub => sub == GuobiaoGameState.HuahunSubRule ? new[] {51,52,53,54} : sub == GuobiaoGameState.FourJokersSubRule ? new[] {59} : System.Array.Empty<int>(),
            TileBadgeText = tile => RuleRegistry.CurrentSubRule == GuobiaoGameState.HuahunSubRule && HuahunRules.IsJoker(tile) ? "癞" : RuleRegistry.CurrentSubRule == GuobiaoGameState.FourJokersSubRule && tile == 59 ? "癞" : null,
            Tingpai = GuobiaoTips.Tingpai,
            DescribeWaitingTile = GuobiaoTips.Describe,
            FullHandTileCount = sub => sub == GuobiaoGameState.SixteenSubRule ? 17 : 14,
            DeferWaitingTileHints = true,
            DeferInvisibleHintCells = GuobiaoGameState.UsesAsyncLocalTips,
            TryDescribeWaitingTileCached = q => GuobiaoTips.TryDescribeCached(q, out WaitTileHint hint) ? hint : null,
            DescribeWaitingTileAsync = (q, cb) => GuobiaoGameState.UsesAsyncLocalTips(q.SubRule) ? FourJokersTips.Resolve(q, cb) : null,
            TileCopiesForSubRule = (sub, tile) => sub == GuobiaoGameState.HuahunSubRule && HuahunRules.IsJoker(tile) ? 1 : 4,
            LiveVisibleIndicators = () => GuobiaoGameState.Active != null ? GuobiaoGameState.Active.PublicOpponentHandTiles() : System.Array.Empty<int>(),
            CollectLivePlayerVisibleTiles = (p, isSelf, add) => {
                if (!WashizuTile.UsesTransparentHands(GameSession.Current.SubRule) || p.combination_masks == null) return false;
                foreach (var mask in p.combination_masks)
                    foreach (int tile in WashizuTile.VisibleMeldTiles(mask)) add(tile);
                return true;
            },
            ReplayConcealedKongMask = HuahunRules.ReplayKongMask,
            FanNameText = GuobiaoFanText.FanName,
            FanValueText = GuobiaoFanText.FanValue,
            RoundName = round => GameSession.Current.SubRule == GuobiaoGameState.SanmaSubRule
                ? RoundTextDictionary.ThreePlayerWindSeatRoundName(round) : RoundTextDictionary.WindSeatRoundName(round),
            PlaysGongHuSound = GuobiaoFanText.PlaysGongHu,
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
