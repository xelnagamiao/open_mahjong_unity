public partial class NormalGameStateManager {
    /// <summary>
    /// 退出对局/牌谱/观战后清理本局运行时状态，不销毁 Manager 单例本身。
    /// 族私有状态由 RuleRegistry.ClearCurrent → IGameState.OnSessionReset 清理。
    /// </summary>
    public void ResetForExit() {
        TurnClock.Current.ResetForExit(); // 含 AutoActionPolicy.Cancel 与切牌约束
        IsGameActive = false;
        awaitingMatchEnd = false;
        hasPendingGameEnd = false;
        pendingGameEndMasterSeed = null;
        pendingGameEndCommitment = null;
        pendingGameEndSalt = null;
        pendingGameEndPlayerFinalData = null;
        selfHandTiles.Clear();
        lastCutCardID = 0;
        lastDiscardPlayerPosition = null;
        currentMeldDiscarderPos = null;
        currentMeldClaimedTileId = 0;
        lastDealTileId = 0;
        chiCandidates.Clear();
        roomRule = null;
        subRule = null;
        RuleRegistry.ClearCurrent();
        detailedConfig.Clear();
        ClearStickerMutes();
    }
}
