using System;
using Newtonsoft.Json;
using UnityEngine;

public partial class RoomNetworkManager {
    internal static object BuildNewChineseRequest(NewChinese_Create_RoomConfig config, string seed) {
        return new {
                type = "room/create_NewChinese_room", rule = NewChineseGameState.RuleId, sub_rule = NewChineseGameState.SubRule,
                roomname = config.RoomName, gameround = 4 * config.GameRound, password = config.Password, random_seed = seed,
                roundTimerValue = config.RoundTimer, stepTimerValue = config.StepTimer, tips = config.Tips,
                count_tips = config.CountTips, pointer_tips = config.PointerTips, tourist_limit = config.TouristLimit,
                allow_spectator = config.AllowSpectator, event_id = string.IsNullOrEmpty(config.EventId) ? null : config.EventId,
                detailed_config = config.NormalizedConfig(),
            };
    }
    public async void Create_NewChinese_Room(NewChinese_Create_RoomConfig config) {
        if (!TryBeginCreateRequest()) return;
        try {
            if (!TryResolveRandomSeed(config.RandomSeed, out string seed, out string error)) {
                CancelPendingRoomEntry();
                NotificationManager.Instance.ShowTip("create_room", false, error); return;
            }
            var request = BuildNewChineseRequest(config, seed);
            await GetWebSocket().SendText(JsonConvert.SerializeObject(request));
        } catch (Exception e) {
            CancelPendingRoomEntry(); Debug.LogError($"创建新中式麻将房间失败: {e.Message}");
        }
    }
}
