using System.Collections.Generic;

public partial class GameRecordManager {
    internal int RecordNavigationVersion => _recordPlaybackGeneration;

    private void InitializeRecordStepButtons() {
        nextStepButton.onClick.AddListener(() => StepRecordFromInput(true));
        backStepButton.onClick.AddListener(() => StepRecordFromInput(false));
    }

    public bool CanUseRecordStepInput(bool forward) {
        if (!isActiveAndEnabled || BlocksRecordNavigation || _pendingRecordDelayedAdvanceCount > 0
            || gameRecord?.gameRound?.rounds == null
            || !gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)
            || round.actionTicks == null) return false;
        string window = GameHost.Current.CurrentWindow;
        if (window != "game" && window != "recordscene") return false;
        return forward ? currentNode < round.actionTicks.Count : currentNode > 0;
    }

    public void StepRecordFromInput(bool forward) {
        StopRecordAutoPlay();
        if (!CanUseRecordStepInput(forward)) return;
        if (forward) NextStep();
        else BackStep();
    }

    /// <summary>只等本次动作涉及的座位，允许另一家在上一家飞牌时继续摸切。</summary>
    private bool CanAnimateNextRecordStep() {
        if (gameRecord?.gameRound?.rounds == null
            || !gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)
            || round.actionTicks == null || Game3DManager.Instance == null) return false;

        int playerIndex = currentPlayerIndex;
        int node = currentNode;
        while (node < round.actionTicks.Count && IsRecordSilentTick(round.actionTicks[node])) {
            List<string> silent = round.actionTicks[node++];
            if (silent != null && silent.Count > 0 && silent[0] == "reset") playerIndex = ParseTickInt(silent, 1);
        }
        if (node >= round.actionTicks.Count) return true;

        List<string> tick = round.actionTicks[node];
        string action = tick[0];
        switch (action) {
            case "d": case "gd": case "bd": case "c": case "bh":
            case "ag": case "jg": case "rk":
            case "cl": case "cm": case "cr": case "p": case "g":
                int actor = GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(tick, action, playerIndex);
                if (!indexToPosition.TryGetValue(actor, out string position)
                    || Game3DManager.Instance.HasPendingRecordStepAnimations(position)) return false;
                // 吃碰认牌依赖上一张河牌；待来源落定再改牌谱手牌，避免展示读到未来状态。
                if ((action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g")
                    && indexToPosition.TryGetValue(lastDiscardPlayerIndex, out string discarder)
                    && Game3DManager.Instance.HasPendingRecordStepAnimations(discarder)) return false;
                return true;
            default:
                // 和牌、流局、局末等全桌变化，须等当前动画完成后再交给既有结算流程。
                return !Game3DManager.Instance.HasPendingRecordTableAnimations;
        }
    }
}
