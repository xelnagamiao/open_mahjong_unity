using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class HangzhouRuleBootstrap {
    public const string Description = "MIL《杭州麻将（推广）竞赛规则（试行2025版）》：136张、十三张手牌，白板为财神，仅自摸。爆头、财飘、七对、十风，4番封顶，老庄2/4/8倍，三吃承包，墙尾20张流局。";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Register() {
        RuleNameDictionary.WholeName[HangzhouGameState.RuleId] = "杭州麻将";
        RuleNameDictionary.WholeName[HangzhouGameState.SubRule] = "MIL 杭州（2025）";
        RuleNameDictionary.ShortName[HangzhouGameState.RuleId] = "杭州";
        RuleNameDictionary.ShortName[HangzhouGameState.SubRule] = "杭州";
        RuleRegistry.Register(new RuleManifest {
            RuleId = HangzhouGameState.RuleId, DefaultSubRule = HangzhouGameState.SubRule,
            DisplayName = "杭州麻将", LobbyName = "杭州麻将", LobbyOrder = 12,
            LobbySubRules = new[] { new RuleLobbySubRule(HangzhouGameState.SubRule, "MIL 2025", Description) },
            CreateRoomDefaults = new Dictionary<string,object> {
                { CreateRoomKeys.SubRule, 0 }, { CreateRoomKeys.GameRound, 4 }, { CreateRoomKeys.RoundTimer, 3 }, { CreateRoomKeys.StepTimer, 1 },
                { CreateRoomKeys.Tips, true }, { CreateRoomKeys.CountTips, false }, { CreateRoomKeys.PointerTips, true },
                { CreateRoomKeys.Password, false }, { CreateRoomKeys.RandomSeed, false }, { CreateRoomKeys.TouristLimit, false }, { CreateRoomKeys.AllowSpectator, true },
            },
            GameStateFactory = () => new HangzhouGameState(), OutboundChannel = "hangzhou",
            Tingpai = query => GameRecordManager.Instance?.ShouldShowRecordTips() == true
                ? GameRecordManager.Instance.HangzhouWaiting(query) : HangzhouGameState.Active?.Hints.Waiting(query) ?? new HashSet<int>(),
            DescribeWaitingTile = query => WaitTileHint.TsumoOnly("仅自摸"),
            TileBadgeText = tile => tile == HangzhouGameState.Joker ? "财" : null,
            FanNameText = (rule,label) => HangzhouFanText.Name(label), FanValueText = (rule,label) => HangzhouFanText.Value(label),
            SettlementTotal = query => new SettlementTotalDisplay { FanText = $"基本分 {query.HuScore}分", ScoreText = query.WinnerPointDelta.HasValue ? $"{query.WinnerPointDelta.Value:+#;-#;0}分" : "" },
            ScoreboardFanText = query => $"基本分 {query.HuScore}分",
            SettlementFootnote = query => query.HuFan != null && query.HuFan.Length > 0
                ? "4番封顶；庄家2/4/8倍\n三吃承包依责任支付。" : null,
            SettlementHasNoWinTile = HangzhouFanText.IsTenWinds,
            RecordFlowerWin = tick => tick != null && tick.Count > 3 && tick[3].Contains("十风"),
            HuPresentationAction = (kind,fans,config) => HangzhouFanText.IsTenWinds(fans) ? "hangzhou_ten_winds" : kind,
            ActionCaption = word => word == "hangzhou_piao" ? "飘" : word == "hangzhou_ten_winds" || (word == "hu_self" && HangzhouGameState.Active?.Info?.phase == "waiting_hangzhou_ten_winds") ? "十风和" : null,
            ActionVoice = word => word == "hangzhou_ten_winds" ? "hu" : word == "hangzhou_piao" ? "piao" : null,
            RoundName = round => $"第{round}局", MaxRoundText = rounds => $"{rounds * 4}局", RoundStatusText = info => $"第{info.current_round}局",
            RoundSupplementText = info => "4番封顶",
            DefaultHepaiLimit = 0, HasFlowerReplacement = false, ReplacementFromTailEnd = true,
            ShowsRonDangerHints = false,
            ReplayConcealedKongMask = (rule,tiles) => new[] { 2,tiles[0],2,tiles[1],2,tiles[2],2,tiles[3] },
            RulebookKey = "hangzhou",
        });
        ActionWords.Register(new ActionWordSpec { Word = "hangzhou_piao", Kind = ActionWordKind.Other, Label = _ => "飘" });
        ActionWords.Register(new ActionWordSpec { Word = "hangzhou_tail_burn", Kind = ActionWordKind.Other, Label = _ => "" });
        ActionWords.Register(new ActionWordSpec { Word = "hangzhou_ten_winds", Kind = ActionWordKind.Tsumo, Label = _ => "十风和" });
    }
}
internal static class HangzhouFanText {
    public static string Name(string label) { var f=label?.Split('|'); return f?.Length>=4 && f[0]=="HZ" ? f[3] : label; }
    public static string Value(string label) { var f=label?.Split('|'); return f?.Length>=4 && f[0]=="HZ" ? f[2]+"番" : ""; }
    public static bool IsTenWinds(IList<string> fans) => fans != null && fans.Any(f => Name(f)=="十风");
}
