using System.Collections.Generic;

internal static class TaiwanLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("taiwan/standard", "台湾麻将", "台湾麻将：使用144张牌与16张手牌，按台计分，支持公开报听、食替限制与八仙过海等规则。具体流程与台表可在馆规设置中选择。"),
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
            { CreateRoomKeys.Cuohe, false },
            { CreateRoomKeys.CuoheType, 0 },
        };
    }
}
