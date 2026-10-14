using UnityEngine;

internal static class ChangshaRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit();
        if (limitError != null) return limitError;
        var config = new Changsha_Create_RoomConfig {
            ClaimProtection = input.Defaults.ContainsKey(CreateRoomKeys.ClaimProtection) && input.ClaimProtection,
            RoomName = input.RoomName,
            GameRound = input.GameRound,
            Password = input.PasswordEnabled ? input.Password : "",
            RandomSeed = input.RandomSeedEnabled ? input.RandomSeed : "",
            Rule = "changsha",
            SubRule = "changsha/classic_double_bird",
            RoundTimer = input.RoundTimer,
            StepTimer = input.StepTimer,
            Tips = input.Tips,
            CountTips = input.CountTips,
            PointerTips = input.PointerTips,
            TouristLimit = input.TouristLimit,
            AllowSpectator = input.AllowSpectator,
            TacticalCall = input.TacticalCall,
            OpenKongReplacementCount = input.OpenKongCount,
            InitialHuSiXi = input.InitialSiXi,
            InitialHuBanBanHu = input.InitialBanBanHu,
            InitialHuQueYiSe = input.InitialQueYiSe,
            InitialHuLiuLiuShun = input.InitialLiuLiuShun,
            InitialHuSanTong = input.InitialSanTong,
            BirdCount = input.BirdCount,
            DealerBird = input.DealerBird,
            BaseScoreNoDealer = input.BaseScoreNoDealer,
            SmallHuScore = input.SmallHuScore,
            BigHuScore = input.BigHuScore,
            SmallHuSelfDrawOnly = input.SmallHuSelfDrawOnly,
            ScoreCapType = input.ScoreCapType,
            ScoreCapValue = input.ScoreCapValue,
            EventId = input.EventId,
        };

        if (!config.Validate(out string error, input.PasswordEnabled, input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid($"创建房间失败: {error}");
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Changsha_Room(config));
    }
}
