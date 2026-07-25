using UnityEngine;

public partial class NormalGameStateManager {
    // 切换玩家状态
    public void SwitchCurrentPlayer(string GetCardPlayer, string SwitchType, int remaining_time, int askHandPlayerIndex = -1, bool isTacticalRecheck = false) {

        // 询问手牌操作
        if (SwitchType == "askHandAction"){
            // 仅行动者换人时收拢：首次 ask、同玩家连补花后的再次 ask 均不收拢，保留摸牌区以区分手切/摸切
            bool shouldConsolidateHands = lastAskHandPlayerIndex >= 0 && askHandPlayerIndex != lastAskHandPlayerIndex;
            if (shouldConsolidateHands) {
                // 3D：跳过当前行动者，避免刚摸牌的家被收拢进主列（2D 侧轮到自己本就不 ReSetHandCards）
                Game3DManager.Instance.CheckAndRearrangeAllPlayersHandCards(GetCardPlayer);
            }
            // 如果行动者是自己
            if (GetCardPlayer == "self"){
                // 清空操作按钮 *有时候补花轮自己不补花，但是别人也不补，就出现两次按钮
                GameCanvas.Instance.ClearActionButton();
                // 立直锁手 / 食替禁切：每次询问立刻刷新自家手牌的可点状态与变灰显示
                AutoAction.Instance.SetAutoCutLocked(IsSelfRiichi());
                GameCanvas.Instance.RefreshHandTileSelectability();

                // 全量自动（自摸/起手胡/补花）：不出按钮，仅延迟发网，避免闪按钮泄密
                if (TryResolveImmediateAutoHand(out string autoHandAction, out float autoHandDelay)) {
                    StartDelayedAutoChoose(autoHandAction, autoHandDelay);
                }
                else {
                    GameCanvas.Instance.SetActionButton(allowActionList);
                    GameCanvas.Instance.LoadingRemianTime(remaining_time, roomStepTime);
                    if (ShouldStartAutoCut()) {
                        StartWaitAutoCut();
                    }
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
            ApplyCurrentPlayerIndicator(GetCardPlayer);
        }

        // 询问鸣牌操作 鸣牌操作的操作方一定是"self"
        else if (SwitchType == "askMingPaiAction"){
            GameCanvas.Instance.ClearActionButton();
            // 全量自动（牌张跳过 / 自动和 / 筛光后 pass）：不出按钮，避免闪按钮泄密
            // pass 立即发网；自动和保留短延迟。半自动仍显示服务端全集按钮，不做 UI 过滤
            if (TryResolveFullAutoMingPai(out string autoMingPaiAction, out float autoMingPaiDelay)) {
                StartDelayedAutoChoose(autoMingPaiAction, autoMingPaiDelay);
            }
            else {
                GameCanvas.Instance.SetActionButton(allowActionList);
                // 战术鸣牌打断窗口：remaining_time 即为 grace 秒数，不再叠加步时（避免显示 5+5）
                GameCanvas.Instance.LoadingRemianTime(remaining_time, isTacticalRecheck ? 0 : roomStepTime);
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
            if (GetCardPlayer == "self"){
                ClearQiangGangAskState();
                // 停止计时器
                GameCanvas.Instance.StopTimeRunning();
                // 清空允许操作列表
                allowActionList.Clear();
                // 清空按钮
                GameCanvas.Instance.ClearActionButton();
                // 切牌后退出立直选牌模式（超时被迫切牌时同样会走到这里），并清空食替禁切
                RiichiCutSelectionController.Instance.ExitRiichiCutMode();
                selfRiichiCandidateCuts.Clear();
                selfForbiddenCutTiles.Clear();
                selfForcedCutTiles.Clear();
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
            selfForcedCutTiles.Clear();
            RiichiCutSelectionController.Instance.ExitRiichiCutMode();
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
            RiichiCutSelectionController.Instance.ExitRiichiCutMode();
            selfForcedCutTiles.Clear();
            IsSelfActionRequired = false;
            GameSceneMouseInputController.Instance.SetActionInputPhase(GameSceneMouseInputController.InputPhaseNone);
        }
    }

    void ApplyCurrentPlayerIndicator(string player) {
        BoardCanvas.Instance.ShowCurrentPlayer(player, remainTiles);
        CurrentPlayer = player;
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
