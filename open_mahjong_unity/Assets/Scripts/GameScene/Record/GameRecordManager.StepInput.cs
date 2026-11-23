using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GameRecordManager {
    internal int RecordNavigationVersion => _recordPlaybackGeneration;
    private Coroutine _pendingRecordStepInput;
    private int _pendingRecordStepTargetNode;
    internal bool HasPendingRecordStepInput => _pendingRecordStepInput != null;
    internal int RecordStepInputTargetNode => HasPendingRecordStepInput ? _pendingRecordStepTargetNode : currentNode;

    private void InitializeRecordStepButtons() {
        nextStepButton.onClick.AddListener(() => StepRecordFromInput(true));
        backStepButton.onClick.AddListener(() => StepRecordFromInput(false));
    }

    private bool BuffersRecordStepInput() {
        if ((IsSpectatorSession && IsLiveSpectatorMode) || gameRecord?.gameTitle == null) return false;
        string subRule = ReadGameTitleString(gameRecord.gameTitle, "sub_rule", "");
        return IsDuplicateReplay || SichuanRoundScoreState.Supports(subRule) || WashizuDoubles.IsEnabled(subRule);
    }

    public bool CanUseRecordStepInput(bool forward) {
        if (ReplayFrameLoader.Busy || !isActiveAndEnabled || (forward && IsRecordMatchResultVisible) || IsAwaitingRecordResult
            || gameRecord?.gameRound?.rounds == null
            || !gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)
            || round.actionTicks == null) return false;
        if ((_recordHuPresentationActive || _pendingRecordDelayedAdvanceCount > 0)
            && !BuffersRecordStepInput()) return false;
        string window = GameHost.Current.CurrentWindow;
        if (window != "game" && window != "recordscene") return false;
        if (!forward) return currentNode > 0;
        return currentNode < round.actionTicks.Count
            || (AtMatchEnd && gameRecord.matchResult != null && BuffersRecordStepInput() && !IsSpectatorSession);
    }

    public void StepRecordFromInput(bool forward, bool continueBuhua = false) =>
        HandleRecordStepInput(forward, continueBuhua, allowDeferredInput: true);

    internal void HandleRecordStepInput(bool forward, bool continueBuhua, bool allowDeferredInput) {
        StopRecordAutoPlay();
        if (!forward) CancelPendingRecordStepInput();
        if (!CanUseRecordStepInput(forward)) return;
        if (!forward) {
            BackStep();
            return;
        }
        if (HasPendingRecordStepInput) {
            if (allowDeferredInput && BuffersRecordStepInput()) {
                var ticks = gameRecord.gameRound.rounds[currentRoundIndex].actionTicks;
                int next = Mathf.Max(currentNode, _pendingRecordStepTargetNode);
                while (next < ticks.Count && IsRecordSilentTick(ticks[next])) next++;
                if (next < ticks.Count) _pendingRecordStepTargetNode = next + 1;
            }
            return;
        }
        if (AtMatchEnd) {
            NextStep();
            return;
        }
        if (!TryGetNextRecordStep(out Round round, out int node, out int actor)) {
            NextStep();
            return;
        }
        bool allowBuhuaDraw = continueBuhua && IsContinuingRecordBuhua(round, node, actor);
        if (CanAdvanceRecordStepNow(allowBuhuaDraw)) {
            AdvanceRecordStep(allowBuhuaDraw);
        } else if (BuffersRecordStepInput() ? allowDeferredInput : allowBuhuaDraw) {
            _pendingRecordStepTargetNode = node + 1;
            _pendingRecordStepInput = StartCoroutine(WaitForRecordStepInput(currentRoundIndex, currentNode,
                RecordNavigationVersion, allowBuhuaDraw, Game3DManager.Instance.RecordHandAnimationVersion));
        }
    }

    private bool IsContinuingRecordBuhua(Round round, int node, int actor) {
        if (round.actionTicks[node][0] != "bd"
            || !indexToPosition.TryGetValue(actor, out string position)
            || !Game3DManager.Instance.HasRecordBuhuaAnimation(position)) return false;
        int previous = currentNode - 1;
        while (previous >= 0 && IsRecordSilentTick(round.actionTicks[previous])) previous--;
        return previous >= 0 && round.actionTicks[previous][0] == "bh"
            && GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(round.actionTicks[previous], "bh", currentPlayerIndex) == actor;
    }

    private bool CanAdvanceRecordStepNow(bool allowBuhuaDraw) =>
        !BlocksRecordNavigation && _pendingRecordDelayedAdvanceCount == 0 && CanAnimateNextRecordStep(allowBuhuaDraw);

    private IEnumerator WaitForRecordStepInput(int roundIndex, int node, int generation, bool allowBuhuaDraw, int animationVersion) {
        yield return null;
        while (currentRoundIndex == roundIndex && currentNode < _pendingRecordStepTargetNode && RecordNavigationVersion == generation
            && CanUseRecordStepInput(true) && !IsRecordAutoPlaying && !(IsSpectatorSession && IsLiveSpectatorMode)
            && Game3DManager.Instance != null
            && (!allowBuhuaDraw || Game3DManager.Instance.RecordHandAnimationVersion == animationVersion)) {
            bool continueBuhua = allowBuhuaDraw && currentNode == node;
            if (CanAdvanceRecordStepNow(continueBuhua)) {
                AdvanceRecordStep(continueBuhua);
                if (currentNode >= _pendingRecordStepTargetNode) break;
            }
            yield return null;
        }
        _pendingRecordStepInput = null;
    }

    private void CancelPendingRecordStepInput() {
        if (_pendingRecordStepInput != null) StopCoroutine(_pendingRecordStepInput);
        _pendingRecordStepInput = null;
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
            case "ca":
                // 战术鸣牌申请只报声、飘字，不操作任何牌；出牌飞行和手牌收拢不能吞掉这次点击。
                return true;
            case "d": case "gd": case "nd": case "bd": case "c": case "bh": case "nuki":
            case "ag": case "jg": case "rk":
            case "cl": case "cm": case "cr": case "p": case "g":
                if (!indexToPosition.TryGetValue(actor, out string position)
                    || (Game3DManager.Instance.HasPendingRecordStepAnimations(position)
                        && !(allowBuhuaDraw && action == "bd" && Game3DManager.Instance.CanContinueRecordBuhuaDraw(position)))) return false;
                // 本次弃牌登记后即可在飞行中被吃碰；来源手牌仍由其队列完成收拢。
                // 尚未登记时继续等，不能误认该家上一张同点数的河牌。
                if ((action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g")
                    && indexToPosition.TryGetValue(lastDiscardPlayerIndex, out string discarder)
                    && Game3DManager.Instance.HasPendingRecordStepAnimations(discarder)
                    && !Game3DManager.Instance.HasClaimableRecordDiscard(discarder, ParseTickInt(tick, 1))) return false;
                return true;
            default:
                // 和牌、流局、局末等全桌变化，须等当前动画完成后再交给既有结算流程。
                return !Game3DManager.Instance.HasPendingRecordTableAnimations;
        }
    }
}
