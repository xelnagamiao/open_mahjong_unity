using System.Collections.Generic;
public sealed class Duocai_Create_RoomConfig : Qingque_Create_RoomConfig {
    public Dictionary<string,object> DetailedConfig;
    public Dictionary<string,object> NormalizedConfig()=>new Dictionary<string,object>{{"rule_version","duocai-v1"}};
}
