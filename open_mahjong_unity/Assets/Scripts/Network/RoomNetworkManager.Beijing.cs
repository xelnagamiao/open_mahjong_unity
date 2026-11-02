using System;
using Newtonsoft.Json;
using UnityEngine;

public partial class RoomNetworkManager {
    internal static object BuildBeijingRequest(Beijing_Create_RoomConfig config, string seed) {
        return new {
                type = "room/create_Beijing_room", rule = BeijingGameState.RuleId, sub_rule = BeijingGameState.SubRule,
                roomname = config.RoomName, gameround = config.GameRound, password = config.Password, random_seed = seed,
                roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
                count_tips = config.CountTips, pointer_tips = config.PointerTips, tourist_limit = config.TouristLimit,
                allow_spectator = config.AllowSpectator, event_id = string.IsNullOrEmpty(config.EventId) ? null : config.EventId,
                detailed_config = config.NormalizedConfig(),
            };
    }
    public async void Create_Beijing_Room(Beijing_Create_RoomConfig config) {
        if (!TryBeginCreateRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed, out string seed, out string error)) {
                CancelPendingRoomEntry();
                NotificationManager.Instance.ShowTip("create_room", false, error); return;
            }
            var request = BuildBeijingRequest(config, seed);
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            CancelPendingRoomEntry(); Debug.LogError($"创建北京麻将房间失败: {e.Message}");
        }
    }
}
