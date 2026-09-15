using System.Collections.Generic;

internal static class ChangshaLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("changsha/classic_double_bird", "长沙麻将(经典双鸟)", "长沙麻将经典双鸟规则：108张数牌，可吃上家牌，258将小胡，大胡可叠加，和牌后翻两只鸟并按座位中鸟加倍。"),
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
            { CreateRoomKeys.TacticalCall, false },
            { CreateRoomKeys.CsOpenKongCount, 2 },
            { CreateRoomKeys.CsInitialSiXi, true },
            { CreateRoomKeys.CsInitialBanBanHu, true },
            { CreateRoomKeys.CsInitialQueYiSe, true },
            { CreateRoomKeys.CsInitialLiuLiuShun, true },
            { CreateRoomKeys.CsInitialSanTong, true },
            { CreateRoomKeys.CsBirdCount, 2 },
            { CreateRoomKeys.CsDealerBird, true },
            { CreateRoomKeys.CsBaseScoreNoDealer, false },
            { CreateRoomKeys.CsSmallHuScore, 2 },
            { CreateRoomKeys.CsBigHuScore, 8 },
        };
    }
}
