using System.Collections.Generic;

internal static class HongqueLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("hongque/v1.6", "虹雀²", "虹雀是由Null设计的一款以彩虹为主题的拉密类桌游，使用十四种花色、九种数字各一张的麻将牌，最先将手牌全部组成顺子或刻子的玩家赢得一局。牌组的种类千变万化，各种起手都存在无限的可能。游戏尚在测试阶段，如对本规则感兴趣或有任何建议都可以添加虹雀官方Q群389832188一同交流。"),
            new RuleLobbySubRule(HongqueWScoring.SubRule, "虹雀 W", "虹雀W第1.0版使用7种花色、9种数字各2张的虹雀牌，最先将手牌全部组成牌组的玩家赢得一局。彩虹顺子的存在给游戏带来了很多变数，是入门虹雀的最佳选择。"),
        };

    public static Dictionary<string, object> Defaults() {
        return new Dictionary<string, object> {
            { CreateRoomKeys.SubRule, 0 },
            { CreateRoomKeys.GameRound, 1 },
            { CreateRoomKeys.RoundTimer, 3 },
            { CreateRoomKeys.StepTimer, 1 },
            { CreateRoomKeys.Tips, true },
            { CreateRoomKeys.CountTips, false },
            { CreateRoomKeys.PointerTips, true },
            { CreateRoomKeys.Password, false },
            { CreateRoomKeys.RandomSeed, false },
            { CreateRoomKeys.TouristLimit, false },
            { CreateRoomKeys.AllowSpectator, true },
            { CreateRoomKeys.HepaiWay, 0 },
        };
    }
}
