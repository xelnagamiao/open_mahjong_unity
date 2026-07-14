public partial class NormalGameStateManager {
    /// <summary>
    /// 退出对局/牌谱/观战后清理本局运行时状态，不销毁 Manager 单例本身。
    /// </summary>
    public void ResetForExit() {
        ClearChangshaSeaBottomVisual();
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
        lastDealTileId = 0;
        selfRiichiCandidateCuts.Clear();
        selfForbiddenCutTiles.Clear();
        selfForcedCutTiles.Clear();
        chiCandidates.Clear();
        IsQiangGangAsk = false;
        pendingAskFromJiagang = false;
        roomRule = null;
        subRule = null;
        changshaBaseScoreNoDealer = false;
        changshaSmallHuScore = 2;
        changshaBigHuScore = 8;

        if (RiichiCutSelectionController.Instance.IsActive) {
            RiichiCutSelectionController.Instance.ExitRiichiCutMode();
        }
    }
}
