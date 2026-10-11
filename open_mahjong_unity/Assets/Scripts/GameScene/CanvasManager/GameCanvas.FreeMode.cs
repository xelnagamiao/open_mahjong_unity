using System.Collections.Generic;

public partial class GameCanvas {
    public void SetFreeHandCounts(Dictionary<string, int> counts) {
        playerSelfPanel?.SetFreeHandCount(null);
        playerLeftPanel?.SetFreeHandCount(null);
        playerTopPanel?.SetFreeHandCount(null);
        playerRightPanel?.SetFreeHandCount(null);
        if (counts == null) return;
        foreach (KeyValuePair<string, int> pair in counts) {
            if (int.TryParse(pair.Key, out int index))
                GetPlayerPanelByPosition(TableMirror.Current.SeatOf(index))?.SetFreeHandCount(pair.Value);
        }
    }
}
