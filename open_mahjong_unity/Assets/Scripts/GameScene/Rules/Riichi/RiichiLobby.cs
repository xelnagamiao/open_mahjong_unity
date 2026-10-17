using System.Collections.Generic;

internal static class RiichiLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("riichi/standard", "立直麻将(标准)", "四人立直麻将。平台可选雀魂规、天凤规、A规、ML规四种预设，并可以在高级设置中单独调整役种、宝牌、立直、流局、终局顺位与责任支付等规则。"),
            new RuleLobbySubRule("riichi/langyong", "浪涌麻将", "让每一局，都像海浪般汹涌滔滔｜一、每吃、碰、杠一次，自己的浪涌点数+1（初始为0）。｜二、每1点浪涌，结算时输赢倍数+1。｜三、当全场浪涌累计达到4点，进入“浪潮模式”，结算时倍数再+1。｜四、规则内置可食替｜规则提供：b站up大理石狐自恧"),
            new RuleLobbySubRule("riichi/sanma", "三人立直麻将", "三人日麻，默认雀魂规，也可选天凤规。"),
            new RuleLobbySubRule("riichi/washizu", "鹫巢麻将", "鹫巢麻将。每种麻将牌3张透明、1张不透明，共136张；透明手牌公共显示。无赤宝牌，可在吃碰时选择使用透明或不透明牌；全局固定摸牌70次，宝牌指示牌单独获取。"),
            new RuleLobbySubRule(WashizuTile.RealtimeSubRule, "鹫巢麻将（实时手牌）", "鹫巢麻将规则变体，玩家的手牌排序将会实时同步。"),
            new RuleLobbySubRule(WashizuTile.DoublesSubRule, "鹫巢麻将（双打）", WashizuDoubles.Description),
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
