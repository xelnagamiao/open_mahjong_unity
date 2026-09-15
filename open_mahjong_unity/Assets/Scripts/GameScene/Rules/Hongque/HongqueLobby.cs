using System.Collections.Generic;

internal static class HongqueLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("hongque/v1.6", "虹雀", "虹雀是由Null设计的一款以彩虹为主题的拉密类桌游，使用十四种花色、九种数字各一张的麻将牌，最先将手牌全部组成顺子或刻子的玩家赢得一局。牌组的种类千变万化，各种起手都存在无限的可能。游戏尚在测试阶段，如对本规则感兴趣或有任何建议都可以添加虹雀官方Q群497685219一同交流。"),
        };

    public static Dictionary<string, object> Defaults() {
        return new Dictionary<string, object> {
            { CreateRoomKeys.GameRound, 1 },
            { CreateRoomKeys.RoundTimer, 3 },
            { CreateRoomKeys.StepTimer, 1 },
            { CreateRoomKeys.Tips, true },
            { CreateRoomKeys.Password, false },
            { CreateRoomKeys.RandomSeed, false },
            { CreateRoomKeys.TouristLimit, false },
            { CreateRoomKeys.HepaiWay, 0 },
        };
    }
}
