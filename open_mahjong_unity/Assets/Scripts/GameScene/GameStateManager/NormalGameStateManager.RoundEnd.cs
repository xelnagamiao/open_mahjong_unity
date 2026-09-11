using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 结算 / 终局的通用部分：末局 match_end 的终局面板暂存、换位、标签刷新、game_end。
/// 结算呈现路径由各族 GameState 决定（TurnBasedGameState.PresentSettlement 及其重写），分数/计分板/演出调用在 <see cref="SettlementPresenter"/>。
/// </summary>
public partial class NormalGameStateManager {
    private string pendingGameEndMasterSeed;
    private string pendingGameEndCommitment;
    private string pendingGameEndSalt;
    private Dictionary<string, Dictionary<string, object>> pendingGameEndPlayerFinalData;
    private bool hasPendingGameEnd;
    /// <summary>本局结算为整场末局 match_end（可能尚在倒牌，结算板未激活）。</summary>
    private bool awaitingMatchEnd;

    private static SettlementPresenter Presenter => SettlementPresenter.Current;

    /// <summary>每条结算开始：清 match_end 等待标记（仅当本条结算会走到带「确定」的面板时由族再置位）。</summary>
    public void BeginSettlement() {
        awaitingMatchEnd = false;
    }

    /// <summary>本条结算是整场末局且会弹带「确定」的面板：game_end 到达时先暂存，等确定后再弹终局排名。</summary>
    public void MarkAwaitingMatchEnd() {
        awaitingMatchEnd = true;
    }

    /// <summary>
    /// 自定义规则适配器（虹雀）写入一条通用计分板结算；分数变化与主流程使用同一套快照/历史逻辑。
    /// </summary>
    public void AppendExternalRoundSettlementSnapshot(
        int hepaiPlayerIndex,
        Dictionary<int, int> playerToScore,
        int huScore,
        string[] huFan,
        string huClass,
        int[] hepaiPlayerHand,
        int[][] combinationMask,
        int? baseFu,
        Dictionary<int, int> scoreChanges) {
        Presenter.AppendScoreboard(new SettlementEnvelope {
            WinnerIndex = hepaiPlayerIndex,
            ScoresAfter = playerToScore,
            HuScore = huScore,
            FanLabels = huFan,
            HuClass = huClass,
            WinnerHand = hepaiPlayerHand,
            WinnerMelds = combinationMask,
            BaseFu = baseFu,
            ScoreChanges = scoreChanges,
        });
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
        // 族后置：续打恢复、锁手后收提示……
        RuleRegistry.ActiveGameState?.OnPlayerTagsRefreshed();
    }

    // 游戏结束
    public void GameEnd(string master_seed, string commitment, string salt, Dictionary<string, Dictionary<string, object>> player_final_data){
        GameSceneUIManager.ResetRealtimeSpectatorUi();
        // 重置自身命令
        TurnClock.Current.Clear("GameEnd");
        IsGameActive = false;
        SwitchSeatPanel.Instance.ClearSwitchSeatPanel();

        if (awaitingMatchEnd) {
            pendingGameEndMasterSeed = master_seed;
            pendingGameEndCommitment = commitment;
            pendingGameEndSalt = salt;
            pendingGameEndPlayerFinalData = player_final_data;
            hasPendingGameEnd = true;
            return;
        }

        ClearPendingGameEnd();
        if (EndResultPanel.Instance != null) {
            EndResultPanel.Instance.ClearEndResultPanel();
        }
        if (EndShuheWeiPanel.Instance != null) {
            EndShuheWeiPanel.Instance.ClearEndShuheWeiPanel();
        }
        RoundEndPresentation.Instance.PresentEndGame(master_seed, commitment, salt, player_final_data);
    }

    /// <summary>末局 match_end 点确定后：弹出暂存的终局排名面板。</summary>
    public void FlushPendingGameEnd() {
        awaitingMatchEnd = false;
        if (!hasPendingGameEnd) {
            return;
        }
        string masterSeed = pendingGameEndMasterSeed;
        string commitment = pendingGameEndCommitment;
        string salt = pendingGameEndSalt;
        var playerFinalData = pendingGameEndPlayerFinalData;
        ClearPendingGameEnd();
        RoundEndPresentation.Instance.PresentEndGame(masterSeed, commitment, salt, playerFinalData);
    }

    private void ClearPendingGameEnd() {
        hasPendingGameEnd = false;
        pendingGameEndMasterSeed = null;
        pendingGameEndCommitment = null;
        pendingGameEndSalt = null;
        pendingGameEndPlayerFinalData = null;
    }
}
