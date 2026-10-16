using System.Collections.Generic;

internal static class ChangshaLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("changsha/classic_double_bird", "长沙麻将(经典双鸟)", "长沙麻将经典双鸟规则：108张数牌，可吃上家牌，258将小胡，大胡可叠加，和牌后翻两只鸟并按座位中鸟加倍。"),
            new RuleLobbySubRule(ChangshaGameState.ExchangeSixSubRule, "长沙麻将(换六张)", "换六张固定玩法：108张数牌，四家先各选六张，最多两种花色，再掷骰交换。每小局另掷抓牌骰子，指定一家不能胡普通小胡，四喜照常收分。杠摇四张、补张一张、两只鸟。小胡2分、单大8分，每多一大翻倍；相同牌型的杠胡合并算，不同牌型分别算后相加。支持报听、明牌和闷杠；杠摇后锁住原手，保持听牌可补张。"),
        };

    public static Dictionary<string, object> Defaults() {
        return new Dictionary<string, object> {
            { CreateRoomKeys.SubRule, 0 },
            { CreateRoomKeys.ClaimProtection, true },
            { CreateRoomKeys.GameRound, 4 },
            { CreateRoomKeys.RoundTimer, 3 },
            { CreateRoomKeys.StepTimer, 1 },
            { CreateRoomKeys.Tips, true },
            { CreateRoomKeys.CountTips, false },
            { CreateRoomKeys.PointerTips, true },
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
            { CreateRoomKeys.CsSmallHuSelfDrawOnly, false },
            { CreateRoomKeys.CsScoreCapType, "none" },
            { CreateRoomKeys.CsScoreCapValue, 0 },
        };
    }
}
