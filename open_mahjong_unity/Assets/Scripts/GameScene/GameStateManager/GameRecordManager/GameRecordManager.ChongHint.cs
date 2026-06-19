using System.Collections.Generic;

using UnityEngine;



public partial class GameRecordManager {

    private readonly HashSet<int> currentDangerTileIds = new HashSet<int>();

    private int currentZimoDrawOriginalIndex = -1;



    internal List<int> GetCurrentTilesListForSim() => currentTilesList;

    internal List<int> GetCurrentOriginalIndicesForSim() => currentOriginalIndices;

    internal HashSet<int> GetConsumedBackIndicesForSim() => consumedBackIndices;

    internal string GetBackwardTilesTypeForSim() => backwardTilesType;



    internal int GetRevealedKanDoraCount() => System.Math.Max(0, recordRiichiDoraIndicators.Count - 1);

    internal int GetRinshanCount() => consumedBackIndices.Count;

    internal bool IsRiichiDeadWallBlockingZimoAt(int originalIndex, string roomRule) {
        return RecordChongHintCalculator.IsRiichiDeadWallBlockingZimo(
            roomRule,
            originalIndex,
            originalTilesList.Count,
            consumedBackIndices,
            GetRevealedKanDoraCount(),
            GetRinshanCount());
    }

    public static bool ShouldApplyRecordChongHint() {

        var mgr = Instance;

        if (mgr == null || !mgr.gameObject.activeSelf || mgr.gameRecord == null) return false;

        if (RecordSetting.Instance != null) return RecordSetting.Instance.IsShowChongHint;

        return mgr.ShowChongHint;

    }



    private static bool IsTileListViewVisible() {

        var mgr = Instance;

        return mgr != null && mgr.tileListView != null && mgr.tileListView.activeSelf;

    }



    public void ClearRecordChongHintVisuals() {

        ClearAllChongOverlays();

        UpdateTileListOpacity();

    }



    public void RefreshRecordChongHint() {

        ClearAllChongOverlays();

        TryGetActiveRecordRuleContext(out string roomRule, out _);



        currentZimoDrawOriginalIndex = -1;

        if (ShouldApplyRecordChongHint() && IsTileListViewVisible()) {

            if (RecordChongHintCalculator.TryPredictNextSelfDrawOriginalIndex(this, out int zimoIdx)

                && !IsRiichiDeadWallBlockingZimoAt(zimoIdx, roomRule)) {

                currentZimoDrawOriginalIndex = zimoIdx;

            }

        }



        if (!ShouldApplyRecordChongHint()) {

            UpdateTileListOpacity();

            return;

        }



        currentDangerTileIds.Clear();

        foreach (int tileId in RecordChongHintCalculator.ComputeDangerTiles(recordPlayer_to_info, roomRule)) {

            currentDangerTileIds.Add(tileId);

        }



        var hiddenHands = GetChongHintHiddenHandPositions();

        Game3DManager.Instance?.ApplyRecordChongHintToShowHands(recordPlayer_to_info, roomRule, hiddenHands);

        ApplyChongToSelf2DHand(roomRule, hiddenHands);

        UpdateTileListOpacity();

    }



    private HashSet<string> GetChongHintHiddenHandPositions() {

        var hidden = new HashSet<string>();

        if (!waitingForDrawAfterCut || lastDiscardPlayerIndex < 0) return hidden;

        if (indexToPosition.TryGetValue(lastDiscardPlayerIndex, out string position)) {

            hidden.Add(position);

        }

        return hidden;

    }



    private void ClearAllChongOverlays() {

        currentDangerTileIds.Clear();

        currentZimoDrawOriginalIndex = -1;

        if (Card3DHoverManager.Instance != null) {

            Card3DHoverManager.Instance.ClearAllDangerOverlays();

        }

        ClearSelf2DHandDangerOverlays();

        ClearTileListWallTints();

    }



    private void ApplyChongToSelf2DHand(string roomRule, HashSet<string> hiddenHands) {

        if (hiddenHands.Contains("self")) return;

        if (GameCanvas.Instance == null || GameCanvas.Instance.HandCardsContainer == null) return;



        HashSet<int> dangerTileIds = RecordChongHintCalculator.ComputeRonDangerForHandOwner(

            recordPlayer_to_info, "self", roomRule);



        Transform container = GameCanvas.Instance.HandCardsContainer;

        for (int i = 0; i < container.childCount; i++) {

            TileCard tileCard = container.GetChild(i).GetComponent<TileCard>();

            if (tileCard == null) continue;

            bool isDanger = dangerTileIds.Contains(TileIdOrder.Normalize(tileCard.tileId));

            tileCard.SetDangerOverlay(isDanger);

        }

    }



    private void ClearSelf2DHandDangerOverlays() {

        if (GameCanvas.Instance == null || GameCanvas.Instance.HandCardsContainer == null) return;

        Transform container = GameCanvas.Instance.HandCardsContainer;

        for (int i = 0; i < container.childCount; i++) {

            TileCard tileCard = container.GetChild(i).GetComponent<TileCard>();

            tileCard?.ClearDangerOverlay();

        }

    }



    private void ClearTileListWallTints() {

        foreach (StaticCard sc in tileListCards) {

            sc?.ClearWallTints();

        }

    }



    public void MarkRecordPlayerHu(int hepaiPlayerIndex) {

        if (!IsSichuanBloodBattleRecord()) return;

        if (!indexToPosition.TryGetValue(hepaiPlayerIndex, out string position)) return;

        if (recordPlayer_to_info.TryGetValue(position, out RecordPlayer player)) {

            player.isHu = true;

        }

    }



    public void OnRecordPlayerCut(RecordPlayer player) {

        if (player == null) return;

        TryGetActiveRecordRuleContext(out string roomRule, out _);

        if (roomRule == "sichuan") {

            RecordChongHintCalculator.TryInferRecordDingqueSuit(player);

        }

    }

}


