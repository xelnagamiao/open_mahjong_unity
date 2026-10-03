using System.Collections.Generic;

public class NanqueGameState : ZhongyongGameState {
    public const string BloodBattleSubRule = "zhongyong/nanque";
    private bool bloodPendingContinue;
    private bool bloodHistoryWritten;
    private bool IsBloodBattle => true;

    protected override void OnRoundStarted(GameInfo gameInfo) => RestoreBloodBattle(gameInfo);
    protected override void OnBeforeAsk() => ResumeBloodBattle();
    public override void OnSessionReset() {
        bloodPendingContinue = false;
        bloodHistoryWritten = false;
    }

    protected override void OnShowResult(Response response) {
        var info = response.show_result_info;
        if (info != null && info.blood_battle_step == "mid_win"
                && Mirror.IndexToPosition.TryGetValue(info.hepai_player_index, out string seat)) {
            var player = Mirror.Info(seat);
            if (player != null) {
                var tags = new List<string>(player.tag_list ?? new string[0]);
                if (!tags.Contains("first_hu") && !tags.Contains("second_hu") && !tags.Contains("third_hu")) {
                    int count = 0;
                    foreach (string position in Mirror.IndexToPosition.Values) {
                        if (SeatHasTag(position, t => t == "first_hu" || t == "second_hu" || t == "third_hu")) count++;
                    }
                    tags.Add(new[] { "first_hu", "second_hu", "third_hu" }[System.Math.Min(count, 2)]);
                    RefreshTags(new Dictionary<int, string[]> { { info.hepai_player_index, tags.ToArray() } });
                }
            }
        }
        base.OnShowResult(response);
    }

    private void RestoreBloodBattle(GameInfo gameInfo) {
        bloodPendingContinue = false;
        bloodHistoryWritten = false;
        if (!IsBloodBattle) return;
        if (gameInfo.players_info != null) {
            foreach (PlayerInfo player in gameInfo.players_info) {
                if (player == null) continue;
                if (player.round_number_history != null && player.round_number_history.Length > 0
                        && player.round_number_history[player.round_number_history.Length - 1] == Mirror.CurrentRound) {
                    bloodHistoryWritten = true;
                }
                if (player.is_hu == true && Mirror.IndexToPosition.TryGetValue(player.player_index, out string position)) {
                    Game3DManager.Instance.RestoreBloodBattleWinMarker(
                        position, player.blood_hu_tile ?? 0, player.blood_hu_zimo == true, player.blood_hu_multi == true);
                }
            }
        }
        if (gameInfo.blood_battle_result != null) {
            Game3DManager.Instance.RevealSichuanLiujuAllHands(gameInfo.blood_battle_result.liuju_hu_hands);
            PresentSettlement(BuildEnvelope(gameInfo.blood_battle_result));
        }
    }

    private void ResumeBloodBattle() {
        if (!bloodPendingContinue) return;
        bloodPendingContinue = false;
        RoundEndPresentation.Instance.StopActiveSequence();
        EndResultPanel.Instance?.ClearEndResultPanel();
        RoundEndPresentation.Instance.ShowSelfGameplayControlAndResyncHand3D();
    }

    protected override void PresentSettlement(SettlementEnvelope env) {
        if (!IsBloodBattle) {
            base.PresentSettlement(env);
            return;
        }
        Manager.BeginSettlement();
        Presenter.BeginLifecycle(env);
        Presenter.CloseTableForSettlement();
        if (env.BloodBattleStep == "mid_win") {
            bloodPendingContinue = true;
            Presenter.PresentHu(env);
            return;
        }
        bloodPendingContinue = false;
        if (env.BloodBattleStep == "reveal_hu") {
            RoundEndPresentation.Instance.StopActiveSequence();
            RoundEndPresentation.Instance.HideSelfGameplayControl(false);
            Game3DManager.Instance.RevealSichuanLiujuAllHands(env.LiujuHuHands);
            return;
        }
        if (env.FinalPanel && !bloodHistoryWritten) {
            string label = env.BloodEndReason == "three_winners" ? SichuanRoundLabel.ThreeHu : SichuanRoundLabel.Liuju;
            RoundSettlementSnapshot snapshot = ScoreHistorySettlementHelper.CreateSichuanScoreboardSnapshot(Session.SubRule, label);
            Presenter.AppendScoreboard(snapshot, env.BloodRoundChanges ?? env.ScoreChanges);
            bloodHistoryWritten = true;
        }
        Presenter.ApplyScores(env.ScoresAfter);
        if (env.IsMatchEnd) Manager.MarkAwaitingMatchEnd();
        if (env.IsLiuju) Presenter.PresentLiuju("流局");
        else Presenter.PresentHu(env);
    }

    public override bool CanCutTile(int tileId) {
        if (IsBloodBattle && SeatHasTag("self", tag => tag == "first_hu" || tag == "second_hu" || tag == "third_hu")) return false;
        return base.CanCutTile(tileId);
    }
}
