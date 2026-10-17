using UnityEngine;

internal static class RiichiRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit();
        if (limitError != null) return limitError;
        if (!int.TryParse(input.StartingScoreText, out int startingScore)) {
            return RoomCreationRequest.Invalid("请填写有效的起始点数");
        }
        // HepaiWayDropdown 选项顺序：0=多家和，1=三家和了流局，2=头跳
        string hepaiWay = input.HepaiWayIndex switch {
            0 => "multi_ron",
            1 => "three_ron_abort",
            2 => "head_bump",
            _ => "multi_ron",
        };

        int hepaiLimit = (int)input.Defaults[CreateRoomKeys.HepaiLimit];
        if (input.HepaiLimitEnabled && int.TryParse(input.HepaiLimitText, out int parsed)) {
            hepaiLimit = Mathf.Clamp(parsed, 1, 64);
        }

        var config = new Riichi_Create_RoomConfig {
            ClaimProtection = input.Defaults.ContainsKey(CreateRoomKeys.ClaimProtection) && input.ClaimProtection,
            DetailedConfig = input.DetailedConfig,
            RoomName = input.RoomName,
            GameRound = input.GameRound,
            Password = input.PasswordEnabled ? input.Password : "",
            RandomSeed = input.RandomSeedEnabled ? input.RandomSeed : "",
            Rule = "riichi",
            SubRule = input.SubRule,
            RoundTimer = input.RoundTimer,
            StepTimer = input.StepTimer,
            Tips = input.Tips,
            CountTips = input.CountTips,
            PointerTips = input.PointerTips,
            TouristLimit = input.TouristLimit,
            AllowSpectator = input.AllowSpectator,
            CuoHe = input.CuoHe,
            HepaiLimit = hepaiLimit,
            RedDora = input.RedDora,
            StartingScore = startingScore,
            AllowKuikae = input.AllowKuikae,
            OpenXiru = input.OpenXiru,
            OpenTobi = input.OpenTobi,
            HepaiWay = hepaiWay,
            EventId = input.EventId,
        };

        WashizuDoubles.Apply(config);
        if (!config.Validate(out string error, input.PasswordEnabled, input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid($"创建房间失败: {error}");
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Riichi_Room(config));
    }
}
