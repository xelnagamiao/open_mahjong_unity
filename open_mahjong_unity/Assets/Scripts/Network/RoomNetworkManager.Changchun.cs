using System;
using Newtonsoft.Json;
using UnityEngine;

public partial class RoomNetworkManager {
    public async void Create_Changchun_Room(Qingque_Create_RoomConfig config) {
        if(!TryBeginCreateRequest()) return;
        try {
            if(!TryResolveRandomSeed(config.RandomSeed,out string seed,out string error)) {
                CancelPendingRoomEntry();NotificationManager.Instance.ShowTip("create_room",false,error);return;
            }
            var request=new CreateGBRoomRequest {
                type="room/create_Changchun_room",rule="changchun",sub_rule="changchun/mil2024",
                roomname=config.RoomName,gameround=config.GameRound,roundTimerValue=config.RoundTimer,stepTimerValue=config.StepTimer,
                tips=config.Tips,count_tips=config.CountTips,pointer_tips=config.PointerTips,password=config.Password,random_seed=seed,
                use_flowers=false,claim_protection=false,open_cuohe=false,hepai_limit=0,tactical_call=config.TacticalCall,
                tourist_limit=config.TouristLimit,allow_spectator=config.AllowSpectator,
                event_id=string.IsNullOrEmpty(config.EventId)?null:config.EventId
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch(Exception e) {CancelPendingRoomEntry();Debug.LogError($"创建长春房间失败: {e.Message}");}
    }
}
