using System.Collections.Generic;

public sealed class NewChinese_Create_RoomConfig : Qingque_Create_RoomConfig {
    public Dictionary<string,object> DetailedConfig;
    public int HepaiLimit { get; set; }
    internal int MatchRounds => GameRound * 4;

    internal static bool UsesExchange(Dictionary<string, object> config) =>
        config != null && config.TryGetValue(CreateRoomKeys.ExchangeThree, out var raw) && raw is bool enabled && enabled;

    public Dictionary<string,object> NormalizedConfig() {
        if (SubRule == SanhuaGameState.SubRule) return new Dictionary<string,object>{{"rule_version","sanhua-2026-11"}};
        return new Dictionary<string,object> {{"rule_version","new-chinese-2026-11"},{CreateRoomKeys.ExchangeThree,UsesExchange(DetailedConfig)}};
    }
}
