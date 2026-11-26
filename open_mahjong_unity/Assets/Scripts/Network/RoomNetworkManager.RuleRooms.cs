using System;
using Newtonsoft.Json;
using UnityEngine;

public partial class RoomNetworkManager {
    private async void SendCreateRoomRequest(string rawSeed, string ruleLabel, Func<string, object> buildRequest) {
        if (!TryBeginCreateRequest()) return;
        try {
            if (!TryResolveRandomSeed(rawSeed, out string seed, out string error)) {
                CancelPendingRoomEntry();
                NotificationManager.Instance.ShowTip("create_room", false, error);
                return;
            }
            var request = buildRequest(seed);
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            CancelPendingRoomEntry();
            Debug.LogError($"创建{ruleLabel}房间失败: {e.Message}");
        }
    }

    private static string OptionalEventId(string eventId) => string.IsNullOrEmpty(eventId) ? null : eventId;

    private static object BuildSimpleRuleRequest(
        string type, string rule, string subRule, Qingque_Create_RoomConfig config, string seed) => new {
        type, rule, sub_rule = subRule,
        roomname = config.RoomName, gameround = config.GameRound, password = config.Password, random_seed = seed,
        roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
        count_tips = config.CountTips, pointer_tips = config.PointerTips, tourist_limit = config.TouristLimit,
        allow_spectator = config.AllowSpectator, event_id = OptionalEventId(config.EventId),
    };

    private static object BuildDetailedRuleRequest(
        string type, string rule, string subRule, Qingque_Create_RoomConfig config, string seed, object detailedConfig) => new {
        type, rule, sub_rule = subRule,
        roomname = config.RoomName, gameround = config.GameRound, password = config.Password, random_seed = seed,
        roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
        count_tips = config.CountTips, pointer_tips = config.PointerTips, tourist_limit = config.TouristLimit,
        allow_spectator = config.AllowSpectator, event_id = OptionalEventId(config.EventId),
        detailed_config = detailedConfig,
    };

    private static object BuildTacticalDetailedRuleRequest(
        string type, string rule, string subRule, Qingque_Create_RoomConfig config, string seed, object detailedConfig) => new {
        type, rule, sub_rule = subRule,
        roomname = config.RoomName, gameround = config.GameRound, password = config.Password, random_seed = seed,
        roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
        tactical_call = config.TacticalCall, count_tips = config.CountTips, pointer_tips = config.PointerTips,
        tourist_limit = config.TouristLimit, allow_spectator = config.AllowSpectator,
        event_id = OptionalEventId(config.EventId), detailed_config = detailedConfig,
    };

    internal static object BuildBeijingRequest(Beijing_Create_RoomConfig config, string seed) => new {
        type = "room/create_Beijing_room", rule = BeijingGameState.RuleId, sub_rule = BeijingGameState.SubRule,
        roomname = config.RoomName, gameround = config.GameRound, password = config.Password, random_seed = seed,
        roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
        count_tips = config.CountTips, pointer_tips = config.PointerTips, tourist_limit = config.TouristLimit,
        allow_spectator = config.AllowSpectator, event_id = OptionalEventId(config.EventId),
        detailed_config = config.NormalizedConfig(),
    };

    internal static object BuildDuocaiRequest(Duocai_Create_RoomConfig config, string seed) => new {
        type = "room/create_Duocai_room", rule = DuocaiGameState.RuleId, sub_rule = config.SubRule,
        roomname = config.RoomName, gameround = config.GameRound, password = config.Password, random_seed = seed,
        roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
        count_tips = config.CountTips, pointer_tips = config.PointerTips, tourist_limit = config.TouristLimit,
        allow_spectator = config.AllowSpectator, event_id = OptionalEventId(config.EventId),
        detailed_config = config.NormalizedConfig(),
    };

    internal static object BuildNanchangRequest(Qingque_Create_RoomConfig config, string seed) =>
        BuildDetailedRuleRequest("room/create_Nanchang_room", NanchangGameState.RuleId, NanchangGameState.SubRule,
            config, seed, new { rule_version = NanchangGameState.RuleVersion });

    internal static object BuildNewChineseRequest(NewChinese_Create_RoomConfig config, string seed) => new {
        type = "room/create_NewChinese_room", rule = NewChineseGameState.RuleId,
        sub_rule = config.SubRule ?? NewChineseGameState.SubRule,
        roomname = config.RoomName, gameround = config.MatchRounds, password = config.Password, random_seed = seed,
        hepai_limit = config.HepaiLimit,
        roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
        count_tips = config.CountTips, pointer_tips = config.PointerTips, tourist_limit = config.TouristLimit,
        allow_spectator = config.AllowSpectator, event_id = OptionalEventId(config.EventId),
        detailed_config = config.NormalizedConfig(),
    };

    internal static object BuildShangraoRequest(Shangrao_Create_RoomConfig config, string seed) => new {
        type = "room/create_Shangrao_room", rule = ShangraoGameState.RuleId, sub_rule = ShangraoGameState.SubRule,
        roomname = config.RoomName, gameround = config.GameRound, password = config.Password, random_seed = seed,
        roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
        count_tips = config.CountTips, pointer_tips = config.PointerTips, tourist_limit = config.TouristLimit,
        allow_spectator = config.AllowSpectator, event_id = OptionalEventId(config.EventId),
        detailed_config = config.NormalizedConfig(),
    };

    internal static object BuildWenzhouRequest(Qingque_Create_RoomConfig config, string seed) =>
        BuildDetailedRuleRequest("room/create_Wenzhou_room", WenzhouGameState.RuleId, WenzhouGameState.SubRule,
            config, seed, new { edition = WenzhouGameState.RuleVersion });

    public void Create_Beijing_Room(Beijing_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "北京麻将", seed => BuildBeijingRequest(config, seed));

    public void Create_Changchun_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "长春", seed => new CreateGBRoomRequest {
            type = "room/create_Changchun_room", rule = "changchun", sub_rule = "changchun/mil2024",
            roomname = config.RoomName, gameround = config.GameRound, roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer,
            tips = config.Tips, count_tips = config.CountTips, pointer_tips = config.PointerTips, password = config.Password, random_seed = seed,
            use_flowers = false, claim_protection = false, open_cuohe = false, hepai_limit = 0, tactical_call = config.TacticalCall,
            tourist_limit = config.TouristLimit, allow_spectator = config.AllowSpectator, event_id = OptionalEventId(config.EventId),
        });

    public void Create_Duocai_Room(Duocai_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "夺彩麻将", seed => BuildDuocaiRequest(config, seed));

    public void Create_Fuzhou_Room(FuzhouCreateRoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "福州麻将", seed => new {
            type = "room/create_Fuzhou_room", rule = FuzhouGameState.RuleId, sub_rule = FuzhouGameState.SubRule,
            roomname = config.RoomName, gameround = config.GameRound, password = config.Password, random_seed = seed,
            roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
            count_tips = config.CountTips, pointer_tips = config.PointerTips, tourist_limit = config.TouristLimit,
            allow_spectator = config.AllowSpectator, event_id = OptionalEventId(config.EventId),
            detailed_config = new { rule_version = FuzhouGameState.RuleVersion, base_score = config.BaseScore },
        });

    public void Create_Guangdong_Room(Qingque_Create_RoomConfig config, bool minimumScore, bool pingjiZimoNoChow = false) =>
        SendCreateRoomRequest(config.RandomSeed, "广东麻将", seed => new {
            type = "room/create_Guangdong_room", rule = "guangdong", sub_rule = config.SubRule,
            roomname = config.RoomName, gameround = config.GameRound, roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer,
            tips = config.Tips, count_tips = config.CountTips, pointer_tips = config.PointerTips, password = config.Password, random_seed = seed,
            open_cuohe = false, hepai_limit = GuangdongMilRules.IsMil(config.SubRule) ? 2 : 0, use_flowers = false, claim_protection = false,
            tactical_call = config.TacticalCall, tourist_limit = config.TouristLimit, allow_spectator = config.AllowSpectator,
            event_id = OptionalEventId(config.EventId),
            detailed_config = GuangdongMilRules.IsMil(config.SubRule)
                ? GuangdongMilRules.Detail(minimumScore, config.SubRule == GuangdongMilRules.Doubles)
                : TuidaoRuleBootstrap.Detail(pingjiZimoNoChow),
        });

    public void Create_Guizhou_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "贵州麻将", seed =>
            BuildTacticalDetailedRuleRequest("room/create_Guizhou_room", GuizhouGameState.RuleId, GuizhouGameState.SubRule,
                config, seed, new { rule_version = GuizhouGameState.RuleVersion }));

    public void Create_Hangzhou_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "杭州麻将", seed =>
            BuildTacticalDetailedRuleRequest("room/create_Hangzhou_room", HangzhouGameState.RuleId, HangzhouGameState.SubRule,
                config, seed, new { rule_version = HangzhouGameState.RuleVersion }));

    public void Create_Hongzhong_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "红中麻将", seed =>
            BuildSimpleRuleRequest("room/create_Hongzhong_room", HongzhongGameState.RuleId, HongzhongGameState.SubRule, config, seed));

    public void Create_Jiujiang_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "九江麻将", seed =>
            BuildSimpleRuleRequest("room/create_Jiujiang_room", JiujiangGameState.RuleId, JiujiangGameState.SubRule, config, seed));

    public void Create_Malaysia_Room(Qingque_Create_RoomConfig config, bool multipleMax = false) =>
        SendCreateRoomRequest(config.RandomSeed, "马来西亚三人麻将", seed =>
            BuildDetailedRuleRequest("room/create_Malaysia_room", MalaysiaGameState.RuleId, MalaysiaGameState.SubRule,
                config, seed, new { multiple_max = multipleMax }));

    public void Create_Nanchang_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "南昌麻将", seed => BuildNanchangRequest(config, seed));

    public void Create_Nanjing_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "南京麻将", seed =>
            BuildDetailedRuleRequest("room/create_Nanjing_room", NanjingGameState.RuleId, NanjingGameState.SubRule,
                config, seed, new { rule_version = NanjingGameState.RuleVersion }));

    public void Create_NewChinese_Room(NewChinese_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "新中式麻将", seed => BuildNewChineseRequest(config, seed));

    public void Create_Shangrao_Room(Shangrao_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "上饶麻将", seed => BuildShangraoRequest(config, seed));

    public void Create_Shenyang_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "沈阳", seed => new CreateGBRoomRequest {
            type = "room/create_Shenyang_room", rule = "shenyang", sub_rule = "shenyang/mil2025",
            roomname = config.RoomName, gameround = config.GameRound, roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer,
            tips = config.Tips, count_tips = config.CountTips, pointer_tips = config.PointerTips, password = config.Password, random_seed = seed,
            use_flowers = false, claim_protection = false, open_cuohe = false, hepai_limit = 0, tactical_call = false,
            tourist_limit = config.TouristLimit, allow_spectator = config.AllowSpectator, event_id = OptionalEventId(config.EventId),
        });

    public void Create_Singapore_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "新加坡麻将", seed =>
            BuildSimpleRuleRequest("room/create_Singapore_room", SingaporeGameState.RuleId, SingaporeGameState.SubRule, config, seed));

    public void Create_Wenzhou_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "温州麻将", seed => BuildWenzhouRequest(config, seed));

    public void Create_Wuhan_Room(Qingque_Create_RoomConfig config) =>
        SendCreateRoomRequest(config.RandomSeed, "武汉麻将", seed =>
            BuildSimpleRuleRequest("room/create_Wuhan_room", WuhanGameState.RuleId, WuhanGameState.SubRule, config, seed));

    public void Create_Yixing_Room(Qingque_Create_RoomConfig config, bool sevenPairs) =>
        SendCreateRoomRequest(config.RandomSeed, "宜兴麻将", seed =>
            BuildDetailedRuleRequest("room/create_Yixing_room", YixingGameState.RuleId, YixingGameState.SubRule,
                config, seed, new { seven_pairs = sevenPairs }));
}
