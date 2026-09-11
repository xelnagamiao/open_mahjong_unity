using UnityEngine;

internal static class FreeRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            RuleId = FreeGameState.RuleId,
            DefaultSubRule = "free/standard",
            DisplayName = "自由模式",
            LobbyOrder = 9,
            LobbySubRules = FreeLobby.SubRules,
            CreateRoomDefaults = FreeLobby.Defaults(),
            GameStateFactory = () => new FreeGameState(),
            OutboundChannel = "free",
            HasFlowerReplacement = false,
            AllowsRoomBots = false,
            MinPlayersToStart = 1,
            UsesDedicatedActionButtonContainer = true,
            ActionCaption = word => word == "hu" ? "和牌" : null,
        });
        FreeActionWords.RegisterAll();
    }
}
