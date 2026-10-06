using UnityEngine;

internal static class ZhongyongRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        var defaults = JiandanLobby.Defaults();
        defaults[CreateRoomKeys.SubRule] = 0;
        RuleRegistry.Register(new RuleManifest {
            RuleId = ZhongyongGameState.RuleId,
            DefaultSubRule = "zhongyong/standard",
            DisplayName = "中庸麻将", LobbyName = "中庸麻将", LobbyOrder = 8,
            LobbySubRules = new[] {
                new RuleLobbySubRule("zhongyong/standard", "标准规", "采用关兆豪的中庸 v3.3：136张牌，无起和限制，44种和种按系列加算，头跳，每局轮庄，保留14张王牌。"),
                new RuleLobbySubRule("zhongyong/nanque", "南雀", "南雀规则由南瓜饼编写，采用简化的中庸计分法，无起和限制。默认血战到底，和牌者退场，三家和牌或牌墙耗尽后统一结算。"),
            },
            CreateRoomDefaults = defaults,
            GameStateFactory = () => new ZhongyongGameState(),
            SubRuleGameStateFactory = rule => ZhongyongFanText.IsNanque(rule) ? (IGameState)new NanqueGameState() : new ZhongyongGameState(),
            OutboundChannel = "zhongyong",
            Tingpai = q => ZhongyongFanText.IsNanque(q.SubRule) ? JiandanTips.Tingpai(q)
                : new Jiandan.JiandanTingpaiCheck().TingpaiCheck(q.Hand, q.Melds, allowQuadSplit: true),
            DescribeWaitingTile = q => ZhongyongFanText.IsNanque(q.SubRule) ? JiandanTips.Describe(q) : WaitTileHint.Ron("可和"),
            FanNameText = ZhongyongFanText.Name,
            FanValueText = ZhongyongFanText.Value,
            RoundName = RoundTextDictionary.WindNumberRoundName,
            MaxRoundText = rounds => $"{rounds * 4}局",
            ScoreboardFanText = q => $"{q.HuScore}{(ZhongyongFanText.IsNanque(q.Rule) ? "番" : "分")}",
            SettlementTotal = q => new SettlementTotalDisplay {
                FanText = ZhongyongFanText.IsNanque(q.Rule) ? "番值" : null,
                ScoreText = ZhongyongFanText.IsNanque(q.Rule) ? $"{q.HuScore}番" : $"共{q.HuScore}分",
            },
            HasFlowerReplacement = false,
            SupportsRobbedAddedKongSource = true,
            ReplacementFromTailEnd = true,
            DefaultHepaiLimit = 0,
            RulebookKey = "zhongyong",
        });
    }
}
