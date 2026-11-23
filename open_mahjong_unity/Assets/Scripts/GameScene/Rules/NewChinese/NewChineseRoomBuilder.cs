using UnityEngine;

internal static class NewChineseRoomBuilder {
    public static RoomCreationRequest Build(RoomCreationInput input) {
        bool sanhua = input.SubRule == SanhuaGameState.SubRule;
        bool exchange = !sanhua && NewChinese_Create_RoomConfig.UsesExchange(input.DetailedConfig);
        int limit = exchange ? NewChineseHand.ExchangeMinimumFan : 0;
        if (!sanhua && !exchange && input.HepaiLimitEnabled && (!int.TryParse(input.HepaiLimitText, out limit) || limit < 0))
            return RoomCreationRequest.Invalid("起和番须为非负整数", true);
        var config = new NewChinese_Create_RoomConfig {
            HepaiLimit = sanhua ? 4 : limit,
            DetailedConfig = input.DetailedConfig,
            Rule = NewChineseGameState.RuleId, SubRule = sanhua ? SanhuaGameState.SubRule : NewChineseGameState.SubRule,
            RoomName = input.RoomName, GameRound = input.GameRound,
            Password = input.PasswordEnabled ? input.Password : "",
            RandomSeed = input.RandomSeedEnabled ? input.RandomSeed : "",
            RoundTimer = input.RoundTimer, StepTimer = input.StepTimer,
            Tips = input.Tips, CountTips = input.CountTips, PointerTips = input.PointerTips,
            TouristLimit = input.TouristLimit, AllowSpectator = input.AllowSpectator,
            ClaimProtection = false, TacticalCall = false, EventId = input.EventId,
        };
        if (!config.Validate(out string error, input.PasswordEnabled, input.RandomSeedEnabled)) {
            return RoomCreationRequest.Invalid(error);
        }
        return RoomCreationRequest.Valid(config, network => network.Create_NewChinese_Room(config));
    }
}
