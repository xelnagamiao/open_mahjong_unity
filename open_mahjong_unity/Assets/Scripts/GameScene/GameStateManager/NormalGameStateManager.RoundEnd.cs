using System.Collections.Generic;
using UnityEngine;

public partial class NormalGameStateManager {
    // 回合结束 和牌 流局
    public void ShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null, bool isSilent = false) {
        // 重置自身命令
        SwitchCurrentPlayer("None","ClearAction",0);
        // 隐藏和牌提示
        TipsBlock.Instance.HideTipsBlock();
        TipsContainer.Instance.HideTips();
        TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
        if (isSilent) {
            // 战术鸣牌：和牌字体动画/音效已在申请阶段播放，结算时直接关闭面板
            TacticalCallPanel.Instance.HidePanel();
        }
        if (riichiExtras != null && IsHuClass(hu_class)) {
            OnRiichiSticksCollected(riichiExtras.RiichiSticksCollected);
        }
        // 显示结算结果
        if (hu_class == "liuju") {
            RoundEndPresentation.Instance.PresentLiuju("流局");
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
            RoundEndPresentation.Instance.PresentLiuju(GetRiichiSpecialLiujuCaption(hu_class));
        } else {
            RoundEndPresentation.Instance.PresentHuResultSequence(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, base_fu, fu_fan_list, riichiExtras, isSilent);
        }
        // 更新分数记录
        GameSceneUIManager.Instance.UpdateScoreRecord();
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

    public static bool IsRiichiSpecialLiujuHuClass(string hu_class) {
        return hu_class == "four_kan_abort"
            || hu_class == "four_wind_abort"
            || hu_class == "four_riichi_abort"
            || hu_class == "three_ron_abort";
    }

    public static string GetRiichiSpecialLiujuCaption(string hu_class) {
        return hu_class switch {
            "jiuzhongjiupai" => "九老峰回",
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
        if (IsSelfRiichi()) {
            TipsContainer.Instance.ResetRyuukyokuTenpaiChoiceForRound();
            TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
        }
    }

    private static bool IsHuClass(string huClass) {
        return huClass == "hu_self" || huClass == "hu_first" || huClass == "hu_second" || huClass == "hu_third";
    }

    // 游戏结束
    public void GameEnd(long game_random_seed, Dictionary<string, Dictionary<string, object>> player_final_data){
        // 重置自身命令
        SwitchCurrentPlayer("None","ClearAction",0);
        IsGameActive = false;
        SwitchSeatPanel.Instance.ClearSwitchSeatPanel();
        IsSelfActionRequired = false;
        TipsContainer.Instance.HideRyuukyokuTenpaiChoice();
        RoundEndPresentation.Instance.PresentEndGame(game_random_seed, player_final_data);
    }
}
