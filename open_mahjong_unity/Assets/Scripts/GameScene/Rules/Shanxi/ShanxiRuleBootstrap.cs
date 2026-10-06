using System.Collections.Generic;
using UnityEngine;

internal static class ShanxiRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = "shanxi", DefaultSubRule = "shanxi/mil2023", DisplayName = "山西麻将", LobbyOrder = 11,
            LobbySubRules = new[] { new RuleLobbySubRule("shanxi/mil2023", "MIL 2023",
                "MIL《山西麻将（推广）竞赛规则（试行2023版）》：136张、十三张手牌，不吃牌、无花无癞子；暗扣一张牌报听，三点可自摸、六点可点和。和牌时结算全桌杠账，未报听放铳须包全桌。线上使用房间配置，默认每局额外思考局时20秒、每次步时5秒，先用步时再扣局时；原书线下3秒报牌时限不另设线上上限。零局时只用步时，重连不重开窗口。") },
            CreateRoomDefaults = new Dictionary<string, object> {
                { CreateRoomKeys.SubRule, 0 }, { CreateRoomKeys.GameRound, 4 },
                { CreateRoomKeys.RoundTimer, 3 }, { CreateRoomKeys.StepTimer, 1 },
                { CreateRoomKeys.Tips, true }, { CreateRoomKeys.CountTips, false }, { CreateRoomKeys.PointerTips, true },
                { CreateRoomKeys.Password, false }, { CreateRoomKeys.RandomSeed, false },
                { CreateRoomKeys.TouristLimit, false }, { CreateRoomKeys.AllowSpectator, true },
            },
            GameStateFactory = () => new ShanxiGameState(),
            Tingpai = q => ShanxiHandCalculator.Waits(q.Hand, q.Melds),
            DescribeWaitingTile = ShanxiHandCalculator.Describe,
            RoundName = r => $"第{r}庄", RoundStatusText = q => $"第{q.current_round}庄",
            MaxRoundText = r => $"{r * 4}庄",
            FanValueText = FanValue,
            ScoreboardFanText = q => $"基本分 {q.HuScore}分",
            SettlementTotal = q => new SettlementTotalDisplay { FanText = "和牌基本分", ScoreText = $"{q.HuScore}分" },
            ActionCaption = word => word == "riichi" || word == "riichi_cut" ? "报听"
                : word == "riichi_cut_cancel" ? "取消报听" : null,
            ActionVoice = word => word == "riichi" || word == "riichi_cut" ? "baoting" : null,
            HasFlowerReplacement = false,
            RonWinTileTravelsFromRiver = true,
            PeekAnkan = true, PublicReadyStateReplay = true, DefaultHepaiLimit = 0,
        });
    }
    private static string FanValue(string rule, string name) {
        switch (name) {
            case "一条龙": case "清一色": case "七对": return "20分";
            case "豪华七对": return "40分";
            case "十三幺": return "60分";
            case "平和": return "0分";
            default: return "";
        }
    }
}
