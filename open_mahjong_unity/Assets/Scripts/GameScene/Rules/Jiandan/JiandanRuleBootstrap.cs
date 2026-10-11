using UnityEngine;

// Nanque statistics still use the jiandan database key. Gameplay and replays use zhongyong/nanque.
internal static class JiandanRuleBootstrap {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register() {
        RuleRegistry.Register(new RuleManifest {
            ReplayFactory = record => record.CreateStandardReplayModule(),
            BuildRoom = input => RoomCreationRequest.Invalid("请在中庸麻将中选择南雀玩法"),
            RuleId = "jiandan",
            DisplayName = "南雀",
            HideFromLobby = true,
            StatsFanNames = JiandanFanText.FanNameToDisplayJiandan,
            FanNameText = JiandanFanText.FanName,
        });
    }
}
