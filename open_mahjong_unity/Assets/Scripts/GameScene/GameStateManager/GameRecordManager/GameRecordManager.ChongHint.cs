using System.Collections.Generic;
using UnityEngine;

public partial class GameRecordManager {
    private readonly HashSet<int> currentDangerTileIds = new HashSet<int>();
    private readonly HashSet<int> currentZimoDrawOriginalIndices = new HashSet<int>();
    internal int ConsumedFromFrontForChongHint => consumedFromFront;
    internal int OriginalWallTileCountForChongHint => originalTilesList?.Count ?? 0;
    internal bool IsWaitingForDrawAfterCutForChongHint => waitingForDrawAfterCut;
    internal int LastDiscardPlayerIndexForChongHint => lastDiscardPlayerIndex;
    internal bool IsOriginalWallIndexBackConsumed(int originalIndex) {
        return consumedBackIndices != null && consumedBackIndices.Contains(originalIndex);
    }

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
        // 铳牌提示仅为牌谱 UI 开关（RecordSetting），从未写入 game_title
        return RecordSetting.Instance.IsShowChongHint;
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

        currentZimoDrawOriginalIndices.Clear();
        if (ShouldApplyRecordChongHint() && IsTileListViewVisible()) {
            RecordChongHintCalculator.ComputeMoqieZimoDrawOriginalIndices(this, currentZimoDrawOriginalIndices);
        }

        if (!ShouldApplyRecordChongHint()) {
            UpdateTileListOpacity();
            return;
        }

        currentDangerTileIds.Clear();
        Dictionary<string, RecordPlayer> hintPlayers = GetRecordPlayersForChongHint();
        Dictionary<string, object> detailedConfig = GetDetailedConfigSnapshot();
        foreach (int tileId in RecordChongHintCalculator.ComputeDangerTiles(hintPlayers, roomRule, detailedConfig)) {
            currentDangerTileIds.Add(tileId);
        }

        var hiddenHands = GetChongHintHiddenHandPositions();
        Game3DManager.Instance.ApplyRecordChongHintToShowHands(hintPlayers, roomRule, hiddenHands, detailedConfig);
        ApplyChongToSelf2DHand(hintPlayers, roomRule, hiddenHands, detailedConfig);
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
        currentZimoDrawOriginalIndices.Clear();
        Card3DHoverManager.Instance.ClearAllDangerOverlays();
        ClearSelf2DHandDangerOverlays();
        ClearTileListWallTints();
    }

    private void ApplyChongToSelf2DHand(
        Dictionary<string, RecordPlayer> players,
        string roomRule,
        HashSet<string> hiddenHands,
        IDictionary<string, object> detailedConfig = null
    ) {
        if (hiddenHands.Contains("self")) return;
        if (GameCanvas.Instance.HandCardsContainer == null) return;

        HashSet<int> dangerTileIds = RecordChongHintCalculator.ComputeRonDangerForHandOwner(players, "self", roomRule, detailedConfig);

        Transform container = GameCanvas.Instance.HandCardsContainer;
        for (int i = 0; i < container.childCount; i++) {
            TileCard tileCard = container.GetChild(i).GetComponent<TileCard>();
            if (tileCard == null) continue;
            bool isDanger = dangerTileIds.Contains(TileIdOrder.Normalize(tileCard.tileId));
            tileCard.SetDangerOverlay(isDanger);
        }
    }

    /// <summary>
    /// 仅重涂自家 2D 手牌的铳张遮罩（用当前已计算的危险牌集合按 self 重算），
    /// 不清 3D 明牌、不重算全局危险牌。用于摸牌/初始化新张创建后补涂，
    /// 避免 ChangeHandCards 异步队列导致新张在动画期间缺失铳张提示。
    /// </summary>
    public void ReapplySelf2DHandChongOverlay() {
        if (!ShouldApplyRecordChongHint()) return;
        if (GameCanvas.Instance.HandCardsContainer == null) return;
        TryGetActiveRecordRuleContext(out string roomRule, out _);
        var hiddenHands = GetChongHintHiddenHandPositions();
        ApplyChongToSelf2DHand(
            GetRecordPlayersForChongHint(),
            roomRule,
            hiddenHands,
            GetDetailedConfigSnapshot()
        );
    }

    private void ClearSelf2DHandDangerOverlays() {
        if (GameCanvas.Instance.HandCardsContainer == null) return;
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
