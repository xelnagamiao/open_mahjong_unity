using System.Collections.Generic;
using UnityEngine;

public partial class GameRecordManager {
    public void NextXunmu() {
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData) ||
            roundData.actionTicks == null) {
            if (IsSpectatorSession) {
                NotifyReachedLastAction();
            }
            return;
        }

        if (xunmuNodeList.Count == 0) {
            if (IsSpectatorSession) {
                NotifyReachedLastAction();
            }
            return;
        }
        int targetNode = -1;
        for (int i = 0; i < xunmuNodeList.Count; i++) {
            int node = xunmuNodeList[i];
            if (node > currentNode) {
                targetNode = node;
                break;
            }
        }
        if (targetNode >= 0) {
            bool updateMode = !(IsSpectatorSession && CurrentMode == RecordManagerMode.Spectator);
            GotoSelectNode(targetNode, updateMode);
            return;
        }

        // 无下一巡时，优先在 hu_xxx / liuju 节点停顿一次，避免直接跨到 end
        int terminalPauseNode = FindNextTerminalPauseNode(roundData, currentNode);
        if (terminalPauseNode >= 0) {
            bool updateMode = !(IsSpectatorSession && CurrentMode == RecordManagerMode.Spectator);
            GotoSelectNode(terminalPauseNode, updateMode);
            NextStep();
            return;
        }

        // 已在最后一巡：若还没到本局最后节点，先跳到本局最后节点
        int lastNodeIndex = Mathf.Max(0, roundData.actionTicks.Count - 1);
        if (currentNode < lastNodeIndex) {
            bool updateMode = !(IsSpectatorSession && CurrentMode == RecordManagerMode.Spectator);
            GotoSelectNode(lastNodeIndex, updateMode);
            return;
        }

        // 已在最后节点前一位时，执行最后一步
        if (currentNode == lastNodeIndex) {
            NextStep();
            return;
        }

        // 已在最后一巡，查找终局动作（和牌/流局）并跳转执行
        int terminalNode = -1;
        for (int i = roundData.actionTicks.Count - 1; i >= 0; i--) {
            List<string> tick = roundData.actionTicks[i];
            if (tick == null || tick.Count == 0) continue;
            string action = tick[0];
            if (action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third" || action == "liuju") {
                terminalNode = i;
                break;
            }
        }
        if (terminalNode >= 0 && terminalNode >= currentNode) {
            if (terminalNode > currentNode) {
                bool updateMode = !(IsSpectatorSession && CurrentMode == RecordManagerMode.Spectator);
                GotoSelectNode(terminalNode, updateMode);
            }
            NextStep();
            return;
        }

        if (IsSpectatorSession) {
            NotifyReachedLastAction();
        }
    }

    public void BackXunmu() {
        if (IsSpectatorSession && CurrentMode == RecordManagerMode.Spectator) {
            SwitchToRecordMode();
        }
        if (xunmuNodeList.Count == 0) return;
        int targetNode = -1;
        for (int i = xunmuNodeList.Count - 1; i >= 0; i--) {
            int node = xunmuNodeList[i];
            if (node < currentNode) {
                targetNode = node;
                break;
            }
        }
        if (targetNode < 0) {
            // 在 0 巡继续向上：切到上一局最后一巡
            int prevRound = currentRoundIndex - 1;
            if (prevRound >= 1 && gameRecord.gameRound.rounds.ContainsKey(prevRound)) {
                GotoSelectRound(prevRound, true);
                int lastXunNode = xunmuNodeList.Count > 0 ? xunmuNodeList[xunmuNodeList.Count - 1] : 0;
                GotoSelectNode(lastXunNode);
            }
            return;
        }
        GotoSelectNode(targetNode);
    }

    public void NextStep() {
        if (IsSpectatorSession) {
            if (!CanAdvanceCurrentRound()) {
                NotifyReachedLastAction();
                return;
            }
        }
        NextAction();
        if (IsSpectatorSession && CurrentMode == RecordManagerMode.RecordOnSpectator) {
            RefreshSpectatorModeByNodePosition();
        }
    }

    public void BackStep() {
        if (IsSpectatorSession && CurrentMode == RecordManagerMode.Spectator) {
            SwitchToRecordMode();
        }
        GotoAction(currentNode - 1);
        if (IsSpectatorSession) RefreshSpectatorModeByNodePosition();
    }

    private void ShowGameRoundContent() {
        bool shouldOpenRound = !roundScrollView.gameObject.activeSelf;
        roundScrollView.gameObject.SetActive(shouldOpenRound);
        if (shouldOpenRound) {
            xunmuScrollView.gameObject.SetActive(false);
        }
    }

    private void ShowXunmuContent() {
        bool shouldOpenXunmu = !xunmuScrollView.gameObject.activeSelf;
        xunmuScrollView.gameObject.SetActive(shouldOpenXunmu);
        if (shouldOpenXunmu) {
            roundScrollView.gameObject.SetActive(false);
        }
    }

    private void ShowTileList() {
        bool shouldShow = !tileListView.activeSelf;
        if (shouldShow) {
            UpdateTileListOpacity();
            tileListView.SetActive(true);
        } else {
            tileListView.SetActive(false);
        }
    }

    private void ShowGameInfo() {
        if (IsSpectatorSession) return;
        bool shouldShow = !gameInfoView.activeSelf;
        if (shouldShow) {
            roundInfoView.SetActive(false);
            gameInfoText.text = BuildGameInfoString();
            gameInfoView.SetActive(true);
        } else {
            gameInfoView.SetActive(false);
        }
    }

    private void ShowRoundInfo() {
        bool shouldShow = !roundInfoView.activeSelf;
        if (shouldShow) {
            gameInfoView.SetActive(false);
            roundInfoText.text = BuildRoundInfoString();
            roundInfoView.SetActive(true);
        } else {
            roundInfoView.SetActive(false);
        }
    }

    private void QuitRecord() {
        Game3DManager.Instance.Clear3DTile();
        WindowsManager.Instance.SwitchWindow("menu");
    }

    private static readonly HashSet<string> XunmuPauseActionKeys = new HashSet<string> {
        "hu_self", "hu_first", "hu_second", "hu_third", "liuju"
    };

    private static bool IsTerminalPauseAction(string action) {
        return !string.IsNullOrEmpty(action) && XunmuPauseActionKeys.Contains(action);
    }

    private static int FindNextTerminalPauseNode(Round roundData, int currentNodeIndex) {
        if (roundData?.actionTicks == null) return -1;
        for (int i = currentNodeIndex + 1; i < roundData.actionTicks.Count; i++) {
            List<string> tick = roundData.actionTicks[i];
            if (tick == null || tick.Count == 0) continue;
            if (IsTerminalPauseAction(tick[0])) {
                return i;
            }
        }
        return -1;
    }
}
