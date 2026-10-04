using System.Collections.Generic;

internal static class SichuanLobby {
    public static bool IsXueliu(string subRule) => subRule == "sichuan/xueliu" || subRule == "sichuan/xueliu_exchange";
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("sichuan/standard", "四川麻将(血战到底)", "四川麻将（血战到底）"),
            new RuleLobbySubRule(
                "sichuan/xueliu",
                "血流成河·弃三张",
                "开局弃置三张同花色牌，和牌时须缺一门。和牌后留在牌局中，可多次和牌，牌墙耗尽后结束本局。"),
            new RuleLobbySubRule(
                "sichuan/xueliu_exchange",
                "血流成河·换三张",
                "开局选择三张同花色牌，四家按随机方向交换。保留十三张手牌，换牌后定缺并优先打出定缺花色；可多次和牌，支持七对、金钩钓与根，杠分即时结算。"),
        };

    public static Dictionary<string, object> Defaults() {
        return new Dictionary<string, object> {
            { CreateRoomKeys.ClaimProtection, true },
            { CreateRoomKeys.GameRound, 4 },
            { CreateRoomKeys.RoundTimer, 3 },
            { CreateRoomKeys.StepTimer, 1 },
            { CreateRoomKeys.Tips, true },
            { CreateRoomKeys.CountTips, false },
            { CreateRoomKeys.SubRule, 0 },
            { CreateRoomKeys.PointerTips, true },
            { CreateRoomKeys.Password, false },
            { CreateRoomKeys.RandomSeed, false },
            { CreateRoomKeys.TouristLimit, false },
            { CreateRoomKeys.AllowSpectator, true },
            { CreateRoomKeys.TacticalCall, false },
            { CreateRoomKeys.BloodBattle, true },
            { CreateRoomKeys.HepaiLimit, 0 },
        };
    }
}
