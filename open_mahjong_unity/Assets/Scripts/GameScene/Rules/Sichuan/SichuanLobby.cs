using System.Collections.Generic;

internal static class SichuanLobby {
    public static readonly RuleLobbySubRule[] SubRules =
        new RuleLobbySubRule[] {
            new RuleLobbySubRule("sichuan/standard", "四川麻将(血战到底)", "四川麻将（血战到底）"),
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
            { CreateRoomKeys.BloodBattle, true },
        };
    }
}
