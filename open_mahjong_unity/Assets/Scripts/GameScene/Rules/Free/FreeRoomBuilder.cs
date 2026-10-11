using UnityEngine;

internal static class FreeRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit();
        if (limitError != null) return limitError;
        var config = new Free_Create_RoomConfig {
            RoomName = input.RoomName,
            Password = input.PasswordEnabled ? input.Password : "",
            RandomSeed = input.RandomSeedEnabled ? input.RandomSeed : "",
            TouristLimit = false,
            PointerTips = input.PointerTips,
            WallWan = input.WallWan,
            WallTong = input.WallTong,
            WallSuo = input.WallSuo,
            WallWinds = input.WallWinds,
            WallDragons = input.WallDragons,
            WallFlowers = input.WallFlowers,
        };
        if (!config.Validate(out string error, input.PasswordEnabled, input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid($"创建房间失败: {error}");
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Free_Room(config));
    }
}
