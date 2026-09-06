using System.Collections.Generic;

internal static class GuobiaoLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("guobiao/standard", "标准规(新编MCR)", "国标麻将源于国家体育总局于1998年11月出台的《中国竞技麻将比赛规则(试行)》、是中国唯一由官方确立的竞技麻将规则；本平台参照Natsuki编著的新编MCR撰写运行逻辑，已通过所有牌例验证，如发现测试过程中出现了不符合国标麻将规则预期的行为，请向Q群906497522反馈。"),
            new RuleLobbySubRule("guobiao/xiaolin", "国标麻将(小林改)", "小林改版国标麻将，对国标麻将进行了番数平衡，还处于测试版，取消了8番起胡和底分，改为点和得分x2，自摸番三。非竞技规则，只为娱乐。"),
            new RuleLobbySubRule("guobiao/kshen", "K神麻将", "K神改版国标麻将，新增镜同、四连刻等番种，复合番100封顶，默认8番起和。小牌点炮无责：点和12分以下三家各付n；12分以上两家各付12，放铳者付3n-24。自摸三家各付n。可开启错和、可自定义起和番。出现计分bug可在群里向q975653345反馈"),
            new RuleLobbySubRule("guobiao/lanshi", "国标麻将(蓝十改)", "蓝十改版的国标麻将规则，对国标麻将的番种表进行了全面的修改，并根据番种的难度调整了评分，5分起和，授受制为半全铳半分付。如在测试中发现设计问题或有任何建议，可以联系规则制定人蓝十QQ1002094810。"),
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
            { CreateRoomKeys.CuoheType, 0 },
            { CreateRoomKeys.HepaiLimit, 8 },
            { CreateRoomKeys.TacticalCall, true },
        };
    }
}
