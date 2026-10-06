using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GameRecordManager {
    internal int RecordNavigationVersion => _recordPlaybackGeneration;
    private Coroutine _recordBuhuaContinuation;
    private int _recordBuhuaInputTargetNode;
    internal bool HasPendingRecordBuhuaContinuation => _recordBuhuaContinuation != null;
    internal int RecordStepInputTargetNode => HasPendingRecordBuhuaContinuation ? _recordBuhuaInputTargetNode : currentNode;

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

    public void StepRecordFromInput(bool forward, bool continueBuhua = false) {
        StopRecordAutoPlay();
        if (!forward) CancelRecordBuhuaContinuation();
        if (!CanUseRecordStepInput(forward)) return;
        if (forward) {
            if (HasPendingRecordBuhuaContinuation) return;
            if (continueBuhua && TryContinueRecordBuhuaFromInput()) return;
            NextStep();
        }
        else BackStep();
    }

    private bool TryContinueRecordBuhuaFromInput() {
        if (!TryGetNextRecordStep(out Round round, out int node, out int actor)
            || round.actionTicks[node][0] != "bd"
            || !indexToPosition.TryGetValue(actor, out string position)
            || !Game3DManager.Instance.HasRecordBuhuaAnimation(position)) return false;

        int previous = currentNode - 1;
        while (previous >= 0 && IsRecordSilentTick(round.actionTicks[previous])) previous--;
        if (previous < 0 || round.actionTicks[previous][0] != "bh"
            || GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(round.actionTicks[previous], "bh", currentPlayerIndex) != actor) return false;

        if (CanAnimateNextRecordStep(allowBuhuaDraw: true)) {
            AdvanceRecordStep(allowBuhuaDraw: true);
        } else {
            // 只接住本次补摸，重复点击不堆积后续节点，也不需要判断连点间隔。
            _recordBuhuaInputTargetNode = node + 1;
            _recordBuhuaContinuation = StartCoroutine(ContinueRecordBuhuaFromInput(
                currentRoundIndex, currentNode, RecordNavigationVersion, Game3DManager.Instance.RecordHandAnimationVersion));
        }
        return true;
    }

    private IEnumerator ContinueRecordBuhuaFromInput(int roundIndex, int node, int navigationVersion, int animationVersion) {
        while (CanUseRecordStepInput(true) && !IsRecordAutoPlaying
            && currentRoundIndex == roundIndex && currentNode == node && RecordNavigationVersion == navigationVersion
            && Game3DManager.Instance != null && Game3DManager.Instance.RecordHandAnimationVersion == animationVersion) {
            if (CanAnimateNextRecordStep(allowBuhuaDraw: true)) {
                _recordBuhuaContinuation = null;
                AdvanceRecordStep(allowBuhuaDraw: true);
                yield break;
            }
            yield return null;
        }
        _recordBuhuaContinuation = null;
    }

    private void CancelRecordBuhuaContinuation() {
        if (_recordBuhuaContinuation != null) StopCoroutine(_recordBuhuaContinuation);
        _recordBuhuaContinuation = null;
    }

    private bool TryGetNextRecordStep(out Round round, out int node, out int actor) {
        round = null;
        node = currentNode;
        actor = currentPlayerIndex;
        if (gameRecord?.gameRound?.rounds == null
            || !gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out round)
            || round.actionTicks == null || Game3DManager.Instance == null) return false;
        while (node < round.actionTicks.Count && IsRecordSilentTick(round.actionTicks[node])) {
            List<string> silent = round.actionTicks[node++];
            if (silent != null && silent.Count > 0 && silent[0] == "reset") actor = ParseTickInt(silent, 1);
        }
        if (node < round.actionTicks.Count) {
            List<string> tick = round.actionTicks[node];
            actor = GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(tick, tick[0], actor);
        }
        return node < round.actionTicks.Count;
    }

    /// <summary>只等本次动作涉及的座位，允许另一家在上一家飞牌时继续摸切。</summary>
    private bool CanAnimateNextRecordStep(bool allowBuhuaDraw = false) {
        if (!TryGetNextRecordStep(out Round round, out int node, out int actor))
            return round?.actionTicks != null && Game3DManager.Instance != null && node >= round.actionTicks.Count;
        if (node >= round.actionTicks.Count) return true;

        List<string> tick = round.actionTicks[node];
        string action = tick[0];
        switch (action) {
            case "d": case "gd": case "nd": case "bd": case "c": case "bh": case "nuki":
            case "ag": case "jg": case "rk":
            case "cl": case "cm": case "cr": case "p": case "g":
                if (!indexToPosition.TryGetValue(actor, out string position)
                    || (Game3DManager.Instance.HasPendingRecordStepAnimations(position)
                        && !(allowBuhuaDraw && action == "bd" && Game3DManager.Instance.CanContinueRecordBuhuaDraw(position)))) return false;
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
