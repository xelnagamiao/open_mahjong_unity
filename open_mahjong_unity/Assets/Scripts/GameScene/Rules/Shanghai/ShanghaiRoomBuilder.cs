using UnityEngine;

internal static class ShanghaiRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        if (input.SubRule != "shanghai/qiaoma" && input.SubRule != "shanghai/qinghunpeng" && input.SubRule != "shanghai/lajihe")
            return RoomCreationRequest.Invalid("不支持的上海麻将子规则");
        var config = new Qingque_Create_RoomConfig {
            ClaimProtection = input.Defaults.ContainsKey(CreateRoomKeys.ClaimProtection) && input.ClaimProtection,
            RoomName = input.RoomName,
            GameRound = input.GameRound,
            Password = input.PasswordEnabled ? input.Password : "",
            RandomSeed = input.RandomSeedEnabled ? input.RandomSeed : "",
            Rule = "shanghai",
            SubRule = input.SubRule,
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
        return RoomCreationRequest.Valid(config, network => network.Create_Shanghai_Room(config, (input.SubRule == "shanghai/qiaoma") && input.HepaiLimitEnabled,
            (input.SubRule == "shanghai/qiaoma") && input.ShanghaiQing3Hun2Peng2));
    }
}
