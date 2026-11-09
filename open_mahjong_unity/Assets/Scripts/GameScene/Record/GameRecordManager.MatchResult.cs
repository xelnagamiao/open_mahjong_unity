public partial class GameRecordManager {
    public bool IsRecordMatchResultVisible => EndGamePanel.Instance != null && EndGamePanel.Instance.IsRecordResultVisible;

    bool AtMatchEnd => gameRecord?.gameRound?.rounds != null
        && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)
        && round.actionTicks != null && currentNode >= round.actionTicks.Count
        && !gameRecord.gameRound.rounds.ContainsKey(currentRoundIndex + 1);

    public bool ShowRecordMatchResult() {
        if (!AtMatchEnd || CurrentMode == RecordManagerMode.Spectator) return false;
        CompleteRecordAutoPlay(showCompletedTip: false);
        if (gameRecord.matchResult == null) {
            GameHost.Current.ShowTip("牌谱", false, "该牌谱尚未保存全局结算数据");
            return false;
        }
        if (IsRecordMatchResultVisible) return true;
        ClearRecordRoundEndPanels();
        RoundEndPresentation.Instance.PresentRecordedEndGame(gameRecord.matchResult, gameRecord.gameTitle, HideRecordMatchResult);
        return true;
    }

    public void HideRecordMatchResult() {
        var panel = EndGamePanel.Instance;
        if (panel == null || !panel.IsRecordResult) return;
        panel.ClearEndGamePanel();
        RoundEndPresentation.Instance?.HidePresentationVisual();
        RestoreRecordSelfHandContainer();
    }

    void FinishRecordMatch() {
        if (IsSpectatorSession) NotifyReachedLastAction();
        else ShowRecordMatchResult();
    }
}
