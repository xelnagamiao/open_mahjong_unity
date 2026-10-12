using UnityEngine;

internal static class FilipinoRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        var limitError = input.ValidateHepaiLimit();
        if (limitError != null) return limitError;
        var config=new Filipino_Create_RoomConfig {
            RoomName=input.RoomName, GameRound=input.GameRound, Rule="filipino",SubRule=input.SubRule,
            Password=input.PasswordEnabled?input.Password:"",RandomSeed=input.RandomSeedEnabled?input.RandomSeed:"",
            RoundTimer=input.RoundTimer,StepTimer=input.StepTimer,Tips=input.Tips,
            CountTips=input.CountTips,PointerTips=input.PointerTips,TouristLimit=input.TouristLimit,
            AllowSpectator=input.AllowSpectator,EventId=input.EventId,DetailedConfig=input.DetailedConfig,
        };
        if (!config.Validate(out string error,input.PasswordEnabled,input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid(error);
        }
        return RoomCreationRequest.Valid(config, network => network.Create_Filipino_Room(config));
    }
}
