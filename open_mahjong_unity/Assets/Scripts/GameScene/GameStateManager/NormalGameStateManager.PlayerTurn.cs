using UnityEngine;

public partial class NormalGameStateManager {
    // 切换玩家状态
    public void SwitchCurrentPlayer(string GetCardPlayer, string SwitchType, int remaining_time, int askHandPlayerIndex = -1) {

        // 询问手牌操作
        if (SwitchType == "askHandAction"){
            // 仅行动者换人时收拢：首次 ask、同玩家连补花后的再次 ask 均不收拢，保留摸牌区以区分手切/摸切
            bool shouldConsolidateHands = lastAskHandPlayerIndex >= 0 && askHandPlayerIndex != lastAskHandPlayerIndex;
            if (shouldConsolidateHands) {
                Game3DManager.Instance.CheckAndRearrangeAllPlayersHandCards();
            }
            // 如果行动者是自己
            if (GetCardPlayer == "self"){
                // 显示可用行动 开启倒计时
                GameCanvas.Instance.ClearActionButton(); // 清空操作按钮 *有时候补花轮自己不补花，但是别人也不补，就出现两次按钮
                GameCanvas.Instance.SetActionButton(allowActionList);
                GameCanvas.Instance.LoadingRemianTime(remaining_time,roomStepTime);
                // 立直锁手 / 食替禁切：每次询问立刻刷新自家手牌的可点状态与变灰显示
                GameCanvas.Instance.RefreshHandTileSelectability();
                if (AutoAction.Instance != null) {
                    AutoAction.Instance.SetAutoCutLocked(IsSelfRiichi());
                }
                // 如果开启自动和牌/自摸、自动补花或者自动出牌，则启动协程
                if (AutoAction.Instance != null && (AutoAction.Instance.ShouldAutoWinTsumo() || AutoAction.Instance.IsAutoBuhua || AutoAction.Instance.IsAutoCut)){
                    StartWaitAutoAction("AutoHandAction");
                }
                // 询问操作时隐藏提示块（实时观战保持与切牌后一致的听牌提示）
                if (!IsRealtimeSpectator) {
                    TipsBlock.Instance.HideTipsBlock();
                    TipsContainer.Instance.HideTips();
                }
                IsSelfActionRequired = true;
                GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseAskHand);
            }
            // 询问的不是自己的回合
            else{
                if (shouldConsolidateHands) {
                    GameCanvas.Instance.ChangeHandCards("ReSetHandCards", 0, null, null);
                }
                SwitchCurrentPlayer(GetCardPlayer, "ClearAction", 0); // 重置自身命令
                IsSelfActionRequired = false;
            }
            lastAskHandPlayerIndex = askHandPlayerIndex;
            // 只有askHandAction才会转移玩家位置
            BoardCanvas.Instance.ShowCurrentPlayer(GetCardPlayer, remainTiles); // 显示当前玩家
            CurrentPlayer = GetCardPlayer; // 存储当前玩家
        }

        // 询问鸣牌操作 鸣牌操作的操作方一定是"self"
        else if (SwitchType == "askMingPaiAction"){
            GameCanvas.Instance.ClearActionButton();
            GameCanvas.Instance.SetActionButton(allowActionList);
            GameCanvas.Instance.LoadingRemianTime(remaining_time,roomStepTime);
            // 如果开启自动过牌、自动胡牌或牌张设置中的鸣牌选项，则启动协程
            if (AutoAction.Instance.IsAutoPass || AutoAction.Instance.IsAutoHepai || AutoAction.Instance.ShouldAutoPassForCurrentDiscard() || AutoAction.Instance.HasAnyTilePassMingPaiOption()){
                StartWaitAutoAction("AutoMingPaiAction");
            }
            IsSelfActionRequired = true;
            GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseAskOther);
        }

        // 执行行动
        else if (SwitchType == "doAction"){
            if (GetCardPlayer == "self") {
                CancelWaitAutoAction("doAction");
            }
            GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseNone);
            Debug.Log($"doAction行动者: {GetCardPlayer}");
            // 如果行动者是自己
            if (GetCardPlayer == "self"){
                ClearQiangGangAskState();
                // 停止计时器
                GameCanvas.Instance.StopTimeRunning();
                // 清空允许操作列表
                allowActionList.Clear();
                // 清空按钮
                GameCanvas.Instance.ClearActionButton();
                // 切牌后退出立直选牌模式（超时被迫切牌时同样会走到这里），并清空食替禁切
                if (RiichiCutSelectionController.Instance != null) RiichiCutSelectionController.Instance.ExitRiichiCutMode();
                selfRiichiCandidateCuts.Clear();
                selfForbiddenCutTiles.Clear();
                // 立刻恢复手牌正常颜色，避免用户看到禁切灰色滞留到下一轮询问
                GameCanvas.Instance.RefreshHandTileSelectability();
                // 在自己执行操作以后计算听牌提示，如果有提示就显示右侧提示块
                if (tips){
                    TipsBlock.Instance.ShowTipsBlock(selfHandTiles, player_to_info["self"].combination_tiles);
                }
                IsSelfActionRequired = false;
            }
        }

        // 选择行动
        else if (SwitchType == "ClearAction"){
            CancelWaitAutoAction("ClearAction");
            ClearQiangGangAskState();
            GameSceneMouseInputController.Instance.ClearStaleHandInput("ClearAction");
            // 停止计时器
            GameCanvas.Instance.StopTimeRunning();
            // 清空操作按钮
            GameCanvas.Instance.ClearActionButton();
            // 清空允许操作列表与立直/食替缓存
            allowActionList.Clear();
            selfRiichiCandidateCuts.Clear();
            selfForbiddenCutTiles.Clear();
            if (RiichiCutSelectionController.Instance != null) RiichiCutSelectionController.Instance.ExitRiichiCutMode();
            IsSelfActionRequired = false;
            GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseNone);
        }

        // 时间耗尽
        else if (SwitchType == "TimeOut"){
            CancelWaitAutoAction("TimeOut");
            ClearQiangGangAskState();
            GameSceneMouseInputController.Instance.ClearStaleHandInput("TimeOut");
            // 清空操作按钮
            GameCanvas.Instance.ClearActionButton();
            if (RiichiCutSelectionController.Instance != null) RiichiCutSelectionController.Instance.ExitRiichiCutMode();
            IsSelfActionRequired = false;
            GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseNone);
        }
    }

    public bool IsSelfRiichi(){
        string[] tags = player_to_info["self"].tag_list;
        if (tags == null) return false;
        for (int i = 0; i < tags.Length; i++){
            if (tags[i] == "riichi" || tags[i] == "daburu_riichi"){
                return true;
            }
        }
        return false;
    }
}
