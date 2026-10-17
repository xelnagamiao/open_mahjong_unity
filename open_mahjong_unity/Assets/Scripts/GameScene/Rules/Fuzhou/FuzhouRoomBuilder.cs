using System;
using System.Collections.Generic;

public sealed class FuzhouCreateRoomConfig : Qingque_Create_RoomConfig {
    public int BaseScore=15;
}
internal static class FuzhouRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        int baseScore=15;
        if(input.GameRound!=1 && input.GameRound!=2 && input.GameRound!=4)
            return RoomCreationRequest.Invalid("福州时长只能为一圈、半庄或一雀");
        if(input.DetailedConfig!=null && input.DetailedConfig.TryGetValue("base_score",out object raw)) {
            if(!(raw is int value) || (value!=10&&value!=15&&value!=20)) return RoomCreationRequest.Invalid("福州底分只能为10、15或20");
            baseScore=value;
        }
        var config=new FuzhouCreateRoomConfig {
            Rule=FuzhouGameState.RuleId,SubRule=FuzhouGameState.SubRule,BaseScore=baseScore,
            RoomName=input.RoomName,GameRound=input.GameRound,Password=input.PasswordEnabled?input.Password:"",
            RandomSeed=input.RandomSeedEnabled?input.RandomSeed:"",RoundTimer=input.RoundTimer,StepTimer=input.StepTimer,
            Tips=input.Tips,CountTips=input.CountTips,PointerTips=input.PointerTips,TouristLimit=input.TouristLimit,
            AllowSpectator=input.AllowSpectator,ClaimProtection=false,TacticalCall=false,EventId=input.EventId,
        };
        if(!config.Validate(out string error,input.PasswordEnabled,input.RandomSeedEnabled))return RoomCreationRequest.Invalid(error);
        return RoomCreationRequest.Valid(config,network=>network.Create_Fuzhou_Room(config));
    }
}
