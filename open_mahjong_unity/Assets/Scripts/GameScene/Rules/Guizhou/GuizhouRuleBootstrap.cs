using System.Collections.Generic;
using UnityEngine;

internal static class GuizhouRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleNameDictionary.WholeName[GuizhouGameState.RuleId] = "贵州麻将";
        RuleNameDictionary.WholeName[GuizhouGameState.SubRule] = "贵州麻将（MIL）";
        RuleNameDictionary.ShortName[GuizhouGameState.RuleId] = "贵州";
        RuleNameDictionary.ShortName[GuizhouGameState.SubRule] = "贵州";
        RuleRegistry.Register(new RuleManifest {
            RuleId = GuizhouGameState.RuleId, DefaultSubRule = GuizhouGameState.SubRule,
            DisplayName = "贵州麻将", LobbyName = "贵州麻将", LobbyOrder = 9,
            LobbySubRules = new[] { new RuleLobbySubRule(GuizhouGameState.SubRule, "MIL 2023",
                "MIL《贵州麻将（推广）竞赛规则（试行2023版）》：108张、十三张手牌，无花无癞子，不吃；开局原报或软报，鸡杠局终结算，基本分39分封顶，流局查叫。") },
            CreateRoomDefaults = new Dictionary<string, object> {
                { CreateRoomKeys.GameRound, 4 }, { CreateRoomKeys.RoundTimer, 3 }, { CreateRoomKeys.StepTimer, 1 },
                { CreateRoomKeys.TacticalCall, true }, { CreateRoomKeys.Tips, true }, { CreateRoomKeys.CountTips, false }, { CreateRoomKeys.PointerTips, true },
                { CreateRoomKeys.Password, false }, { CreateRoomKeys.RandomSeed, false },
                { CreateRoomKeys.TouristLimit, false }, { CreateRoomKeys.AllowSpectator, true },
            },
            GameStateFactory = () => new GuizhouGameState(), OutboundChannel = "guizhou",
            Tingpai = GuizhouShape.Waiting,
            DescribeWaitingTile = GuizhouTips.Describe,
            RecordDangerUsesWaitHint = true,
            FanNameText = (rule, label) => GuizhouFanText.Name(label),
            FanValueText = (rule, label) => GuizhouFanText.Value(label),
            SettlementTotal = q => new SettlementTotalDisplay {
                FanText = $"基本分 {q.HuScore}分",
                ScoreText = q.WinnerPointDelta.HasValue ? $"{q.WinnerPointDelta.Value:+#;-#;0}分" : "",
            },
            ScoreboardFanText = q => $"基本分 {q.HuScore}分",
            SettlementFootnote = q => "鸡杠另计；基本分上限39分。",
            RoundName = round => $"第{round}局", MaxRoundText = rounds => $"{rounds * 4}局",
            RoundStatusText = info => $"第{info.current_round}局", RoundSupplementText = info => "捉鸡",
            DefaultHepaiLimit = 0, HasFlowerReplacement = false,
            SupportsRobbedAddedKongSource = true, PublicReadyStateReplay = true,
            // Leave ReplacementFromTailEnd false: MIL takes the upper tile of
            // the final stack, then its lower tile, like the common MCR path.
            ReplayConcealedKongMask = (rule, tiles) => GameRecordManager.GuizhouReplayKongMask(tiles),
            ActionCaption = word => word == "riichi" ? "报听" : null,
            // MIL 2023三-22/23：声明天听（原报/软报），口头报“天听”。
            ActionVoice = word => word == "riichi" ? "tianting" : word == "hu_self" ? "hu" : null,
            RulebookKey = "guizhou",
        });
        ActionWords.Register(new ActionWordSpec {
            Word = "baoting_initial", Kind = ActionWordKind.Other, Label = _ => "原报",
        });
        ActionWords.Register(new ActionWordSpec {
            Word = "guizhou_ready", Kind = ActionWordKind.Other, Label = _ => "报听",
        });
        ActionWords.Register(new ActionWordSpec {
            Word = "guizhou_ready_cancel", Kind = ActionWordKind.Other, DisplayPriority = 60, Label = _ => "取消报听",
        });
    }
}

internal static class GuizhouFanText {
    public static string Name(string label) {
        string[] fields = label?.Split('|');
        return fields?.Length >= 4 && fields[0] == "GZ" ? fields[3] : label;
    }
    public static string Value(string label) {
        string[] fields = label?.Split('|');
        return fields?.Length >= 4 && fields[0] == "GZ" ? fields[2] + "分" : "";
    }
}
