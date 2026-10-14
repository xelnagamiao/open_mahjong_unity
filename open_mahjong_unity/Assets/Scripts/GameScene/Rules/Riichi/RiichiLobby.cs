using System.Collections.Generic;

internal static class RiichiLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("riichi/standard", "立直麻将(标准)", "四人立直麻将。可选雀魂规、天凤规、A规、ML规预设，并单独调整役种、宝牌、立直、流局、终局顺位与责任支付规则。"),
            new RuleLobbySubRule("riichi/langyong", "浪涌麻将", "让每一局，都像海浪般汹涌滔滔｜一、每吃、碰、杠一次，自己的浪涌点数+1（初始为0）。｜二、每1点浪涌，结算时输赢倍数+1。｜三、当全场浪涌累计达到4点，进入“浪潮模式”，结算时倍数再+1。｜四、规则内置可食替｜规则提供：b站up大理石狐自恧"),
            new RuleLobbySubRule("riichi/sanma", "三人立直麻将", "三人日麻，东南西三家。去掉二至八万，共108张牌；禁止吃牌，可碰杠；北可留在手中或拔出作宝牌。35000起始点、40000目标点，每圈3局。默认雀魂三麻，可选天凤三麻，并设置自摸损、抢拔北和拔北补牌的岭上开花。"),
            new RuleLobbySubRule("riichi/washizu", "鹫巢麻将", "四人鹫巢麻将（三透一暗）。每种牌3张透明、1张不透明，共136张；所有人能看到透明手牌。无赤宝牌，可在吃碰时选择使用透明或不透明牌。无实体牌山，从共同牌池摸牌；全局70次摸牌，庄家首摸和杠后补牌计入，宝牌指示牌另抽。多家荣和按头跳处理，不连庄、不西入，其余采用本房间日麻设置。"),
            new RuleLobbySubRule(WashizuTile.RealtimeSubRule, "鹫巢麻将（实时手牌）", "采用鹫巢麻将规则，并实时公开手牌的摆放位置。起手保留发牌乱序，不按透明或暗牌分组，关闭自动理牌；拖动手牌后，其他玩家会看到相同的顺序和移动动画。暗牌始终只显示牌背。摸牌可保留在独立摸牌位，也可拖入手牌；重连和牌谱保留摆牌顺序。"),
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
