using System.Collections.Generic;

internal static class JiandanLobby {
    public static Dictionary<string, object> Defaults() {
        return new Dictionary<string, object> {
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
        };
    }
}
