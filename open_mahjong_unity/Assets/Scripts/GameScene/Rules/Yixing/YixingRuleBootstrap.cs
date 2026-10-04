using System.Collections.Generic;
using UnityEngine;

internal static class YixingRuleBootstrap {
    public const string Description = "宜兴麻将是江苏宜兴本地的特色玩法，由144张牌组成，其中万条筒各36张，东南西北中发白各4张，花牌8张，2花自摸，3花放冲，一花独吊，最先将手牌全部组成顺子和刻子的玩家赢得一局，起手花牌数能决定你当前牌局打法规划，牌局种类门清，碰碰胡，混一色，清一色等常见大牌，还包括独吊翻倍，杠开翻倍，海底翻倍，抢杠翻3倍等特殊机制，游戏尚在测试阶段，如对本规则感兴趣或有任何建议都可以添加Q541784531一同交流";
    public const string SevenPairsKey = "yixing_seven_pairs";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleNameDictionary.WholeName[YixingGameState.RuleId] = "宜兴麻将";
        RuleNameDictionary.WholeName[YixingGameState.SubRule] = "宜兴麻将";
        RuleNameDictionary.ShortName[YixingGameState.RuleId] = "宜兴";
        RuleNameDictionary.ShortName[YixingGameState.SubRule] = "宜兴";
        RuleRegistry.Register(new RuleManifest {
            RuleId = YixingGameState.RuleId, DefaultSubRule = YixingGameState.SubRule,
            DisplayName = "宜兴麻将", LobbyName = "宜兴麻将", LobbyOrder = 10,
            LobbySubRules = new[] { new RuleLobbySubRule(YixingGameState.SubRule, "标准规", Description) },
            CreateRoomDefaults = new Dictionary<string, object> {
                { CreateRoomKeys.GameRound, 4 }, { CreateRoomKeys.RoundTimer, 3 }, { CreateRoomKeys.StepTimer, 1 },
                { CreateRoomKeys.Tips, true }, { CreateRoomKeys.CountTips, false }, { CreateRoomKeys.PointerTips, true },
                { CreateRoomKeys.Password, false }, { CreateRoomKeys.RandomSeed, false },
                { CreateRoomKeys.TouristLimit, false }, { CreateRoomKeys.AllowSpectator, true },
                { SevenPairsKey, false },
            },
            GameStateFactory = () => new YixingGameState(), OutboundChannel = "yixing",
            Tingpai = q => YixingCalculator.Waiting(q.Hand, q.Melds, YixingCalculator.SevenPairs(q.DetailedConfig)),
            DescribeWaitingTile = YixingCalculator.Describe,
            FanNameText = (rule, label) => YixingFanText.Name(label),
            FanValueText = (rule, label) => YixingFanText.Value(label),
            SettlementTotal = q => new SettlementTotalDisplay {
                FanText = $"和牌点数 {q.HuScore}分",
                ScoreText = q.WinnerPointDelta.HasValue ? $"{q.WinnerPointDelta.Value:+#;-#;0}分" : "",
            },
            ScoreboardFanText = q => $"{q.HuScore}分",
            SettlementFootnote = q => "固定牌型已含胡牌底花；普通放冲一家支付，抢杠及海底放冲已计三倍。",
            RoundName = round => RoundTextDictionary.WindSeatRoundName(round),
            RoundSupplementText = info => info.yixing_info?.phase == "waiting_last_tile" ? "海底选择" : $"连庄{info.yixing_info?.dealer_streak ?? 0}次",
            DefaultHepaiLimit = 0, HasFlowerReplacement = true, ReplacementFromTailEnd = true,
            SupportsRobbedAddedKongSource = true,
            ReplayConcealedKongMask = (rule, tiles) => new[] {2,tiles[0],2,tiles[1],2,tiles[2],2,tiles[3]},
            RulebookKey = "yixing",
        });
        ActionWords.Register(new ActionWordSpec { Word = "yixing_last_draw", Kind = ActionWordKind.Other, Label = _ => "摸海底" });
        ActionWords.Register(new ActionWordSpec { Word = "yixing_last_choice", Kind = ActionWordKind.Other, Label = _ => "" });
    }
}
internal static class YixingFanText {
    public static string Name(string label) {
        string[] fields = label?.Split('|');
        return fields?.Length >= 4 && fields[0] == "YX" ? fields[3] : label;
    }
    public static string Value(string label) {
        string[] fields = label?.Split('|');
        return fields?.Length >= 4 && fields[0] == "YX" ? fields[2] + (fields[2].StartsWith("×") ? "" : "花") : "";
    }
}
