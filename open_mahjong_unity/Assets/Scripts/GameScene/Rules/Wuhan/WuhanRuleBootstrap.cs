using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class WuhanRuleBootstrap {
    public const string Description = "MIL《武汉麻将（推广）竞赛规则（试行2025版）》：136张，开牌后翻癞子，皮子与癞子可单张杠出并选择补牌或过。必须开口，普通和牌须258将及实体胚子；两张及以上癞子只能大和。大和首项3番、叠加每项1番。逐家6番起和、10番封顶，全满为金顶。末四张只能自摸或过；七皮、十杠途中结算，轮庄。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Register() {
        RuleNameDictionary.WholeName[WuhanGameState.RuleId] = "武汉麻将";
        RuleNameDictionary.WholeName[WuhanGameState.SubRule] = "武汉麻将（MIL 2025）";
        RuleNameDictionary.ShortName[WuhanGameState.RuleId] = "武汉";
        RuleNameDictionary.ShortName[WuhanGameState.SubRule] = "武汉";
        RuleRegistry.Register(new RuleManifest {
            RuleId = WuhanGameState.RuleId, DefaultSubRule = WuhanGameState.SubRule,
            DisplayName = "武汉麻将", LobbyName = "武汉麻将", LobbyOrder = 23,
            LobbySubRules = new[] { new RuleLobbySubRule(WuhanGameState.SubRule, "MIL 2025", Description) },
            BuildRoom = WuhanRoomBuilder.Build, ReplayFactory = record => record.CreateWuhanReplayModule(),
            CreateRoomDefaults = new Dictionary<string, object> {
                {CreateRoomKeys.SubRule,0},{CreateRoomKeys.GameRound,4},{CreateRoomKeys.RoundTimer,3},{CreateRoomKeys.StepTimer,1},
                {CreateRoomKeys.Tips,true},{CreateRoomKeys.CountTips,false},{CreateRoomKeys.PointerTips,true},
                {CreateRoomKeys.Password,false},{CreateRoomKeys.RandomSeed,false},{CreateRoomKeys.TouristLimit,false},{CreateRoomKeys.AllowSpectator,true}
            },
            GameStateFactory = () => new WuhanGameState(), OutboundChannel = "wuhan",
            OpeningHandHasDrawSlot = info => info?.wuhan_info?.self_has_draw_slot == true,
            Tingpai = query => GameRecordManager.Instance?.ShouldShowRecordTips() == true
                ? GameRecordManager.Instance.WuhanWaiting(query) : WuhanGameState.Active?.Hints.Waiting(query) ?? new HashSet<int>(),
            DescribeWaitingTile = query => query.Record != null
                ? GameRecordManager.Instance?.DescribeWuhanWaiting(query) : WuhanGameState.Active?.DescribeWaiting(query),
            TileBadgeText = Badge, LiveVisibleIndicators = () => CurrentInfo?.indicator is int tile ? new[] { tile } : Array.Empty<int>(),
            VisibleMeldTiles = (code, self) => !self && code?.StartsWith("G") == true ? Array.Empty<int>() : null,
            FanValueText = (rule, name) => name == "风一色" ? "金顶" : name == "硬和" ? "1番" : "主3番\n叠1番",
            SettlementTotal = query => new SettlementTotalDisplay {
                FanText = "逐家6番起和", ScoreText = query.WinnerPointDelta.HasValue ? $"{query.WinnerPointDelta.Value}点" : "" },
            ScoreboardFanText = query => $"最低{query.HuScore}番",
            ActionCaption = word => word == "buhua" ? "杠" : null,
            ActionVoice = word => word == "buhua" ? "gang" : word == "hu_self" ? "hu" : null,
            RoundName = round => $"第{round}副", RoundStatusText = info => $"第{info.current_round}副", MaxRoundText = rounds => $"{rounds * 4}副",
            RoundSupplementText = info => "6番起和",
            HasFlowerReplacement = false, DefaultHepaiLimit = 6, FaceDownAnkan = true,
            ReplayConcealedKongMask = (rule, tiles) => tiles.SelectMany(tile => new[] {2, tile}).ToArray(),
            SupportsRobbedAddedKongSource = true, RonWinTileTravelsFromRiver = true, RecordDangerUsesWaitHint = true,
            RulebookKey = "wuhan"
        });
    }
    public static WuhanInfo CurrentInfo => GameRecordManager.Instance?.ShouldShowRecordTips() == true
        ? GameRecordManager.Instance.WuhanRecordInfo : WuhanGameState.Active?.Info;
    public static string Badge(int tile) => CurrentInfo?.joker == tile ? "癞" : (CurrentInfo?.skins ?? Array.Empty<int>()).Contains(tile) ? "皮" : null;
}
