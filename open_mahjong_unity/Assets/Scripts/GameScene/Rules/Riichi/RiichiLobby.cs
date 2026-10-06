using System.Collections.Generic;

internal static class RiichiLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("riichi/standard", "立直麻将(标准)", "四人立直麻将。可选雀魂规、天凤规、A规、ML规预设，并单独调整役种、宝牌、立直、流局、终局顺位与责任支付规则。"),
            new RuleLobbySubRule("riichi/langyong", "浪涌麻将", "让每一局，都像海浪般汹涌滔滔｜一、每吃、碰、杠一次，自己的浪涌点数+1（初始为0）。｜二、每1点浪涌，结算时输赢倍数+1。｜三、当全场浪涌累计达到4点，进入“浪潮模式”，结算时倍数再+1。｜四、规则内置可食替｜规则提供：b站up大理石狐自恧"),
            new RuleLobbySubRule("riichi/sanma", "三人立直麻将", "三人日麻，东南西三家。去掉二至八万，共108张牌；禁止吃牌，可碰杠；北可留在手中或拔出作宝牌。35000起始点、40000目标点，每圈3局。默认雀魂三麻，可选天凤三麻，并设置自摸损、抢拔北和拔北补牌的岭上开花。"),
        };

    public static Dictionary<string, object> Defaults() {
        return new Dictionary<string, object> {
            { CreateRoomKeys.ClaimProtection, false },
            { CreateRoomKeys.GameRound, 4 },
            { CreateRoomKeys.RoundTimer, 3 },
            { CreateRoomKeys.StepTimer, 1 },
            { CreateRoomKeys.Tips, false },
            { CreateRoomKeys.CountTips, true },
            { CreateRoomKeys.PointerTips, true },
            { CreateRoomKeys.Password, false },
            { CreateRoomKeys.RandomSeed, false },
            { CreateRoomKeys.TouristLimit, false },
            { CreateRoomKeys.AllowSpectator, true },
            { CreateRoomKeys.SubRule, 0 },
            { CreateRoomKeys.Cuohe, false },
            { CreateRoomKeys.HepaiLimit, 1 },
            { CreateRoomKeys.RedDora, true },
            { CreateRoomKeys.StartingScore, 25000 },
            { CreateRoomKeys.AllowKuikae, false },
            { CreateRoomKeys.OpenXiru, true },
            { CreateRoomKeys.OpenTobi, true },
            { CreateRoomKeys.HepaiWay, 0 },
        };
    }
}
