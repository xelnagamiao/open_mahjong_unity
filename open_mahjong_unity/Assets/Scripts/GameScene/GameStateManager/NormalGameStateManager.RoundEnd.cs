using System.Collections.Generic;
using UnityEngine;

public partial class NormalGameStateManager {
    // 回合结束 和牌 流局
    public void ShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null, Dictionary<int, int> score_changes = null, bool isSilent = false, GuobiaoEndResultExtras guobiaoExtras = null, string liuju_step = null, Dictionary<int, string> liuju_status = null, Dictionary<int, int[]> liuju_hands = null, bool liuju_status_final = false, int? hepai_tile = null, bool? multi_ron = null, bool? suppress_hand_reveal = null, Dictionary<int, int[]> liuju_hu_hands = null, bool? defer_score_settlement = null, int? cha_payer_index = null, int? ron_discarder_index = null, bool? recycle_discard = null, Dictionary<int, int> gang_refund_changes = null, bool? is_qianggang = null, bool liuju_refund = false) {
        lastGuobiaoEndExtras = guobiaoExtras;
        // 重置自身命令
        SwitchCurrentPlayer("None","ClearAction",0);
        // 隐藏和牌提示
        TipsBlock.Instance.HideTipsBlock();
        TipsContainer.Instance.HideTips();
        TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
        if (riichiExtras != null && IsHuClass(hu_class)) {
            OnRiichiSticksCollected(riichiExtras.RiichiSticksCollected);
        } else if (subRule != null && subRule.StartsWith("riichi/") && ShouldHideRiichiSticksOnLiuju(hu_class)) {
            OnRiichiSticksHideOnLiuju();
        }

        bool deferScore = defer_score_settlement == true;
        bool isMidGameSichuanHu = deferScore && IsSichuanRule() && IsHuClass(hu_class);
        bool isSichuanEndgameScoreStep = IsSichuanRule() && IsSichuanEndgameScoreStep(liuju_step);
        ApplySichuanGangRefundIfAny(gang_refund_changes, liuju_step);
        if (isSichuanEndgameScoreStep) {
            if (liuju_step == "reveal_hu") {
                BeginSichuanEndgameScoreAccum();
            } else if (liuju_step == "settle_hu") {
                AccumulateSichuanEndgameScore(score_changes);
                RecordSichuanEndgameHu();
            } else if (liuju_step == "chajiao") {
                MarkSichuanEndgameChajiaoStep();
                AccumulateSichuanEndgameScore(score_changes);
            }
            if (liuju_status_final && TryFlushSichuanEndgameScoreToHistory()) {
                GameSceneUIManager.Instance.UpdateScoreRecord();
            }
        } else if (!isMidGameSichuanHu) {
            AppendRoundSettlementSnapshot(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_combination_mask, base_fu, fu_fan_list, riichiExtras, score_changes);
            GameSceneUIManager.Instance.UpdateScoreRecord();
        }

        if (hu_class == "liuju") {
            if (IsSichuanRule() && liuju_step == "reveal_hu" && liuju_hu_hands != null && liuju_hu_hands.Count > 0) {
                RoundEndPresentation.Instance.ResetSichuanEndgameQueue();
                RoundEndPresentation.Instance.EnqueueSichuanRevealHu(liuju_hu_hands);
            } else if (IsSichuanRule() && liuju_step == "chajiao" && liuju_status != null) {
                int focusIndex = hepai_player_index;
                string statusKey = "no_ting";
                int[] hand = null;
                if (liuju_status.TryGetValue(focusIndex, out string st)) {
                    statusKey = st;
                }
                if (liuju_hands != null && liuju_hands.TryGetValue(focusIndex, out int[] focusedHand)) {
                    hand = focusedHand;
                }
                RoundEndPresentation.Instance.EnqueueSichuanChajiao(
                    focusIndex, statusKey, hand, hepai_player_combination_mask, player_to_score, score_changes, liuju_status_final, liuju_refund);
                if (player_to_score != null) {
                    ApplyShowResultScores(player_to_score);
                }
            } else if (IsSichuanRule() && liuju_step == "cha_refund") {
                if (player_to_score != null) {
                    ApplyShowResultScores(player_to_score);
                }
                if (GameCanvas.HasNonZeroGangScoreChanges(score_changes)) {
                    GameCanvas.Instance.ShowGangScoreFloats(score_changes);
                }
                RoundEndPresentation.Instance.EnqueueSichuanChaRefund(
                    player_to_score, score_changes, liuju_status_final);
            } else if (IsSichuanRule() && liuju_step == "final") {
                if (player_to_score != null) {
                    ApplyShowResultScores(player_to_score);
                }
            } else if (IsSichuanRule() && string.IsNullOrEmpty(liuju_step)) {
                // 兼容旧版单次流局包
                RoundEndPresentation.Instance.PresentLiuju("流局");
            } else if (!IsSichuanRule()) {
                RoundEndPresentation.Instance.PresentLiuju("流局");
            }
        } else if (hu_class == "ryuukyoku") {
            foreach (var kvp in indexToPosition) {
                int idx = kvp.Key;
                string pos = kvp.Value;
                if (player_to_score != null && player_to_score.ContainsKey(idx) && player_to_info.ContainsKey(pos)) {
                    player_to_info[pos].score = player_to_score[idx];
                }
            }
            BoardCanvas.Instance.UpdatePlayerScores(player_to_score, indexToPosition);
            RoundEndPresentation.Instance.PresentDrawWallLiujuAndPenalty("荒牌流局", player_to_score, riichiExtras);
        } else if (hu_class == "jiuzhongjiupai" || IsRiichiSpecialLiujuHuClass(hu_class)) {
            ApplyShowResultScores(player_to_score);
            RoundEndPresentation.Instance.PresentLiuju(GetSpecialLiujuCaption(hu_class, roomRule));
        } else {
            if (IsSichuanRule() && liuju_step == "settle_hu") {
                RoundEndPresentation.Instance.EnqueueSichuanSettleHu(
                    hepai_player_index, player_to_score, hu_score, hu_fan, hu_class,
                    hepai_player_hand, hepai_player_combination_mask, score_changes, liuju_status_final);
                if (player_to_score != null) {
                    ApplyShowResultScores(player_to_score);
                }
            } else {
            if (!IsSichuanRule() || liuju_step != "settle_hu") {
                MarkPendingCuoheContinue(hepai_player_index, hu_fan);
            }
            bool suppressHand = suppress_hand_reveal == true;
            bool recycleDiscard = recycle_discard == true;
            if (!recycle_discard.HasValue && deferScore && IsHuClass(hu_class) && hu_class != "hu_self") {
                recycleDiscard = multi_ron != true;
            }
            bool isQianggang = is_qianggang == true || ContainsSichuanQianggangFan(hu_fan);
            RoundEndPresentation.Instance.PresentHuResultSequence(
                hepai_player_index, player_to_score, hu_score, hu_fan, hu_class,
                hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask,
                base_fu, fu_fan_list, riichiExtras, score_changes, isSilent,
                playPresentationEffects: true,
                suppressHandReveal: suppressHand,
                hepaiTile: hepai_tile ?? 0,
                multiRon: multi_ron == true,
                deferScoreSettlement: deferScore,
                ronDiscarderIndex: ron_discarder_index,
                recycleDiscard: recycleDiscard,
                isQianggang: isQianggang,
                endgameScoreOnly: false);
            }
        }
    }

    private void AppendRoundSettlementSnapshot(
        int hepai_player_index,
        Dictionary<int, int> player_to_score,
        int hu_score,
        string[] hu_fan,
        string hu_class,
        int[] hepai_player_hand,
        int[][] hepai_player_combination_mask,
        int? base_fu,
        string[] fu_fan_list,
        RiichiEndResultExtras riichiExtras,
        Dictionary<int, int> serverScoreChanges) {
        string winnerUsername = "";
        if (hepai_player_index >= 0 && indexToPosition.TryGetValue(hepai_player_index, out string huPos)
            && player_to_info.TryGetValue(huPos, out PlayerInfoClass winnerInfo)) {
            winnerUsername = winnerInfo.username;
        }

        Dictionary<int, int> scoreChanges = serverScoreChanges;
        if (scoreChanges == null || scoreChanges.Count == 0) {
            scoreChanges = riichiExtras?.ScoreChanges;
        }
        if (scoreChanges == null || scoreChanges.Count == 0) {
            scoreChanges = BuildScoreChangesFromPlayerToScore(player_to_score);
        }

        if (IsSichuanRule()) {
            AppendSichuanSimpleScoreboardSnapshot(hu_class, scoreChanges);
            return;
        }

        var snapshot = ScoreHistorySettlementHelper.CreateFromShowResult(
            subRule, hu_class, hepai_player_index, winnerUsername, hu_score, hu_fan,
            hepai_player_hand, hepai_player_combination_mask, base_fu, fu_fan_list,
            riichiExtras, scoreChanges);
        roundSettlementHistory.Add(snapshot);
        ApplyLocalScoreHistoryFromSettlement(snapshot, scoreChanges);
    }

    private Dictionary<int, int> BuildScoreChangesFromPlayerToScore(Dictionary<int, int> playerToScoreAfter) {
        if (playerToScoreAfter == null) return null;
        var changes = new Dictionary<int, int>();
        foreach (var kvp in indexToPosition) {
            int seatIdx = kvp.Key;
            string pos = kvp.Value;
            if (!player_to_info.TryGetValue(pos, out PlayerInfoClass info)) continue;
            if (ShowResultPlayerScoreResolver.TryGetAfterScore(playerToScoreAfter, seatIdx, info.original_player_index, out int afterScore)) {
                changes[seatIdx] = afterScore - info.score;
            }
        }
        return changes;
    }

    private void ApplyLocalScoreHistoryFromSettlement(RoundSettlementSnapshot snapshot, Dictionary<int, int> scoreChanges) {
        if (scoreChanges == null || scoreChanges.Count == 0) {
            if (!snapshot.isLiuju) return;
            scoreChanges = new Dictionary<int, int>();
            foreach (var kvp in indexToPosition) {
                scoreChanges[kvp.Key] = 0;
            }
        }

        foreach (var kvp in indexToPosition) {
            int seatIdx = kvp.Key;
            string pos = kvp.Value;
            if (!player_to_info.TryGetValue(pos, out PlayerInfoClass info)) continue;
            int delta = 0;
            if (ShowResultPlayerScoreResolver.TryGetDelta(scoreChanges, seatIdx, info.original_player_index, out int resolvedDelta)) {
                delta = resolvedDelta;
            }
            info.score_history ??= new List<string>();
            info.score_history.Add(FormatLocalScoreChange(delta));
        }
        foreach (var info in player_to_info.Values) {
            info.round_number_history ??= new List<int>();
            info.round_number_history.Add(currentRound);
            ScoreHistorySettlementHelper.AlignRoundNumberHistory(info.score_history, info.round_number_history);
        }
    }

    private static string FormatLocalScoreChange(int delta) {
        if (delta > 0) return "+" + delta;
        if (delta < 0) return delta.ToString();
        return "0";
    }

    private void ApplyShowResultScores(Dictionary<int, int> player_to_score) {
        if (player_to_score == null) {
            return;
        }
        foreach (var kvp in indexToPosition) {
            int idx = kvp.Key;
            string pos = kvp.Value;
            if (player_to_score.ContainsKey(idx) && player_to_info.ContainsKey(pos)) {
                player_to_info[pos].score = player_to_score[idx];
            }
        }
        BoardCanvas.Instance.UpdatePlayerScores(player_to_score, indexToPosition);
    }

    private static bool ContainsSichuanQianggangFan(string[] huFan) {
        if (huFan == null) return false;
        for (int i = 0; i < huFan.Length; i++) {
            if (huFan[i] == "抢杠") return true;
        }
        return false;
    }

    /// <summary>血战中途和牌/抢杠：杠上炮等即时退回刮风下雨分并飘字（流局 cha_refund 由面板分支单独处理）。</summary>
    private void ApplySichuanGangRefundIfAny(Dictionary<int, int> gangRefundChanges, string liujuStep) {
        if (!IsSichuanRule() || liujuStep == "cha_refund") return;
        if (!GameCanvas.HasNonZeroGangScoreChanges(gangRefundChanges)) return;
        ApplyGangScoreDeltas(gangRefundChanges);
        GameCanvas.Instance.ShowGangScoreFloats(gangRefundChanges);
    }

    public static bool IsRiichiSpecialLiujuHuClass(string hu_class) {
        return hu_class == "four_kan_abort"
            || hu_class == "four_wind_abort"
            || hu_class == "four_riichi_abort"
            || hu_class == "three_ron_abort";
    }

    /// <summary>九种九牌（日麻）/ 九老峰回（古典）流局名称。</summary>
    public static string GetJiuzhongjiupaiCaption(string roomRule) {
        return roomRule == "riichi" ? "九种九牌" : "九老峰回";
    }

    public static string GetSpecialLiujuCaption(string hu_class, string roomRule) {
        if (hu_class == "jiuzhongjiupai") {
            return GetJiuzhongjiupaiCaption(roomRule);
        }
        return GetRiichiSpecialLiujuCaption(hu_class);
    }

    public static string GetRiichiSpecialLiujuCaption(string hu_class) {
        return hu_class switch {
            "four_wind_abort" => "四风连打",
            "four_kan_abort" => "四杠散了",
            "four_riichi_abort" => "四人立直",
            "three_ron_abort" => "三家和流局",
            _ => "流局",
        };
    }

    // 数和尾结算
    public void ShowShuhewei(
        Dictionary<int, int> player_fu,
        Dictionary<int, int> player_to_score,
        Dictionary<int, int> score_changes,
        Dictionary<int, string[]> player_fan,
        Dictionary<int, string[]> player_fu_types,
        string hu_class,
        int? hepai_player_index,
        int[] hepai_player_hand = null,
        int[][] hepai_player_combination_mask = null
    ) {
        EndResultPanel.Instance.ClearEndResultPanel();
        if (!string.IsNullOrEmpty(hu_class) && hu_class != "liuju" && hu_class != "jiuzhongjiupai" && hepai_player_index.HasValue && indexToPosition.ContainsKey(hepai_player_index.Value)) {
            string huPos = indexToPosition[hepai_player_index.Value];
            GameCanvas.Instance.ShowActionDisplay(huPos, hu_class);
            SoundManager.Instance.PlayActionSound(huPos, hu_class);
        }
        // 更新计分板
        BoardCanvas.Instance.UpdatePlayerScores(player_to_score, indexToPosition);
        // 更新本地分数记录
        foreach (var kvp in player_to_score) {
            string pos = indexToPosition.ContainsKey(kvp.Key) ? indexToPosition[kvp.Key] : null;
            if (pos != null && player_to_info.ContainsKey(pos)) {
                player_to_info[pos].score = kvp.Value;
            }
        }
        RoundEndPresentation.Instance.PresentShuhewei(player_fu, player_to_score, score_changes, player_fan, player_fu_types, indexToPosition, player_to_info, hepai_player_index, hepai_player_hand, hepai_player_combination_mask);
        // 古典和牌仅 show_shuhewei：追加一行。流局先 show_result 再 shuhewei：更新最后一行。
        bool shuheweiUpdatesExistingRow = roundSettlementHistory.Count > 0 && hu_class == "liuju";
        if (shuheweiUpdatesExistingRow) {
            ScoreHistorySettlementHelper.UpdateLastFromShuhewei(
                roundSettlementHistory, hepai_player_index, player_fan, score_changes,
                hepai_player_hand, hepai_player_combination_mask, hu_class);
        } else {
            string winnerUsername = "";
            string[] huFan = null;
            string[] fuFanList = null;
            if (hepai_player_index.HasValue && indexToPosition.TryGetValue(hepai_player_index.Value, out string huPos)
                && player_to_info.TryGetValue(huPos, out PlayerInfoClass winnerInfo)) {
                winnerUsername = winnerInfo.username;
            }
            if (hepai_player_index.HasValue && player_fan != null) {
                player_fan.TryGetValue(hepai_player_index.Value, out huFan);
            }
            if (hepai_player_index.HasValue && player_fu_types != null) {
                player_fu_types.TryGetValue(hepai_player_index.Value, out fuFanList);
            }
            var snapshot = ScoreHistorySettlementHelper.CreateFromShuhewei(
                subRule, hu_class, hepai_player_index, winnerUsername, huFan, fuFanList,
                score_changes, hepai_player_hand, hepai_player_combination_mask);
            roundSettlementHistory.Add(snapshot);
            ApplyLocalScoreHistoryFromSettlement(snapshot, score_changes);
        }
        GameSceneUIManager.Instance.UpdateScoreRecord();
    }

    // 执行换位
    public void HandleSwitchSeat(int current_round){
        if (maxRound > 0) {
            int minMaxRoundForSwitch = current_round switch {
                5 => 2,
                9 => 3,
                13 => 4,
                _ => 0
            };
            if (minMaxRoundForSwitch > 0 && maxRound < minMaxRoundForSwitch) return;
            if (current_round > maxRound * 4) return;
        }
        GameSceneUIManager.Instance.ShowSwitchSeat(current_round);
    }

    // 刷新玩家标签列表 更新掉线,陪打等状态
    public void RefreshPlayerTagList(Dictionary<int, string[]> player_to_tag_list){
        // 更新所有玩家的标签列表
        foreach (var kvp in player_to_tag_list){
            int player_index = kvp.Key;
            string[] tag_list = kvp.Value;
            
            // 根据 player_index 找到对应的玩家位置
            if (indexToPosition.ContainsKey(player_index)){
                string position = indexToPosition[player_index];
                if (player_to_info.ContainsKey(position)){
                    player_to_info[position].tag_list = tag_list;
                    Debug.Log($"更新玩家 {position} (索引 {player_index}) 的标签列表: {string.Join(", ", tag_list)}");
                }
            }
        }
        
        // 更新 GameCanvas 中的玩家面板显示
        GameCanvas.Instance.UpdatePlayerTagList(player_to_tag_list);
        TryResumeAfterCuoheContinue();
        TryResumeAfterSichuanContinue();
        if (IsSelfRiichi()) {
            TipsContainer.Instance.ResetRyuukyokuTenpaiChoiceForRound();
            TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
        }
    }

    private static bool IsHuClass(string huClass) {
        return huClass == "hu_self" || huClass == "hu_first" || huClass == "hu_second" || huClass == "hu_third";
    }

    // 游戏结束
    public void GameEnd(string master_seed, string commitment, string salt, Dictionary<string, Dictionary<string, object>> player_final_data){
        // 重置自身命令
        SwitchCurrentPlayer("None","ClearAction",0);
        IsGameActive = false;
        SwitchSeatPanel.Instance.ClearSwitchSeatPanel();
        IsSelfActionRequired = false;
        TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
        RoundEndPresentation.Instance.PresentEndGame(master_seed, commitment, salt, player_final_data);
    }
}
