using UnityEngine;

internal static class SichuanRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit(0);
        if (limitError != null) return limitError;
        string subRule = input.SubRule;
        bool duplicate = SichuanLobby.IsDuplicate(subRule);
        string source = duplicate ? input.WallSource ?? "key" : null;
        bool training = duplicate && source == "training";
        if (!duplicate && (input.DuplicateEnabled || !string.IsNullOrEmpty(input.DuplicateKey)))
            return RoomCreationRequest.Invalid("川麻复式密钥仅支持双打复式子规则");
        if (duplicate && input.RandomSeedEnabled && !training)
            return RoomCreationRequest.Invalid("复式双打请选择自动生成或输入已有密钥");
        if (duplicate && source != "training" && source != "key" && source != "seed" && source != "manual")
            return RoomCreationRequest.Invalid("请选择有效的牌山来源");
        if (duplicate && (source == "seed" || source == "manual") && string.IsNullOrWhiteSpace(input.DuplicateKey))
            return RoomCreationRequest.Invalid("请输入所选牌山类型的复式密钥");
        if (training && (!MasterSeedInputValidator.TryNormalizeHex(input.RandomSeed, out var trainingSeed, out var seedError)
            || trainingSeed == new string('0', MasterSeedInputValidator.HexLength)))
            return RoomCreationRequest.Invalid(seedError ?? "训练随机种子不能全为零");
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
            RandomSeed = training || input.RandomSeedEnabled ? input.RandomSeed : "",
            Rule = "sichuan",
            SubRule = subRule,
            RoundTimer = input.RoundTimer,
            StepTimer = input.StepTimer,
            Tips = input.Tips,
            CountTips = input.CountTips,
            PointerTips = input.PointerTips,
            TouristLimit = input.TouristLimit,
            AllowSpectator = (!duplicate || training) && input.AllowSpectator,
            TacticalCall = input.TacticalCall,
            BloodBattle = bloodBattle,
            HepaiLimit = hepaiLimit,
            EventId = input.EventId,
            DuplicateKey = duplicate && !training ? input.DuplicateKey : null,
            WallSource = source,
            DuplicateRoundCount = duplicate ? input.DuplicateRoundCount : 0,
            DuplicateSyncMode = duplicate && input.DuplicateSyncMode,
            RealTime = duplicate && input.RealTime,
            TacticalGraceSeconds = duplicate ? input.TacticalGraceSeconds : 5,
        };

        if (!config.Validate(out string error, input.PasswordEnabled, training || input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid($"创建房间失败: {error}");
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Sichuan_Room(config));
    }
}
