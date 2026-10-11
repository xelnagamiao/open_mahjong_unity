using UnityEngine;

internal static class GuobiaoRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit();
        if (limitError != null) return limitError;
        if (input.DuplicateEnabled && string.IsNullOrEmpty(input.DuplicateKey))
            return RoomCreationRequest.Invalid("请填写在网站账户面板创建的复式密钥");
        string subRule = input.SubRule;
        int hepaiLimit = ResolveHepaiLimit(input);

        var config = new GB_Create_RoomConfig {
            ClaimProtection = input.Defaults.ContainsKey(CreateRoomKeys.ClaimProtection) && input.ClaimProtection,
            RoomName = input.RoomName,
            GameRound = input.GameRound,
            Password = input.PasswordEnabled ? input.Password : "",
            RandomSeed = input.RandomSeedEnabled ? input.RandomSeed : "",
            DuplicateKey = input.DuplicateKey,
            UseFlowers = subRule != "guobiao/lanshi" && (input.UseFlowers),
            TianDiRenHe = (subRule == "guobiao/standard" || subRule == GuobiaoGameState.SanmaSubRule || subRule == GuobiaoGameState.BloodBattleSubRule || subRule == GuobiaoGameState.TransparentSubRule) && input.TianDiRenHe,
            Rule = "guobiao",
            SubRule = subRule,
            RoundTimer = input.RoundTimer,
            StepTimer = input.StepTimer,
            Tips = input.Tips,
            CountTips = input.CountTips,
            PointerTips = input.PointerTips,
            CuoHe = subRule != "guobiao/blood_battle" && (subRule == "guobiao/lanshi" || input.CuoHe),
            CuoheType = subRule == "guobiao/lanshi" ? 1 : input.CuoheType,
            HepaiLimit = subRule == "guobiao/blood_battle" ? 8 : hepaiLimit,
            TouristLimit = input.TouristLimit,
            AllowSpectator = input.AllowSpectator,
            TacticalCall = subRule != "guobiao/blood_battle" && input.TacticalCall,
            EventId = input.EventId,
        };

        if (!config.Validate(out string error, input.PasswordEnabled, input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid($"创建房间失败: {error}");
        }
        return RoomCreationRequest.Valid(config, network => network.Create_GB_Room(config));
    }
    private static int ResolveHepaiLimit(RoomCreationInput input) {
        if (input.SubRule == "guobiao/lanshi") return 5;
        int value = input.SubRule == "guobiao/xiaolin" ? 1 : 8;
        if (input.HepaiLimitEnabled && int.TryParse(input.HepaiLimitText, out int parsed)) value = Mathf.Clamp(parsed, 1, 64);
        return value;
    }

}
