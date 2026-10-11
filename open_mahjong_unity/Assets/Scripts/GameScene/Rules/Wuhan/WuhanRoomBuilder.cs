using UnityEngine;

internal static class WuhanRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit();
        if (limitError != null) return limitError;
        var config = new Qingque_Create_RoomConfig {
            Rule=WuhanGameState.RuleId, SubRule=WuhanGameState.SubRule,
            RoomName=input.RoomName, GameRound=input.GameRound,
            Password=input.PasswordEnabled ? input.Password : "",
            RandomSeed=input.RandomSeedEnabled ? input.RandomSeed : "",
            RoundTimer=input.RoundTimer, StepTimer=input.StepTimer,
            Tips=input.Tips, CountTips=input.CountTips, PointerTips=input.PointerTips,
            TouristLimit=input.TouristLimit, AllowSpectator=input.AllowSpectator,
            ClaimProtection=false, TacticalCall=false, EventId=input.EventId,
        };
        if (!config.Validate(out string error,input.PasswordEnabled,input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid(error);
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Wuhan_Room(config));
    }
}
