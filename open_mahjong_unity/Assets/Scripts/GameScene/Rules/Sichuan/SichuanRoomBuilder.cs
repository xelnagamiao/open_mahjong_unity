using UnityEngine;

internal static class SichuanRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit(0);
        if (limitError != null) return limitError;
        string subRule = input.SubRule;
        if (input.DuplicateEnabled && string.IsNullOrWhiteSpace(input.DuplicateKey))
            return RoomCreationRequest.Invalid("请输入复式密钥");
        if (!string.IsNullOrEmpty(input.DuplicateKey) && (subRule != "sichuan/xueliu" && subRule != "sichuan/xueliu_exchange"))
            return RoomCreationRequest.Invalid("此子规则不支持普通血流复式密钥");
        if (!string.IsNullOrEmpty(input.DuplicateKey) && input.RandomSeedEnabled)
            return RoomCreationRequest.Invalid("复式密钥与场景复现种子不能同时使用");
        int hepaiLimit = (int)input.Defaults[CreateRoomKeys.HepaiLimit];
        if (input.HepaiLimitEnabled && int.TryParse(input.HepaiLimitText, out int parsed)) {
            hepaiLimit = parsed;
        }
        bool bloodBattle = SichuanLobby.IsXueliu(subRule) ? false : input.BloodBattle;
        if (SichuanLobby.IsTeamMode(subRule)) { bloodBattle = !SichuanLobby.IsXueliu(subRule); hepaiLimit = 0; }

        var config = new Sichuan_Create_RoomConfig {
            ClaimProtection = input.Defaults.ContainsKey(CreateRoomKeys.ClaimProtection) && input.ClaimProtection,
            RoomName = input.RoomName,
            GameRound = input.GameRound,
            Password = input.PasswordEnabled ? input.Password : "",
            RandomSeed = input.RandomSeedEnabled ? input.RandomSeed : "",
            DuplicateKey = input.DuplicateKey,
            Rule = "sichuan",
            SubRule = subRule,
            RoundTimer = input.RoundTimer,
            StepTimer = input.StepTimer,
            Tips = input.Tips,
            CountTips = input.CountTips,
            PointerTips = input.PointerTips,
            TouristLimit = input.TouristLimit,
            AllowSpectator = input.AllowSpectator,
            TacticalCall = input.TacticalCall,
            BloodBattle = bloodBattle,
            HepaiLimit = hepaiLimit,
            EventId = input.EventId,
        };

        if (!config.Validate(out string error, input.PasswordEnabled, input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid($"创建房间失败: {error}");
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Sichuan_Room(config));
    }
}
