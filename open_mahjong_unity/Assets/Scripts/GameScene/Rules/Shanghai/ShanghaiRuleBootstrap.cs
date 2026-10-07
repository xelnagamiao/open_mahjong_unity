using System.Collections.Generic;
using UnityEngine;

internal static class ShanghaiRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = "shanghai",
            DefaultSubRule = "shanghai/qiaoma",
            DisplayName = "上海麻将",
            LobbyOrder = 7,
            LobbySubRules = new[] {
                new RuleLobbySubRule("shanghai/qiaoma", "上海敲麻", "上海敲麻：上海特色麻将规则，使用144张麻将牌，有着中发白当花、可以垃圾和、听牌后要敲牌报听、番种简单等特点，节奏快且易上手。"),
                new RuleLobbySubRule("shanghai/qinghunpeng", "清混碰", "上海清混碰：上海传统麻将规则，使用144张麻将牌，以必须做出清、混一色或碰碰和才能和牌为特色，与快节奏的上海敲麻有着鲜明对比，独具特色。"),
            },
            CreateRoomDefaults = new Dictionary<string, object> {
                { CreateRoomKeys.SubRule, 0 },
                { CreateRoomKeys.HepaiLimit, 0 },
                { CreateRoomKeys.GameRound, 4 },
                { CreateRoomKeys.RoundTimer, 3 },
                { CreateRoomKeys.StepTimer, 1 },
                { CreateRoomKeys.Tips, true },
                { CreateRoomKeys.CountTips, false },
                { CreateRoomKeys.PointerTips, true },
                { CreateRoomKeys.Password, false },
                { CreateRoomKeys.RandomSeed, false },
                { CreateRoomKeys.TouristLimit, false },
                { CreateRoomKeys.AllowSpectator, true },
            },
            GameStateFactory = () => new ShanghaiGameState(),
            SubRuleGameStateFactory = subRule => subRule == "shanghai/qinghunpeng"
                ? (IGameState)new QinghunpengGameState() : new ShanghaiGameState(),
            Tingpai = query => query.SubRule == "shanghai/qinghunpeng"
                ? QinghunpengHandCalculator.Waits(query.Hand, query.Melds)
                : ShanghaiHandCalculator.Waits(query.Hand, query.Melds),
            DescribeWaitingTile = query => query.SubRule == "shanghai/qinghunpeng"
                ? QinghunpengHandCalculator.Describe(query) : ShanghaiHandCalculator.Describe(query),
            RecordDangerUsesWaitHint = true,
            RecordDangerQualification = query => query.SubRule == "shanghai/qinghunpeng"
                || query.Record?.SelfTags?.Contains("declared_ready") == true,
            RoundName = round => $"第{round}局",
            RoundStatusText = RoundStatus,
            RoundSupplementText = RoundSupplement,
            MaxRoundText = rounds => $"{rounds * 4}局",
            FanNameText = FanName,
            FanValueText = FanValue,
            ScoreboardFanText = query => $"{query.HuScore}分",
            SettlementTotal = query => new SettlementTotalDisplay { FanText = query.Rule == "shanghai/qinghunpeng" ? "和牌点数" : "基本分", ScoreText = $"{query.HuScore}分" },
            ActionCaption = ActionCaption,
            ActionVoice = ActionVoice,
            PeekAnkan = true,
            SupportsRobbedAddedKongSource = true,
            ReplacementFromTailEnd = true,
            PublicReadyStateReplay = true,
            DefaultHepaiLimit = 0,
        });
    }

    private static string RoundStatus(GameInfo info) {
        string round = info.sub_rule == "shanghai/qinghunpeng" && info.current_round > info.max_round * 4
            ? $"加时{info.current_round - info.max_round * 4}" : $"第{info.current_round}局";
        return round;
    }

    private static string RoundSupplement(GameInfo info) {
        if (info.sub_rule != "shanghai/qinghunpeng") return null;
        int debt = info.detailed_config != null && info.detailed_config.TryGetValue("huangfan_count", out object value)
            ? System.Convert.ToInt32(value) : 0;
        return debt > 0 ? $"荒番:{debt}" : "荒番:无";
    }

    private static string ActionCaption(string word) {
        switch (word) {
            case "riichi":
            case "riichi_cut": return "敲牌";
            case "riichi_cut_cancel": return "取消敲牌";
            default: return null;
        }
    }

    private static string ActionVoice(string word) {
        if (!IsQiaomaActionContext()) return null;
        // 敲麻MIL 2024六-2：听、花、和，清混碰保留自己的报声。
        switch (word) {
            case "riichi": case "riichi_cut": return "ting";
            case "buhua": return "hua";
            case "hu_self": return "hu";
            default: return null;
        }
    }

    private static bool IsQiaomaActionContext() {
        // 牌谱和观战使用自己的规则上下文；旧上海牌谱缺少子规则时沿用默认敲麻。
        GameRecordManager.ResolveActionRuleContext(null, null, out string roomRule, out string subRule);
        return subRule == "shanghai/qiaoma" || (string.IsNullOrEmpty(subRule) && roomRule == "shanghai");
    }

    private static bool TryFlowerValue(string name, out string label, out string value) {
        label = name;
        value = null;
        if (string.IsNullOrEmpty(name) || !name.EndsWith("花）")) return false;
        int start = name.IndexOf('（');
        if (start <= 0) return false;
        string number = name.Substring(start + 1, name.Length - start - 3);
        if (!int.TryParse(number, out int flowers) || flowers < 0) return false;
        label = name.Substring(0, start);
        value = $"{flowers}花";
        return true;
    }

    private static string FanName(string rule, string name) {
        if (TryFlowerValue(name, out string label, out _)) return label;
        if (rule == "shanghai/qinghunpeng") {
            switch (name) {
                case "清一色": case "混碰": case "大吊车": case "杠头开花":
                case "海底捞月": case "抢杠": return $"{name}（勒子）";
                case "清碰": case "乱风向": return $"{name}（双勒子）";
                case "全风向": return $"{name}（四勒子）";
            }
        }
        return name;
    }

    private static string FanValue(string rule, string name) {
        if (TryFlowerValue(name, out _, out string value)) return value;
        if (rule == "shanghai/qinghunpeng") {
            switch (name) {
                case "混一色": case "碰碰和": return "1花";
                case "清一色": case "混碰": case "大吊车": case "杠头开花":
                case "海底捞月": case "抢杠": return "10花";
                case "清碰": case "乱风向": return "20花";
                case "全风向": return "40花";
                default: return "";
            }
        }
        switch (name) {
            case "清一色": return "2番";
            case "门清": case "大吊车": case "杠开": case "抢杠":
            case "混一色": case "碰碰和": return "1番";
            default: return "";
        }
    }
}
