using UnityEngine;

internal static class ClassicalRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit();
        if (limitError != null) return limitError;
        var config = new Qingque_Create_RoomConfig {
            ClaimProtection = input.Defaults.ContainsKey(CreateRoomKeys.ClaimProtection) && input.ClaimProtection,
            RoomName = input.RoomName,
            GameRound = input.GameRound,
            Password = input.PasswordEnabled ? input.Password : "",
            RandomSeed = input.RandomSeedEnabled ? input.RandomSeed : "",
            Rule = "classical",
            SubRule = "classical/standard",
            RoundTimer = input.RoundTimer,
            StepTimer = input.StepTimer,
            Tips = input.Tips,
            CountTips = input.CountTips,
            PointerTips = input.PointerTips,
            TouristLimit = input.TouristLimit,
            AllowSpectator = input.AllowSpectator,
            EventId = input.EventId,
        };

        if (!config.Validate(out string error, input.PasswordEnabled, input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid($"创建房间失败: {error}");
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Classical_Room(config));
    }
}
