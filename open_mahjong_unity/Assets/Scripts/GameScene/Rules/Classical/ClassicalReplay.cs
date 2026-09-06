using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 古典牌谱回放：直播是报自摸/点和 → 倒牌 → 数和尾；牌谱 tick 却是 shuhewei → hu_*。
/// 演出挂在 shuhewei 上，后续 hu/liuju 只同步状态。
/// </summary>
public partial class GameRecordManager {
    private bool RecordHuTickFollowsShuhewei() {
        return RecordManifest()?.HuTickFollowsShuhewei == true;
    }

    private static bool IsClassicalFollowSettlementAction(string action) {
        return action == "hu_self" || action == "hu_first"
            || action == "hu_second" || action == "hu_third"
            || action == "liuju";
    }

    private static bool IsRecordHuClass(string action) {
        return action == "hu_self" || action == "hu_first"
            || action == "hu_second" || action == "hu_third";
    }

    private bool ShouldSkipRecordTickVoice(string action) {
        if (action == "shuhewei") return true;
        return RecordHuTickFollowsShuhewei() && IsClassicalFollowSettlementAction(action);
    }

    /// <summary>数和尾确认后吞掉跟随的 hu/liuju，并自动走 end 切局。</summary>
    private void DrainClassicalFollowSettlementTicks() {
        if (!RecordHuTickFollowsShuhewei()) return;
        if (gameRecord?.gameRound?.rounds == null) return;
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)
            || roundData.actionTicks == null) {
            return;
        }
        int guard = 0;
        while (guard++ < 8 && currentNode < roundData.actionTicks.Count) {
            List<string> tick = roundData.actionTicks[currentNode];
            if (tick == null || tick.Count == 0) break;
            string action = tick[0];
            if (IsClassicalFollowSettlementAction(action)) {
                NextAction();
                continue;
            }
            if (action == "end") {
                NextAction();
            }
            break;
        }
    }

    private void PlayClassicalRecordShuhewei(List<string> tick) {
        int[] fuArray = ParseTickScoreChanges(tick, 1);
        int[] changesArray = ParseTickScoreChanges(tick, 2);
        string[][] fanArray = ParseTickFanLists(tick, 3);
        string[][] fuTypeArray = ParseTickFanLists(tick, 4);
        string huClass = tick.Count > 5 ? (tick[5] ?? "").Trim().Trim('"') : "";
        int hepaiPlayerIndex = tick.Count > 6 ? ParseTickInt(tick, 6) : -1;

        var playerFu = new Dictionary<int, int>();
        var scoreChanges = new Dictionary<int, int>();
        var deltas = new Dictionary<int, int>();
        var playerFan = new Dictionary<int, string[]>();
        var playerFuTypes = new Dictionary<int, string[]>();

        foreach (var rp in recordPlayerList) {
            int origIdx = rp.originalPlayerIndex;
            playerFu[rp.playerIndex] = (fuArray != null && origIdx < fuArray.Length) ? fuArray[origIdx] : 0;
            int change = (changesArray != null && origIdx < changesArray.Length) ? changesArray[origIdx] : 0;
            scoreChanges[rp.playerIndex] = change;
            deltas[rp.playerIndex] = change;
            playerFan[rp.playerIndex] = (fanArray != null && origIdx < fanArray.Length && fanArray[origIdx] != null)
                ? fanArray[origIdx] : Array.Empty<string>();
            playerFuTypes[rp.playerIndex] = (fuTypeArray != null && origIdx < fuTypeArray.Length && fuTypeArray[origIdx] != null)
                ? fuTypeArray[origIdx] : Array.Empty<string>();
        }

        ApplyScoreDeltas(deltas, out _, out Dictionary<int, int> playerToScoreAfter);

        var playerToInfo = new Dictionary<string, PlayerInfoClass>();
        foreach (var rp in recordPlayerList) {
            string pos = indexToPosition[rp.playerIndex];
            string username = userIdToUsername.TryGetValue(rp.userId, out string name) ? name : rp.userId.ToString();
            playerToInfo[pos] = new PlayerInfoClass { username = username, userId = rp.userId };
        }

        BoardCanvas.Instance.UpdatePlayerScores(playerToScoreAfter, indexToPosition);
        TryRefreshRecordScoreTable();

        _recordHuPresentationCoroutine = StartCoroutine(CoPresentClassicalRecordShuhewei(
            huClass, hepaiPlayerIndex, playerFu, playerToScoreAfter, scoreChanges, playerFan, playerFuTypes, playerToInfo));

        if (IsSpectating && IsLiveSpectatorMode) {
            float revealWait = 0f;
            bool animate = RecordSetting.Instance != null && RecordSetting.Instance.IsShowHepaiAnimation;
            if (animate) {
                foreach (var rp in recordPlayerList) {
                    int idx = rp.playerIndex;
                    int revealItems = (playerFuTypes.ContainsKey(idx) ? playerFuTypes[idx].Length : 0)
                        + (playerFan.ContainsKey(idx) ? playerFan[idx].Length : 0);
                    revealWait += revealItems * 1.0f + 0.5f;
                }
            }
            StartCoroutine(AutoNextActionAfterDelay(animate ? 8f + revealWait : 2f));
        }
    }

    private IEnumerator CoPresentClassicalRecordShuhewei(
        string huClass,
        int hepaiPlayerIndex,
        Dictionary<int, int> playerFu,
        Dictionary<int, int> playerToScoreAfter,
        Dictionary<int, int> scoreChanges,
        Dictionary<int, string[]> playerFan,
        Dictionary<int, string[]> playerFuTypes,
        Dictionary<string, PlayerInfoClass> playerToInfo) {
        BeginRecordHuPresentation();
        try {
            bool animatePanel = RecordSetting.Instance != null && RecordSetting.Instance.IsShowHepaiAnimation;
            bool isHu = IsRecordHuClass(huClass);

            if (!isHu && (huClass == "liuju" || string.IsNullOrEmpty(huClass))) {
                RoundEndPresentation.Instance.PresentLiuju("流局", false);
                yield return new WaitForSeconds(HepaiRevealTiming.RecordShowCardsPanelDelaySeconds);
                if (EndLiujuPanel.Instance != null) {
                    EndLiujuPanel.Instance.ClearEndLiujuPanel();
                }
            }

            if (isHu && indexToPosition.TryGetValue(hepaiPlayerIndex, out string huPosition)
                && recordPlayer_to_info.TryGetValue(huPosition, out RecordPlayer huPlayer)) {
                string[] huFan = playerFan != null && playerFan.ContainsKey(hepaiPlayerIndex)
                    ? playerFan[hepaiPlayerIndex] : Array.Empty<string>();
                string recordRule = ResolveRecordHepaiRuleKey();
                List<string> huTick = PeekFollowingHuTick();
                int[] hepaiPlayerHand = huTick != null
                    ? RecordHuHandBuilder.BuildDisplayHandFromTick(
                        huTick, recordRule, huPlayer.tileList, huClass, lastWinnableTileId)
                    : RecordHuHandBuilder.BuildDisplayHand(
                        huPlayer.tileList, huClass, 0, lastWinnableTileId, recordRule);
                yield return CoAnnounceRecordHuAndReveal(huClass, huFan, huPosition, hepaiPlayerHand);
            }

            if (EndResultPanel.Instance != null) {
                EndResultPanel.Instance.ClearEndResultPanel();
            }
            EndShuheWeiPanel.Instance.ShowShuhewei(
                playerFu, playerToScoreAfter, scoreChanges, playerFan, playerFuTypes,
                indexToPosition, playerToInfo, true, animatePanel);
        } finally {
            EndRecordHuPresentation();
        }
    }

    private List<string> PeekFollowingHuTick() {
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)
            || roundData.actionTicks == null) {
            return null;
        }
        for (int i = currentNode + 1; i < roundData.actionTicks.Count; i++) {
            List<string> tick = roundData.actionTicks[i];
            if (tick == null || tick.Count == 0) continue;
            string action = tick[0];
            if (IsRecordHuClass(action)) return tick;
            if (action == "end" || action == "liuju" || action == "shuhewei") break;
        }
        return null;
    }
}
