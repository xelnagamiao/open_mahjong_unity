using System;
using Newtonsoft.Json;
using UnityEngine;

public partial class RoomNetworkManager {
    public async void Create_Guangdong_Room(Qingque_Create_RoomConfig config, bool minimumScore) {
        if (!TryBeginCreateRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed,out string seed,out string error)) {
                CancelPendingRoomEntry(); NotificationManager.Instance.ShowTip("create_room",false,error); return;
            }
            var request=new {
                type="room/create_Guangdong_room",rule="guangdong",sub_rule=config.SubRule,
                roomname=config.RoomName,gameround=config.GameRound,roundTimerValue=config.RoundTimer,stepTimerValue=config.StepTimer,
                tips=config.Tips,count_tips=config.CountTips,pointer_tips=config.PointerTips,password=config.Password,random_seed=seed,
                open_cuohe=false,hepai_limit=GuangdongMilRules.IsMil(config.SubRule)?2:0,use_flowers=false,claim_protection=false,tactical_call=false,tourist_limit=config.TouristLimit,allow_spectator=config.AllowSpectator,
                event_id=string.IsNullOrEmpty(config.EventId)?null:config.EventId,
                detailed_config=GuangdongMilRules.IsMil(config.SubRule)?GuangdongMilRules.Detail(minimumScore):null,
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch(Exception error) { CancelPendingRoomEntry(); Debug.LogError($"创建广东麻将房间失败: {error.Message}"); }
    }
}
