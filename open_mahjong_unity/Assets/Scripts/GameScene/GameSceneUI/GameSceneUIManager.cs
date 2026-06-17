using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class GameSceneUIManager : MonoBehaviour
{
    public static GameSceneUIManager Instance { get; private set; }

    [SerializeField] private RealtimeSpectatorIndicator realtimeSpectatorIndicator;
    public RealtimeSpectatorIndicator RealtimeSpectatorIndicator => realtimeSpectatorIndicator;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy() {
        if (Instance == this) {
            Instance = null;
        }
    }

    private void Start(){
        // ClearTemporaryPanels();
    }

    /// <summary>关闭计分板 UI（不清对局结算缓存，用于新开对局/观战初始化）。</summary>
    public void ClearScoreRecordUi() {
        ScoreHistoryPanel.Instance?.Close();
        if (GameCanvas.Instance != null) {
            GameCanvas.Instance.SetScoreRecordOpen(false);
        }
    }

    /// <summary>关闭计分板并清空对局结算缓存，用于退出对局/牌谱/观战及切换牌谱。</summary>
    public void ClearScoreRecordState() {
        ClearScoreRecordUi();
        NormalGameStateManager.Instance?.ClearScoreRecordSettlementCache();
    }

    /// <summary>
    /// 清空所有临时面板
    /// </summary>
    public void ClearTemporaryPanels(){
        EndResultPanel.Instance.ClearEndResultPanel(); // 清空和牌结算面板
        EndGamePanel.Instance.ClearEndGamePanel();       // 清空游戏结束面板
        SwitchSeatPanel.Instance.ClearSwitchSeatPanel(); // 清空换位面板
        EndLiujuPanel.Instance.ClearEndLiujuPanel();     // 清空流局面板
        PenaltyPanel.Instance.ClearPenaltyPanel(); // 清空罚符面板
        EndShuheWeiPanel.Instance.ClearEndShuheWeiPanel(); // 清空数和尾面板
        StartGamePanel.Instance.ClearStartGamePanel();   // 清空开始游戏面板
        GameRecordManager.Instance.HideGameRecord();     // 隐藏游戏牌谱面板
        ClearScoreRecordState();
        TipsBlock.Instance.HideTipsBlock(); // 隐藏提示面板
        TipsContainer.Instance.HideTips(); // 隐藏提示容器
        AutoAction.Instance.gameObject.SetActive(false); // 隐藏自动行为组件
        RecordSetting.Instance.gameObject.SetActive(false); // 隐藏牌谱设置组件
        RoundEndPresentation.Instance.StopActiveSequence();
        RoundEndPresentation.Instance.ResetSichuanEndgameQueue();
    }

    /// <summary>
    /// 显示和牌结算结果
    /// </summary>
    public void ShowEndResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null) {
        EndResultPanel.Instance.StartShowResult(hepai_player_index, player_to_score, hu_score, hu_fan, hu_class, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask, base_fu, fu_fan_list, riichiExtras);
    }

    /// <summary>
    /// 牌谱回放结算展示（和牌）。观战模式下不显示确认按钮，由 end tick 驱动进入下一局。
    /// </summary>
    public void ShowRecordResult(int hepai_player_index, int hu_score, string[] hu_fan, string hu_class, string roomType,
        Dictionary<int, string> indexToPosition, Dictionary<string, string> positionToUsername,
        int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask,
        Dictionary<int, int> player_to_score_before, Dictionary<int, int> player_to_score_after, bool isSpectator = false,
        int? base_fu = null, string[] fu_fan_list = null, RiichiEndResultExtras riichiExtras = null) {
        EndResultPanel.Instance.StartRecordResult(hepai_player_index, hu_score, hu_fan, hu_class, roomType,
            indexToPosition, positionToUsername, hepai_player_hand, hepai_player_huapai, hepai_player_combination_mask,
            player_to_score_before, player_to_score_after, isSpectator, base_fu, fu_fan_list, riichiExtras);
    }

    /// <summary>
    /// 显示流局面板
    /// </summary>
    public void ShowEndLiuju(string displayText = "流局") {
        EndLiujuPanel.Instance.ShowLiujuPanel(displayText);
    }

    /// <summary>
    /// 显示罚符面板（如荒牌不听罚符等）。
    /// </summary>
    public void ShowPenalty(
        System.Collections.Generic.Dictionary<string, string> usernameByPos,
        System.Collections.Generic.Dictionary<string, int> scoreByPos,
        System.Collections.Generic.Dictionary<string, int> deltaByPos) {
        PenaltyPanel.Instance.ShowPenaltyPanel(usernameByPos, scoreByPos, deltaByPos);
    }

    /// <summary>
    /// 显示数和尾结算面板
    /// </summary>
    public void ShowShuhewei(Dictionary<int, int> player_fu, Dictionary<int, int> player_to_score, Dictionary<int, int> score_changes, Dictionary<int, string[]> player_fan, Dictionary<int, string[]> player_fu_types, Dictionary<int, string> indexToPosition, Dictionary<string, PlayerInfoClass> player_to_info) {
        EndShuheWeiPanel.Instance.ShowShuhewei(player_fu, player_to_score, score_changes, player_fan, player_fu_types, indexToPosition, player_to_info, false);
    }

    /// <summary>
    /// 显示换位面板
    /// </summary>
    public void ShowSwitchSeat(int current_round)
    {
        StartCoroutine(SwitchSeatPanel.Instance.ShowSwitchSeatPanel(current_round));
    }

    /// <summary>
    /// 显示游戏结束面板
    /// </summary>
    public void ShowEndGame(string master_seed, string commitment, string salt, Dictionary<string, Dictionary<string, object>> player_final_data)
    {
        EndGamePanel.Instance.ShowGameEndPanel(master_seed, commitment, salt, player_final_data);
    }

    /// <summary>
    /// 更新分数记录显示：牌谱模式由 GameRecordManager 传入数据，对局模式由 NormalGameStateManager 传入 player_to_info。
    /// </summary>
    public void UpdateScoreRecord()
    {
        if (ScoreHistoryPanel.Instance == null) return;

        var mgr = NormalGameStateManager.Instance;
        var player_to_info = mgr?.player_to_info;
        bool recordActive = GameRecordManager.Instance != null
            && GameRecordManager.Instance.gameObject.activeSelf
            && GameRecordManager.Instance.gameRecord != null;

        if (recordActive) {
            GameRecordManager.Instance.RefreshRecordScoreTable();
            return;
        }

        if (player_to_info != null && player_to_info.Count >= 4
            && mgr != null && (mgr.IsGameActive || mgr.roundSettlementHistory.Count > 0)) {
            string rule = !string.IsNullOrEmpty(mgr.roomRule) ? mgr.roomRule : "UNKNOWN";
            bool maskPlayerNames = StreamerModeHelper.IsEnabled && !mgr.IsRealtimeSpectator;
            ScoreHistoryPanel.Instance.UpdateScoreRecord(rule, player_to_info, mgr.roundSettlementHistory, maskPlayerNames: maskPlayerNames);
            return;
        }

        if (player_to_info == null || player_to_info.Count < 4) return;

        string fallbackRule = mgr != null ? mgr.roomRule : "UNKNOWN";
        var settlements = mgr?.roundSettlementHistory;
        bool maskNames = StreamerModeHelper.IsEnabled && mgr != null && !mgr.IsRealtimeSpectator;
        ScoreHistoryPanel.Instance.UpdateScoreRecord(fallbackRule, player_to_info, settlements, maskPlayerNames: maskNames);
    }

    /// <summary>
    /// 初始化游戏开始（清空临时面板并显示自动行为组件）
    /// </summary>
    public void InitGameStart() {
        EndResultPanel.Instance.ClearEndResultPanel(); // 清空和牌结算面板
        EndGamePanel.Instance.ClearEndGamePanel();       // 清空游戏结束面板
        SwitchSeatPanel.Instance.ClearSwitchSeatPanel(); // 清空换位面板
        EndLiujuPanel.Instance.ClearEndLiujuPanel();     // 清空流局面板
        PenaltyPanel.Instance.ClearPenaltyPanel(); // 清空罚符面板
        EndShuheWeiPanel.Instance.ClearEndShuheWeiPanel(); // 清空数和尾面板
        StartGamePanel.Instance.ClearStartGamePanel();   // 清空开始游戏面板
        GameRecordManager.Instance.HideGameRecord();     // 隐藏游戏牌谱面板
        ClearScoreRecordUi();
        TipsBlock.Instance.HideTipsBlock(); // 隐藏提示面板
        TipsContainer.Instance.HideTips(); // 隐藏提示容器
        AutoAction.Instance.gameObject.SetActive(true);
        AutoAction.Instance.Initialize(); // 初始化自动行为组件
        RecordSetting.Instance.gameObject.SetActive(false);
        if (ExitButtonManager.Instance != null) ExitButtonManager.Instance.HideAll(); // 正常对局隐藏退出牌谱/退出观战按钮
        if (realtimeSpectatorIndicator != null) realtimeSpectatorIndicator.ResetForNewGame(); // 重置被实时观战指示器，主动询问一次
        RoundEndPresentation.Instance.ShowSelfGameplayControlAndResyncHand3D();
    }

    /// <summary>实时观战进入对局：清空临时面板，仅显示自动排列手牌。</summary>
    public void InitRealtimeSpectatorStart() {
        EndResultPanel.Instance.ClearEndResultPanel();
        EndGamePanel.Instance.ClearEndGamePanel();
        SwitchSeatPanel.Instance.ClearSwitchSeatPanel();
        EndLiujuPanel.Instance.ClearEndLiujuPanel();
        PenaltyPanel.Instance.ClearPenaltyPanel();
        EndShuheWeiPanel.Instance.ClearEndShuheWeiPanel();
        StartGamePanel.Instance.ClearStartGamePanel();
        GameRecordManager.Instance.HideGameRecord();
        ClearScoreRecordUi();
        TipsBlock.Instance.HideTipsBlock();
        TipsContainer.Instance.HideTips();
        AutoAction.Instance.InitializeForSpectator();
        RecordSetting.Instance.gameObject.SetActive(false);
        if (ExitButtonManager.Instance != null) ExitButtonManager.Instance.ShowForRealtimeSpectator();
        RoundEndPresentation.Instance.ShowSelfGameplayControlAndResyncHand3D();
    }

    public void InitGameRecord() {
        EndResultPanel.Instance.ClearEndResultPanel(); // 清空和牌结算面板
        EndGamePanel.Instance.ClearEndGamePanel();       // 清空游戏结束面板
        SwitchSeatPanel.Instance.ClearSwitchSeatPanel(); // 清空换位面板
        EndLiujuPanel.Instance.ClearEndLiujuPanel();     // 清空流局面板
        PenaltyPanel.Instance.ClearPenaltyPanel(); // 清空罚符面板
        EndShuheWeiPanel.Instance.ClearEndShuheWeiPanel(); // 清空数和尾面板
        StartGamePanel.Instance.ClearStartGamePanel();   // 清空开始游戏面板
        GameRecordManager.Instance.HideGameRecord();     // 隐藏游戏牌谱面板
        ClearScoreRecordState();
        TipsBlock.Instance.HideTipsBlock(); // 隐藏提示面板
        TipsContainer.Instance.HideTips(); // 隐藏提示容器
        AutoAction.Instance.gameObject.SetActive(false); // 隐藏自动行为组件
        RecordSetting.Instance.gameObject.SetActive(true);
        RecordSetting.Instance.Initialize();
        GameRecordManager.Instance.gameObject.SetActive(true); // 显示牌谱组件
        RoundEndPresentation.Instance.ShowSelfGameplayControlAndResyncHand3D();
    }
}
