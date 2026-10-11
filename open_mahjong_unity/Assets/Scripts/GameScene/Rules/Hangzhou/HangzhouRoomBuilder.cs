using UnityEngine;

internal static class HangzhouRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var config = new Qingque_Create_RoomConfig {
            Rule=HangzhouGameState.RuleId, SubRule=HangzhouGameState.SubRule,
            RoomName=input.RoomName, GameRound=input.GameRound,
            Password=input.PasswordEnabled ? input.Password : "",
            RandomSeed=input.RandomSeedEnabled ? input.RandomSeed : "",
            RoundTimer=input.RoundTimer, StepTimer=input.StepTimer,
            Tips=input.Tips, CountTips=input.CountTips, PointerTips=input.PointerTips,
            TouristLimit=input.TouristLimit, AllowSpectator=input.AllowSpectator,
            ClaimProtection=false, TacticalCall=input.TacticalCall, EventId=input.EventId,
        };
        if (!config.Validate(out string error,input.PasswordEnabled,input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid(error);
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Hangzhou_Room(config));
    }
}
