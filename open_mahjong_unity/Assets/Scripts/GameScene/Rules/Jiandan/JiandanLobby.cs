using System.Collections.Generic;

internal static class JiandanLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("jiandan/standard", "南雀", "南雀规则由南瓜饼编写，是一个正在测试的规则，目标是在新手易上手与竞技策略深度之间取得平衡。无起和限制。当前版本固定采用一人和牌即止。标准规则将采用三人和牌（血战到底），正在开发中。"),
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
        };
    }
}
