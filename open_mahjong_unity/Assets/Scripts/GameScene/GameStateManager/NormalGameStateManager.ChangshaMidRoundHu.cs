using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class NormalGameStateManager {
    private sealed class ChangshaMidRoundHuRevealRequest {
        public int PlayerIndex;
        public Dictionary<int, int> PlayerToScore;
        public Dictionary<int, int> ScoreChanges;
        public string[] HuFan;
        public int[] RevealedTiles;
        public bool IsSilent;
        public string PlayerPosition;
    }

    private sealed class DeferredShowResultRequest {
        public float ArrivalTime;
        public Action Continuation;
    }

    private readonly Queue<ChangshaMidRoundHuRevealRequest> _changshaMidRoundHuRevealQueue
        = new Queue<ChangshaMidRoundHuRevealRequest>();
    private readonly Queue<DeferredShowResultRequest> _changshaDeferredShowResultQueue
        = new Queue<DeferredShowResultRequest>();
    private Coroutine _changshaMidRoundHuRevealCoroutine;
    private Coroutine _changshaDeferredShowResultCoroutine;
    private bool _isReplayingChangshaDeferredResult;

    private void ShowChangshaMidRoundHuResult(
        int playerIndex,
        Dictionary<int, int> playerToScore,
        Dictionary<int, int> scoreChanges,
        string[] huFan,
        int[] revealedTiles,
        bool isSilent) {
        indexToPosition.TryGetValue(playerIndex, out string playerPosition);
        _changshaMidRoundHuRevealQueue.Enqueue(new ChangshaMidRoundHuRevealRequest {
            PlayerIndex = playerIndex,
            PlayerToScore = playerToScore,
            ScoreChanges = scoreChanges,
            HuFan = huFan,
            RevealedTiles = revealedTiles,
            IsSilent = isSilent,
            PlayerPosition = playerPosition,
        });
        if (_changshaMidRoundHuRevealCoroutine == null) {
            _changshaMidRoundHuRevealCoroutine = StartCoroutine(PlayChangshaMidRoundHuRevealQueue());
        }
    }

    /// <summary>连续中途胡按收到顺序完整播放，避免提示文字、分数和亮牌互相覆盖。</summary>
    private IEnumerator PlayChangshaMidRoundHuRevealQueue() {
        while (_changshaMidRoundHuRevealQueue.Count > 0) {
            ChangshaMidRoundHuRevealRequest request = _changshaMidRoundHuRevealQueue.Dequeue();
            ApplyShowResultScores(request.PlayerToScore);
            string actionType = ContainsChangshaHuFan(request.HuFan, "六六顺")
                ? "mid_round_six_six"
                : "mid_round_four_joys";
            if (!request.IsSilent && !string.IsNullOrEmpty(request.PlayerPosition)) {
                GameCanvas.Instance.ShowActionDisplay(request.PlayerPosition, actionType, roomRule);
            }
            if (GameCanvas.HasNonZeroGangScoreChanges(request.ScoreChanges)) {
                GameCanvas.Instance.ShowGangScoreFloats(request.ScoreChanges, 0f);
            }
            if (request.RevealedTiles != null
                && request.RevealedTiles.Length > 0
                && !string.IsNullOrEmpty(request.PlayerPosition)) {
                yield return HepaiRevealDirector.Play(
                    request.PlayerIndex,
                    request.RevealedTiles,
                    "mid_round_hu",
                    request.HuFan);
                Game3DManager.Instance.RestoreMidGameHandAfterCuoheRonReveal(request.PlayerPosition);
            } else {
                yield return null;
            }
        }
        _changshaMidRoundHuRevealCoroutine = null;
    }

    private bool TryDeferShowResultForChangshaMidRoundHu(Action continuation) {
        if (_isReplayingChangshaDeferredResult
            || (_changshaMidRoundHuRevealCoroutine == null
                && _changshaMidRoundHuRevealQueue.Count == 0)) {
            return false;
        }
        _changshaDeferredShowResultQueue.Enqueue(new DeferredShowResultRequest {
            ArrivalTime = Time.realtimeSinceStartup,
            Continuation = continuation,
        });
        if (_changshaDeferredShowResultCoroutine == null) {
            _changshaDeferredShowResultCoroutine = StartCoroutine(ReplayChangshaDeferredShowResults());
        }
        return true;
    }

    /// <summary>中途胡亮牌结束后按原消息间隔恢复普通结算，避免终局演出抢占手牌。</summary>
    private IEnumerator ReplayChangshaDeferredShowResults() {
        while (_changshaMidRoundHuRevealCoroutine != null
            || _changshaMidRoundHuRevealQueue.Count > 0) {
            yield return null;
        }

        float? previousArrivalTime = null;
        while (_changshaDeferredShowResultQueue.Count > 0) {
            DeferredShowResultRequest request = _changshaDeferredShowResultQueue.Dequeue();
            if (previousArrivalTime.HasValue) {
                float delay = Mathf.Max(0f, request.ArrivalTime - previousArrivalTime.Value);
                if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            }
            try {
                _isReplayingChangshaDeferredResult = true;
                request.Continuation?.Invoke();
            } finally {
                _isReplayingChangshaDeferredResult = false;
            }
            previousArrivalTime = request.ArrivalTime;
            yield return null;
        }
        _changshaDeferredShowResultCoroutine = null;
    }

    private static bool ContainsChangshaHuFan(string[] huFan, string expectedFan) {
        if (huFan == null) return false;
        foreach (string fan in huFan) {
            if (fan == expectedFan) return true;
        }
        return false;
    }
}
