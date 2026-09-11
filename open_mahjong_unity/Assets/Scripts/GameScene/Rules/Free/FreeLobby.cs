using System.Collections.Generic;

internal static class FreeLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule(
                "free/standard",
                "自由模式",
                "空牌桌沙盒：1–4 人开局，无回合、无计时、不校验吃碰杠。可随意摸切喊牌，推牌倒下、立牌收起；工具箱支持三向弃牌、创建或收回副露、改分与共享转移区。在座玩家选择相同状态后结束本局、重新本局或结束对局回到房间。"),
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
