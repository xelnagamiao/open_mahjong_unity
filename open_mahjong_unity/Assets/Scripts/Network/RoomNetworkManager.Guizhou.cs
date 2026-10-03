using System;
using Newtonsoft.Json;
using UnityEngine;

public partial class RoomNetworkManager {
    public async void Create_Guizhou_Room(Qingque_Create_RoomConfig config) {
        if (!TryBeginCreateRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed, out string seed, out string error)) {
                CancelPendingRoomEntry();
                NotificationManager.Instance.ShowTip("create_room", false, error);
                return;
            }
            var request = new {
                type = "room/create_Guizhou_room", rule = GuizhouGameState.RuleId, sub_rule = GuizhouGameState.SubRule,
                roomname = config.RoomName, gameround = config.GameRound, password = config.Password, random_seed = seed,
                roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
                count_tips = config.CountTips, pointer_tips = config.PointerTips, tourist_limit = config.TouristLimit,
                allow_spectator = config.AllowSpectator, event_id = string.IsNullOrEmpty(config.EventId) ? null : config.EventId,
                detailed_config = new { rule_version = GuizhouGameState.RuleVersion },
            };
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            CancelPendingRoomEntry();
            Debug.LogError($"创建贵州麻将房间失败: {e.Message}");
        }
    }
}
