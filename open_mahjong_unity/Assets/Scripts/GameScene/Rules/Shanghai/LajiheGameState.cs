/// <summary>八花先收分再继续行牌；不倒牌，不把奖励记为一次和牌。</summary>
public sealed class LajiheGameState : TurnBasedGameState {
    public override bool IsSelfLocked => false;
    public bool PassedWin { get; private set; }
    protected override void OnRoundStarted(GameInfo info) { PassedWin = info.lajihe_passed_win == true; }
    protected override void OnHandAskReceived(AskHandActionGBInfo info) {
        if (info.player_index == Session.SelfIndex && info.lajihe_passed_win.HasValue) PassedWin = info.lajihe_passed_win.Value;
    }
    protected override void OnDoAction(Response response) {
        if (response.do_action_info?.lajihe_passed_win.HasValue == true) PassedWin = response.do_action_info.lajihe_passed_win.Value;
        base.OnDoAction(response);
    }
    public override void OnSessionReset() { PassedWin = false; }

    protected override void OnShowResult(Response response) {
        if (response.show_result_info?.hu_class != "lajihe_eight_flowers") {
            base.OnShowResult(response);
            return;
        }
        var env = BuildEnvelope(response.show_result_info);
        Presenter.AppendScoreboard(env);
        Presenter.ApplyScores(env.ScoresAfter);
        GameCanvas.Instance.ShowGangScoreFloats(env.ScoreChanges, 0f);
        if (Mirror.IndexToPosition.TryGetValue(env.WinnerIndex, out string position))
            GameCanvas.Instance.ShowActionDisplay(position, "lajihe_eight_flowers", "shanghai");
    }
}
