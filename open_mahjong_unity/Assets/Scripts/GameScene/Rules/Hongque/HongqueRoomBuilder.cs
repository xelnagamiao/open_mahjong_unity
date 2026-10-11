using UnityEngine;

internal static class HongqueRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit();
        if (limitError != null) return limitError;
        // 虹雀和牌方式：0=允许多家和（multi_ron），1=头跳（head_bump）
        string hepaiWay = input.HepaiWayIndex switch {
            1 => "head_bump",
            _ => "multi_ron",
        };
        var config = new Jiandan_Create_RoomConfig {
            ClaimProtection = input.Defaults.ContainsKey(CreateRoomKeys.ClaimProtection) && input.ClaimProtection,
            RoomName = input.RoomName,
            GameRound = input.GameRound,
            Password = input.PasswordEnabled ? input.Password : "",
            RandomSeed = input.RandomSeedEnabled ? input.RandomSeed : "",
            Rule = "hongque",
            SubRule = "hongque/v1.6",
            RoundTimer = input.RoundTimer,
            StepTimer = input.StepTimer,
            Tips = input.Tips,
            CountTips = input.CountTips,
            PointerTips = input.PointerTips,
            TouristLimit = input.TouristLimit,
            AllowSpectator = false,
            TacticalCall = false,
            HepaiWay = hepaiWay,
            EventId = input.EventId,
        };
        if (!config.Validate(out string error, input.PasswordEnabled, input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid($"创建房间失败: {error}");
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Hongque_Room(config));
    }
}
