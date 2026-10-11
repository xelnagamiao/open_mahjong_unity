using System.Collections.Generic;

public partial class GameCanvas {
    public void SetHangzhouPlayerStatus(HangzhouInfo info, IReadOnlyDictionary<int, string> positions) {
        playerSelfPanel?.SetHangzhouStatus(null, -1);
        playerRightPanel?.SetHangzhouStatus(null, -1);
        playerTopPanel?.SetHangzhouStatus(null, -1);
        playerLeftPanel?.SetHangzhouStatus(null, -1);
        if (info == null || positions == null) return;
        foreach (var pair in positions)
            GetPlayerPanelByPosition(pair.Value)?.SetHangzhouStatus(info, pair.Key);
    }

    public void SetPlayerFlowerCounts(IReadOnlyDictionary<int, int> counts,
        IReadOnlyDictionary<int, string> positions) {
        playerSelfPanel?.SetYixingFlowerCount(null);
        playerRightPanel?.SetYixingFlowerCount(null);
        playerTopPanel?.SetYixingFlowerCount(null);
        playerLeftPanel?.SetYixingFlowerCount(null);
        if (counts == null || positions == null) return;
        foreach (var pair in counts)
            if (positions.TryGetValue(pair.Key, out string position))
                GetPlayerPanelByPosition(position)?.SetYixingFlowerCount(pair.Value);
    }
}
