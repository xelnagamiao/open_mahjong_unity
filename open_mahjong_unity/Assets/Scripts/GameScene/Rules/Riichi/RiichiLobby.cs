using System.Collections.Generic;

internal static class RiichiLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("riichi/standard", "立直麻将(标准)", "立直麻将参照天凤/雀魂规则进行设计，无双倍役满"),
            new RuleLobbySubRule("riichi/langyong", "浪涌麻将", "让每一局，都像海浪般汹涌滔滔｜一、每吃、碰、杠一次，自己的浪涌点数+1（初始为0）。｜二、每1点浪涌，结算时输赢倍数+1。｜三、当全场浪涌累计达到4点，进入“浪潮模式”，结算时倍数再+1。｜四、规则内置可食替｜规则提供：b站up大理石狐自恧"),
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
            { CreateRoomKeys.SubRule, 0 },
            { CreateRoomKeys.Cuohe, false },
            { CreateRoomKeys.HepaiLimit, 1 },
            { CreateRoomKeys.RedDora, true },
            { CreateRoomKeys.AllowKuikae, false },
            { CreateRoomKeys.OpenXiru, true },
            { CreateRoomKeys.OpenTobi, true },
            { CreateRoomKeys.HepaiWay, 0 },
        };
    }
}
