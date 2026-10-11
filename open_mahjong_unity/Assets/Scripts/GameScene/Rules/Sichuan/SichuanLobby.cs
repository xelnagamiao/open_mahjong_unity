using System.Collections.Generic;

internal static class SichuanLobby {
    public const string DoublesSubRule = "sichuan/doubles";
    public const string XueliuDoublesSubRule = "sichuan/xueliu_doubles";
    public const string DoublesDuplicateSubRule = "sichuan/doubles_duplicate";
    public const string XueliuDuplicateSubRule = "sichuan/xueliu_doubles_duplicate";
    public const string DuplicateCreationMessage = "该房间只能在比赛场的管理后台创建";
    public static bool IsDuplicate(string subRule) => subRule == DoublesDuplicateSubRule || subRule == XueliuDuplicateSubRule;
    public static bool IsXueliuDoubles(string subRule) => subRule == XueliuDoublesSubRule || subRule == XueliuDuplicateSubRule;
    public static bool IsTeamMode(string subRule) => IsDoubles(subRule) || IsXueliuDoubles(subRule);
    public static bool IsExchange(string subRule) => subRule == "sichuan/xueliu_exchange" || IsXueliuDoubles(subRule);
    public static bool IsDoubles(string subRule) => subRule == DoublesSubRule || subRule == DoublesDuplicateSubRule;
    public static bool IsXueliu(string subRule) => subRule == "sichuan/xueliu" || IsExchange(subRule);
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
            new RuleLobbySubRule(DoublesSubRule, "血战双打·2V2", "四人同桌、两人一队，对家入座。不定缺、无顺和，和牌仍须缺一门；同队两人先和结束本副，免查叫和花猪。按两人总分判盘胜负，同分按追平队；无法判定时各得0.5盘分。"),
            new RuleLobbySubRule(XueliuDoublesSubRule, "血流双打·2V2", "随机单桌，两人一队、对家入座。分两轮与队友换三张，后手只看队友换出花色；定缺后可控猪，胡牌仍须清缺。仅向对手收分，暗杠共收4分；胡后对手点炮必胡、队友可过。牌墙摸完查叫，花猪每名对手至少32分；队分相同加副。"),
            new RuleLobbySubRule(DoublesDuplicateSubRule, "血战双打复式", DuplicateCreationMessage),
            new RuleLobbySubRule(XueliuDuplicateSubRule, "血流双打复式", DuplicateCreationMessage),
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
