using UnityEngine;

internal static class ShangraoRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit();
        if (limitError != null) return limitError;
        var config = new Shangrao_Create_RoomConfig {
            DetailedConfig = input.DetailedConfig,
            Rule = ShangraoGameState.RuleId, SubRule = ShangraoGameState.SubRule,
            RoomName = input.RoomName, GameRound = input.GameRound,
            Password = input.PasswordEnabled ? input.Password : "",
            RandomSeed = input.RandomSeedEnabled ? input.RandomSeed : "",
            RoundTimer = input.RoundTimer, StepTimer = input.StepTimer,
            Tips = input.Tips, CountTips = input.CountTips, PointerTips = input.PointerTips,
            TouristLimit = input.TouristLimit, AllowSpectator = input.AllowSpectator,
            ClaimProtection = false, TacticalCall = false, EventId = input.EventId,
        };
        if (!config.Validate(out string error, input.PasswordEnabled, input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid(error);
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Shangrao_Room(config));
    }
}
