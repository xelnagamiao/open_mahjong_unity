using System.Collections.Generic;
using UnityEngine;

public partial class GameRecordManager {
    public bool IsTransparentRecord() => gameRecord?.gameTitle != null
        && gameRecord.gameTitle.TryGetValue("sub_rule", out object value)
        && WashizuTile.UsesTransparentHands(value?.ToString());

    public bool IsWashizuRecord() => gameRecord?.gameTitle != null
        && gameRecord.gameTitle.TryGetValue("sub_rule", out object value)
        && value?.ToString() == WashizuTile.SubRule;
}
