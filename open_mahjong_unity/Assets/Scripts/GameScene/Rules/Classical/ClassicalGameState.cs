using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 古典麻将。对应服务端 game_classical/ClassicalGameState。
/// 与标准骨架的差别：和牌/流局后追加"数和尾"结算（show_shuhewei）；九老峰回流局。
/// </summary>
public class ClassicalGameState : TurnBasedGameState {
    public const string RuleId = "classical";

    protected override bool HandleExtraMessage(string suffix, Response response) {
        if (suffix != "show_shuhewei") return false;
        Debug.Log($"收到数和尾结算消息: {response.show_shuhewei_info}");
        ShowShuheWeiInfo info = response.show_shuhewei_info;
        if (info != null) {
            ShowShuhewei(info);
        }
        return true;
    }

    protected override string GetSpecialLiujuCaption(string huClass) {
        return huClass == "jiuzhongjiupai" ? SpecialLiujuCaptions.JiuzhongjiupaiCaption(RuleId) : null;
    }

    /// <summary>数和尾结算：先关和牌面板，再走 EndShuheWeiPanel；古典和牌仅 show_shuhewei → 计分板追加一行；流局先 show_result 再 shuhewei → 更新最后一行。</summary>
    private void ShowShuhewei(ShowShuheWeiInfo info) {
        EndResultPanel.Instance.ClearEndResultPanel();
        if (info.next_status == "match_end") {
            Manager.MarkAwaitingMatchEnd();
        }
        if (EndShuheWeiPanel.Instance != null) {
            EndShuheWeiPanel.Instance.SetNextStatus(info.next_status);
        }
        string huClass = info.hu_class;
        int? winner = info.hepai_player_index;
        if (!string.IsNullOrEmpty(huClass) && huClass != "liuju" && huClass != "jiuzhongjiupai"
            && winner.HasValue && Mirror.IndexToPosition.TryGetValue(winner.Value, out string huSeat)) {
            GameCanvas.Instance.ShowActionDisplay(huSeat, huClass);
            SoundManager.Instance.PlayActionSound(huSeat, huClass);
        }
        Presenter.ApplyScores(info.player_to_score);
        RoundEndPresentation.Instance.PresentShuhewei(
            info.player_fu, info.player_to_score, info.score_changes, info.player_fan, info.player_fu_types,
            Mirror.IndexToPosition, Mirror.PlayerToInfo, winner, info.hepai_player_hand, info.hepai_player_combination_mask);

        List<RoundSettlementSnapshot> history = Mirror.RoundSettlementHistory;
        bool updatesExistingRow = history.Count > 0 && huClass == "liuju";
        if (updatesExistingRow) {
            ScoreHistorySettlementHelper.UpdateLastFromShuhewei(
                history, winner, info.player_fan, info.score_changes,
                info.hepai_player_hand, info.hepai_player_combination_mask, huClass);
            GameSceneUIManager.Instance.UpdateScoreRecord();
            return;
        }

        string winnerUsername = "";
        string[] huFan = null;
        string[] fuFanList = null;
        if (winner.HasValue) {
            PlayerInfoClass winnerInfo = Mirror.IndexToPosition.TryGetValue(winner.Value, out string seat) ? Mirror.Info(seat) : null;
            if (winnerInfo != null) winnerUsername = winnerInfo.username;
            info.player_fan?.TryGetValue(winner.Value, out huFan);
            info.player_fu_types?.TryGetValue(winner.Value, out fuFanList);
        }
        RoundSettlementSnapshot snapshot = ScoreHistorySettlementHelper.CreateFromShuhewei(
            Session.SubRule, huClass, winner, winnerUsername, huFan, fuFanList,
            info.score_changes, info.hepai_player_hand, info.hepai_player_combination_mask);
        Presenter.AppendScoreboard(snapshot, info.score_changes);
    }
}
