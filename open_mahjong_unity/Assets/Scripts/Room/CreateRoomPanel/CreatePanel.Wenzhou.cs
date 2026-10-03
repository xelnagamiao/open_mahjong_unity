public partial class CreatePanel {
    private void CreateWenzhouRoom() {
        var config = new Qingque_Create_RoomConfig {
            Rule = WenzhouGameState.RuleId, SubRule = WenzhouGameState.SubRule,
            RoomName = roomNameInput.text.Trim(), GameRound = GetSelectedGameTime(),
            Password = passwordToggle.isOn ? passwordInput.text.Trim() : "",
            RandomSeed = SetRandomSeedToggle.isOn ? randomSeedInput.text.Trim() : "",
            RoundTimer = GetSelectedRoundTimer(), StepTimer = GetSelectedStepTimer(),
            Tips = tipsToggle.isOn, CountTips = countTipsToggle.isOn, PointerTips = pointerTipsToggle.isOn,
            TouristLimit = TouristLimitToggle.isOn, AllowSpectator = AllowSpectatorToggle.isOn,
            ClaimProtection = false, TacticalCall = false, EventId = _venueEventId,
        };
        if (!config.Validate(out string error, passwordToggle.isOn, SetRandomSeedToggle.isOn)) {
            NotificationManager.Instance.ShowTip("create_room", false, error); return;
        }
        RoomNetworkManager.Instance.Create_Wenzhou_Room(config);
    }
}
