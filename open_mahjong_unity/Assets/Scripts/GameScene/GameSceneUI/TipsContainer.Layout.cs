using System.Collections.Generic;
using UnityEngine;

public partial class TipsContainer {
    private UpwardPopupLayout hintPopupLayout;
    private string visibleHintSubRule;
    private bool reuseHintRows, visibleShowFan, visibleShowCount;
    private int nextHintTile, nextHintFan;

    private void PrepareHintRows(string subRule, IReadOnlyList<int> tiles) {
        reuseHintRows = visibleHintSubRule == subRule && visibleShowFan == ShowFanTips && visibleShowCount == ShowCountTips
            && TileContainer.transform.childCount == tiles.Count && FanContainer.transform.childCount == tiles.Count;
        for (int i = 0; reuseHintRows && i < tiles.Count; i++) {
            var card = TileContainer.transform.GetChild(i).GetComponent<StaticCard>();
            reuseHintRows = card != null && card.TileId == tiles[i];
        }
        if (!reuseHintRows) ClearHintRows();
        nextHintTile = nextHintFan = 0;
        visibleHintSubRule = subRule;
        visibleShowFan = ShowFanTips; visibleShowCount = ShowCountTips;
    }

    private void ClearHintRows() {
        reuseHintRows = false;
        nextHintTile = nextHintFan = 0;
        // Destroyer detaches immediately. Count/score refreshes keep their cells;
        // only a different set of waits or presentation replaces the rows.
        while (TileContainer.transform.childCount > 0) Destroyer.Instance.AddToDestroyer(TileContainer.transform.GetChild(0));
        while (FanContainer.transform.childCount > 0) Destroyer.Instance.AddToDestroyer(FanContainer.transform.GetChild(0));
    }
}
