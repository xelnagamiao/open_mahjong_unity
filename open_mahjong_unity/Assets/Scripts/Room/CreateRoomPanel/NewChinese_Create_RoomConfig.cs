using System.Collections.Generic;

public sealed class NewChinese_Create_RoomConfig : Qingque_Create_RoomConfig {
    public Dictionary<string,object> DetailedConfig;
    internal int MatchRounds => GameRound * 4;

    public Dictionary<string,object> NormalizedConfig() {
        bool exchange=DetailedConfig!=null && DetailedConfig.TryGetValue(CreateRoomKeys.ExchangeThree,out var raw) && raw is bool value && value;
        return new Dictionary<string,object> {{"rule_version","new-chinese-2026-11"},{CreateRoomKeys.ExchangeThree,exchange}};
    }
}
