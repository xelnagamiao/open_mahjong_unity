using System.Collections.Generic;

internal static class QingqueLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("qingque/standard", "青雀", "青雀是由莫莫柴编写的一款麻雀规则，旨在寻求一种在传统麻将行牌规则框架内的做大、抢和、兜牌防守三者平衡的麻雀游戏，同时试图为各类和牌提供基于美感和难度评估的赋分参照；如在测试中发现设计问题或有任何建议，可以联系规则制定人莫莫柴Q1107574，提交bug可在群906497522提交"),
        };

    public static Dictionary<string, object> Defaults() {
        return new Dictionary<string, object> {
            { CreateRoomKeys.GameRound, 4 },
            { CreateRoomKeys.RoundTimer, 3 },
            { CreateRoomKeys.StepTimer, 1 },
            { CreateRoomKeys.Tips, true },
            { CreateRoomKeys.Password, false },
            { CreateRoomKeys.RandomSeed, false },
            { CreateRoomKeys.TouristLimit, false },
            { CreateRoomKeys.AllowSpectator, true },
            { CreateRoomKeys.TacticalCall, false },
        };
    }
}
