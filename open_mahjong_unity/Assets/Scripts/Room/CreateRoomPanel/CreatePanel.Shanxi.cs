using UnityEngine;

public partial class CreatePanel {
    private void CreateShanxiRoom() {
        var config = new Qingque_Create_RoomConfig {
            ClaimProtection = DefaultsOf(_ruleState).ContainsKey(CreateRoomKeys.ClaimProtection) && ClaimProtectionToggle.isOn,
            RoomName = roomNameInput.text.Trim(),
            GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            Rule = "shanxi",
            SubRule = GetSelectedSubRule(),
            RoundTimer = GetSelectedRoundTimer(),
            StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn,
            CountTips = countTipsToggle.isOn,
            PointerTips = pointerTipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn,
            AllowSpectator = AllowSpectatorToggle.isOn,
            EventId = _venueEventId,
        };

        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            Debug.LogWarning(error);
            NotificationManager.Instance.ShowTip("create_room", false, $"创建房间失败: {error}");
            return;
        }
        RoomNetworkManager.Instance.Create_Shanxi_Room(config);
    }

}
