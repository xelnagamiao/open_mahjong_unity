using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameRecordManager {
    private const float AutoPlayPollIntervalSeconds = 0.1f;

    private Button recordAutoPlayButton;
    private TMP_Text recordAutoPlayButtonText;
    private Coroutine recordAutoPlayCoroutine;
    private int _recordPlaybackGeneration;
    private int _pendingRecordDelayedAdvanceCount;

    public bool IsRecordAutoPlaying { get; private set; }

    /// <summary>复用“下一步”按钮样式，在控制栏末尾创建牌谱自动播放按钮。</summary>
    private void InitializeRecordAutoPlayButton() {
        if (nextStepButton == null || recordAutoPlayButton != null) return;

        GameObject buttonObject = Instantiate(
            nextStepButton.gameObject,
            nextStepButton.transform.parent,
            false
        );
        buttonObject.name = "AutoPlay";
        recordAutoPlayButton = buttonObject.GetComponent<Button>();
        recordAutoPlayButton.onClick.RemoveAllListeners();
        recordAutoPlayButton.onClick.AddListener(ToggleRecordAutoPlay);

        recordAutoPlayButtonText = buttonObject.GetComponentInChildren<TMP_Text>(true);
        RectTransform autoRect = buttonObject.transform as RectTransform;
        if (autoRect != null) {
            autoRect.sizeDelta = new Vector2(Mathf.Max(110f, autoRect.sizeDelta.x), autoRect.sizeDelta.y);
        }
        if (recordAutoPlayButtonText != null) {
            recordAutoPlayButtonText.fontSize = Mathf.Min(recordAutoPlayButtonText.fontSize, 22f);
        }

        buttonObject.SetActive(false);
        UpdateRecordAutoPlayButtonText();
        LayoutRecordControlButtons(includeAutoPlay: false);
    }

    private void LayoutRecordControlButtons(bool includeAutoPlay) {
        if (nextStepButton == null || nextStepButton.transform.parent is not RectTransform parentRect) return;

        var buttons = new List<Button> {
            showTileListButton,
            backXunmuButton,
            backStepButton,
            showGameRoundContentButton,
            showXunmuContentButton,
            nextStepButton,
            nextXunmuButton,
        };
        if (includeAutoPlay && recordAutoPlayButton != null) {
            buttons.Add(recordAutoPlayButton);
        }
        buttons.RemoveAll(button => button == null);
        if (buttons.Count == 0) return;

        float panelWidth = parentRect.rect.width > 0f ? parentRect.rect.width : parentRect.sizeDelta.x;
        float maxButtonWidth = 0f;
        foreach (Button button in buttons) {
            if (button.transform is RectTransform buttonRect) {
                maxButtonWidth = Mathf.Max(maxButtonWidth, buttonRect.rect.width, buttonRect.sizeDelta.x);
            }
        }

        float edgeInset = maxButtonWidth * 0.5f + 16f;
        float left = -panelWidth * 0.5f + edgeInset;
        float right = panelWidth * 0.5f - edgeInset;
        float step = buttons.Count > 1 ? (right - left) / (buttons.Count - 1) : 0f;
        for (int i = 0; i < buttons.Count; i++) {
            if (buttons[i].transform is not RectTransform buttonRect) continue;
            Vector2 position = buttonRect.anchoredPosition;
            position.x = buttons.Count > 1 ? left + step * i : 0f;
            buttonRect.anchoredPosition = position;
        }
    }

    private void ToggleRecordAutoPlay() {
        if (IsRecordAutoPlaying) {
            StopRecordAutoPlay();
            return;
        }
        if (gameRecord?.gameRound?.rounds == null || IsSpectating || CurrentMode != RecordManagerMode.Record) {
            return;
        }

        IsRecordAutoPlaying = true;
        UpdateRecordAutoPlayButtonText();
        recordAutoPlayCoroutine = StartCoroutine(RecordAutoPlayCoroutine());
    }

    private IEnumerator RecordAutoPlayCoroutine() {
        var pollWait = new WaitForSeconds(AutoPlayPollIntervalSeconds);

        while (IsRecordAutoPlaying) {
            if (gameRecord?.gameRound?.rounds == null || IsSpectating || CurrentMode != RecordManagerMode.Record) {
                CompleteRecordAutoPlay(showCompletedTip: false);
                yield break;
            }

            if (_recordHuPresentationActive || _pendingRecordDelayedAdvanceCount > 0) {
                yield return pollWait;
                continue;
            }

            if (EndResultPanel.Instance != null && EndResultPanel.Instance.IsAwaitingRecordResultConfirm) {
                // EndButtonClick 会在按钮尚不可用时安全返回；番种演出完毕后会自动确认并推进。
                EndResultPanel.Instance.EndButtonClick();
                yield return pollWait;
                continue;
            }

            if (EndShuheWeiPanel.Instance != null && EndShuheWeiPanel.Instance.IsAwaitingRecordResultConfirm) {
                EndShuheWeiPanel.Instance.TryConfirmRecordResult();
                yield return pollWait;
                continue;
            }

            if (BlocksRecordNavigation) {
                yield return pollWait;
                continue;
            }

            if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)
                || roundData.actionTicks == null) {
                CompleteRecordAutoPlay();
                yield break;
            }

            if (currentNode >= roundData.actionTicks.Count) {
                int nextRound = currentRoundIndex + 1;
                if (gameRecord.gameRound.rounds.ContainsKey(nextRound)) {
                    GotoSelectRound(nextRound, false);
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }
                CompleteRecordAutoPlay();
                yield break;
            }

            List<string> tick = roundData.actionTicks[currentNode];
            string action = tick != null && tick.Count > 0 ? tick[0] : "";
            int previousRound = currentRoundIndex;
            int previousNode = currentNode;
            NextStep();

            if (previousRound == currentRoundIndex && previousNode == currentNode) {
                yield return pollWait;
            } else {
                yield return new WaitForSeconds(GetRecordAutoPlayDelay(action));
            }
        }
    }

    private static float GetRecordAutoPlayDelay(string action) {
        switch (action) {
            case "d":
            case "gd":
            case "bd":
            case "bh":
                return 0.5f;
            case "c":
                return 0.7f;
            case "ag":
            case "jg":
            case "cl":
            case "cm":
            case "cr":
            case "p":
            case "g":
                return 0.8f;
            default:
                return 0.6f;
        }
    }

    private void StopRecordAutoPlay() {
        if (recordAutoPlayCoroutine != null) {
            StopCoroutine(recordAutoPlayCoroutine);
            recordAutoPlayCoroutine = null;
        }
        IsRecordAutoPlaying = false;
        UpdateRecordAutoPlayButtonText();
    }

    private void CompleteRecordAutoPlay(bool showCompletedTip = true) {
        recordAutoPlayCoroutine = null;
        bool wasPlaying = IsRecordAutoPlaying;
        IsRecordAutoPlaying = false;
        UpdateRecordAutoPlayButtonText();
        if (wasPlaying && showCompletedTip) {
            NotificationManager.Instance.ShowTip("牌谱", true, "自动播放完毕");
        }
    }

    private void UpdateRecordAutoPlayButtonText() {
        if (recordAutoPlayButtonText != null) {
            recordAutoPlayButtonText.text = IsRecordAutoPlaying ? "暂停播放" : "自动播放";
        }
    }

    private void UpdateRecordAutoPlayButtonVisibility() {
        if (recordAutoPlayButton == null) return;
        bool visible = CurrentMode == RecordManagerMode.Record && !IsSpectating;
        if (!visible && IsRecordAutoPlaying) {
            StopRecordAutoPlay();
        }
        recordAutoPlayButton.gameObject.SetActive(visible);
        LayoutRecordControlButtons(visible);
    }

    private void InvalidateRecordDelayedAdvances() {
        _recordPlaybackGeneration++;
        _pendingRecordDelayedAdvanceCount = 0;
    }

    public void NextXunmu() {
        StopRecordAutoPlay();
        if (BlocksRecordNavigation) return;
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
        StopRecordAutoPlay();
        if (BlocksRecordNavigation) return;
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
        if (BlocksRecordNavigation) {
            return;
        }
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
        if (BlocksRecordNavigation) return;
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
            tileListView.SetActive(true);
            RefreshRecordChongHint();
            FocusTileListScrollOnWallSection();
        } else {
            tileListView.SetActive(false);
            RefreshRecordChongHint();
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
        PostGameNavigator.ExitToRecord();
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
