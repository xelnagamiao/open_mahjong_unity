public partial class NormalGameStateManager {
    /// <summary>
    /// 退出对局/牌谱/观战后清理本局运行时状态，不销毁 Manager 单例本身。
    /// </summary>
    public void ResetForExit() {
        CancelWaitAutoAction("ResetForExit");
        IsGameActive = false;
        IsSelfActionRequired = false;
        selfHandTiles.Clear();
        allowActionList.Clear();
        lastCutCardID = 0;
        currentAskCutTileId = 0;
        lastDiscardPlayerPosition = null;
        currentMeldDiscarderPos = null;
        currentMeldClaimedTileId = 0;
        CurrentPlayer = null;
        lastAskHandPlayerIndex = -1;
        lastDealTileType = null;
        pendingBuhuaIsHandSettle = false;
        pendingKanIsMoGang = true;
        selfRiichiCandidateCuts.Clear();
        selfForbiddenCutTiles.Clear();
        chiCandidates.Clear();
        IsQiangGangAsk = false;
        pendingAskFromJiagang = false;
        roomRule = null;
        subRule = null;

        if (RiichiCutSelectionController.Instance != null && RiichiCutSelectionController.Instance.IsActive) {
            RiichiCutSelectionController.Instance.ExitRiichiCutMode();
        }
    }
}
