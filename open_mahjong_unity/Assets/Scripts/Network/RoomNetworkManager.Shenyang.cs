using System;
using Newtonsoft.Json;
using UnityEngine;

public partial class RoomNetworkManager {
    public async void Create_Shenyang_Room(Qingque_Create_RoomConfig config) {
        if(!TryBeginCreateRequest()) return;
        try {
            if(!TryResolveRandomSeed(config.RandomSeed,out string seed,out string error)) {
                CancelPendingRoomEntry();NotificationManager.Instance.ShowTip("create_room",false,error);return;
            }
            var request=new CreateGBRoomRequest {
                type="room/create_Shenyang_room",rule="shenyang",sub_rule="shenyang/mil2025",
                roomname=config.RoomName,gameround=config.GameRound,roundTimerValue=config.RoundTimer,stepTimerValue=config.StepTimer,
                tips=config.Tips,count_tips=config.CountTips,pointer_tips=config.PointerTips,password=config.Password,random_seed=seed,
                use_flowers=false,claim_protection=false,open_cuohe=false,hepai_limit=0,tactical_call=false,
                tourist_limit=config.TouristLimit,allow_spectator=config.AllowSpectator,
                event_id=string.IsNullOrEmpty(config.EventId)?null:config.EventId
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch(Exception e) {CancelPendingRoomEntry();Debug.LogError($"创建沈阳房间失败: {e.Message}");}
    }
}
