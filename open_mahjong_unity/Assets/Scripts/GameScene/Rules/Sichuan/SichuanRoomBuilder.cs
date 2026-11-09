using UnityEngine;

internal static class SichuanRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit(0);
        if (limitError != null) return limitError;
        string subRule = input.SubRule;
        bool duplicate = SichuanLobby.IsDuplicate(subRule);
        if (!duplicate && (input.DuplicateEnabled || !string.IsNullOrEmpty(input.DuplicateKey)))
            return RoomCreationRequest.Invalid("川麻复式密钥仅支持双打复式子规则");
        if (duplicate && input.RandomSeedEnabled)
            return RoomCreationRequest.Invalid("复式双打请选择自动生成或输入已有密钥");
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
            Rule = "sichuan",
            SubRule = subRule,
            RoundTimer = input.RoundTimer,
            StepTimer = input.StepTimer,
            Tips = input.Tips,
            CountTips = input.CountTips,
            PointerTips = input.PointerTips,
            TouristLimit = input.TouristLimit,
            AllowSpectator = !duplicate && input.AllowSpectator,
            TacticalCall = input.TacticalCall,
            BloodBattle = bloodBattle,
            HepaiLimit = hepaiLimit,
            EventId = input.EventId,
            DuplicateKey = duplicate ? input.DuplicateKey : null,
            DuplicateRoundCount = duplicate ? input.DuplicateRoundCount : 0,
            DuplicateSyncMode = duplicate && input.DuplicateSyncMode,
        };

        if (!config.Validate(out string error, input.PasswordEnabled, input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid($"创建房间失败: {error}");
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Sichuan_Room(config));
    }
}
