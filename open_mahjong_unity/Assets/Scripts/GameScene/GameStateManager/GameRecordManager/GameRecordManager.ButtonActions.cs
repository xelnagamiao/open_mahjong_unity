using System.Collections.Generic;
using UnityEngine;

public partial class GameRecordManager {
    public void NextXunmu() {
        if (xunmuNodeList.Count == 0) return;
        int targetNode = -1;
        for (int i = 0; i < xunmuNodeList.Count; i++) {
            int node = xunmuNodeList[i];
            if (node > currentNode) {
                targetNode = node;
                break;
            }
        }
        if (targetNode >= 0) {
            GotoSelectNode(targetNode);
            return;
        }

        // 已在最后一巡，查找终局动作（和牌/流局）并跳转执行
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData) ||
            roundData.actionTicks == null) {
            return;
        }
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
                GotoSelectNode(terminalNode);
            }
            NextStep();
        }
    }

    public void BackXunmu() {
        if (xunmuNodeList.Count == 0) return;
        int targetNode = -1;
        for (int i = xunmuNodeList.Count - 1; i >= 0; i--) {
            int node = xunmuNodeList[i];
            if (node < currentNode) {
                targetNode = node;
                break;
            }
        }
        if (targetNode < 0) return;
        GotoSelectNode(targetNode);
    }

    public void NextStep() {
        NextAction();
    }

    public void BackStep() {
        GotoAction(currentNode - 1);
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
}
