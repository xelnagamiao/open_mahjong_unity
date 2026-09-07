using System.Collections.Generic;

internal static class FreeLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule(
                "free/standard",
                "自由模式",
                "空牌桌沙盒：无回合、无计时、不校验吃碰杠。四人准备后开局，可随意摸切喊牌、倒牌立牌、创建或收回副露，并改分、使用共享转移区。凑齐四张相同状态票后结束本局、重新本局或结束对局回到房间。"),
        };

    public static Dictionary<string, object> Defaults() {
        return new Dictionary<string, object> {
            { CreateRoomKeys.Password, false },
            { CreateRoomKeys.RandomSeed, false },
            { CreateRoomKeys.WallWan, true },
            { CreateRoomKeys.WallTong, true },
            { CreateRoomKeys.WallSuo, true },
            { CreateRoomKeys.WallWinds, true },
            { CreateRoomKeys.WallDragons, true },
            { CreateRoomKeys.WallFlowers, true },
        };
    }
}
