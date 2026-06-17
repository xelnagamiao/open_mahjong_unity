using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>四川血战牌谱/观战：中途和牌补花区摆牌与终局分步结算回放。</summary>
public partial class GameRecordManager {
    private bool IsSichuanBloodBattleRecord() {
        if (gameRecord?.gameTitle == null) return false;
        string subRule = ReadGameTitleString(gameRecord.gameTitle, "sub_rule", "").ToLowerInvariant();
        if (!subRule.StartsWith("sichuan")) return false;
        string bloodBattle = ReadGameTitleString(gameRecord.gameTitle, "blood_battle", "true");
        return bloodBattle != "false";
    }

    private bool TryParseGangScoreChangesFromTick(List<string> tick, out Dictionary<int, int> changes) {
        changes = null;
        if (!IsSichuanBloodBattleRecord() || tick == null) return false;
        int gsIndex = tick.LastIndexOf("gs");
        if (gsIndex < 0 || gsIndex + 4 >= tick.Count) return false;
        var arr = new int[4];
        for (int i = 0; i < 4; i++) {
            if (!int.TryParse(tick[gsIndex + 1 + i]?.Trim(), out arr[i])) return false;
        }
        if (arr[0] == 0 && arr[1] == 0 && arr[2] == 0 && arr[3] == 0) return false;
        changes = new Dictionary<int, int>();
        MapTickScoreChangesToDeltas(arr, changes);
        return true;
    }

    private void ApplyRecordGangScoreDeltasFromTick(List<string> tick) {
        if (!TryParseGangScoreChangesFromTick(tick, out Dictionary<int, int> changes)) return;
        ApplyScoreDeltas(changes, out _, out _);
    }

    private void PlayRecordGangScoreChanges(List<string> tick) {
        if (!TryParseGangScoreChangesFromTick(tick, out Dictionary<int, int> changes)) return;
        ApplyScoreDeltas(changes, out _, out Dictionary<int, int> after);
        BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
        GameCanvas.Instance.ShowGangScoreFloats(changes);
    }

    private void PlayRecordGangRefundTick(List<string> tick) {
        if (tick == null || tick.Count < 6 || tick[1] != "gs") return;
        var arr = new int[4];
        for (int i = 0; i < 4; i++) {
            if (!int.TryParse(tick[2 + i]?.Trim(), out arr[i])) return;
        }
        var changes = new Dictionary<int, int>();
        MapTickScoreChangesToDeltas(arr, changes);
        if (!GameCanvas.HasNonZeroGangScoreChanges(changes)) return;
        ApplyScoreDeltas(changes, out _, out Dictionary<int, int> after);
        BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
        GameCanvas.Instance.ShowGangScoreFloats(changes);
    }

    private static bool IsDeferredSichuanHuScore(int huScore, int[] scoreChanges) {
        if (huScore != 0) return false;
        if (scoreChanges == null) return true;
        foreach (int c in scoreChanges) {
            if (c != 0) return false;
        }
        return true;
    }

    private void TryParseSichuanHuTickExtras(List<string> tick, out int hepaiTile, out bool multiRon, out int? ronDiscarderIndex, out bool recycleDiscard) {
        hepaiTile = 0;
        multiRon = false;
        ronDiscarderIndex = null;
        recycleDiscard = false;
        if (tick == null || tick.Count <= 5) return;
        string rule = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
        if (rule == "classical") return;
        hepaiTile = ParseTickInt(tick, 5);
        if (tick.Count > 6) multiRon = ParseTickInt(tick, 6) != 0;
        if (tick.Count > 7) ronDiscarderIndex = ParseTickInt(tick, 7);
        if (tick.Count > 8) recycleDiscard = ParseTickInt(tick, 8) != 0;
    }

    private static bool ContainsSichuanQianggangFan(string[] huFan) {
        if (huFan == null) return false;
        for (int i = 0; i < huFan.Length; i++) {
            if (huFan[i] == "抢杠") return true;
        }
        return false;
    }

    private int ResolveRecordHepaiTile(string action, int hepaiPlayerIndex, int parsedTile, RecordPlayer huPlayer) {
        if (parsedTile >= 10) return parsedTile;
        if (action != "hu_self" && lastWinnableTileId >= 10) return lastWinnableTileId;
        if (huPlayer?.tileList != null && huPlayer.tileList.Count > 0) {
            return huPlayer.tileList[huPlayer.tileList.Count - 1];
        }
        return parsedTile;
    }

    private void SyncRecordRonDiscardRemoved(string discarderPos, int tileId) {
        if (string.IsNullOrEmpty(discarderPos) || !recordPlayer_to_info.TryGetValue(discarderPos, out RecordPlayer rp)) return;
        if (rp.discardTiles == null || rp.discardTiles.Count == 0) return;
        if (tileId >= 10) {
            int idx = rp.discardTiles.LastIndexOf(tileId);
            if (idx >= 0) rp.discardTiles.RemoveAt(idx);
            else rp.discardTiles.RemoveAt(rp.discardTiles.Count - 1);
        } else {
            rp.discardTiles.RemoveAt(rp.discardTiles.Count - 1);
        }
        if (rp.discardRiichiFlags != null && rp.discardRiichiFlags.Count > 0) {
            rp.discardRiichiFlags.RemoveAt(rp.discardRiichiFlags.Count - 1);
        }
    }

    private string ResolveRecordRonDiscarderPosition(int? ronDiscarderIndex) {
        if (ronDiscarderIndex.HasValue && indexToPosition.TryGetValue(ronDiscarderIndex.Value, out string pos)) {
            return pos;
        }
        if (lastDiscardPlayerIndex >= 0 && indexToPosition.TryGetValue(lastDiscardPlayerIndex, out string fallback)) {
            return fallback;
        }
        return null;
    }

    private void PlaySichuanMidGameHuRecord(
        string action, int hepaiPlayerIndex, int hepaiTile, bool multiRon, int? ronDiscarderIndex,
        bool recycleDiscard, bool isQianggang) {
        if (!indexToPosition.TryGetValue(hepaiPlayerIndex, out string winnerPos)) return;
        GameCanvas.Instance.ShowActionDisplay(winnerPos, action);
        SoundManager.Instance.PlayActionSound(winnerPos, action);
        StartCoroutine(CoPlaySichuanMidGameHuRecord(
            action, hepaiPlayerIndex, hepaiTile, multiRon, ronDiscarderIndex, recycleDiscard, isQianggang));
    }

    private IEnumerator CoPlaySichuanMidGameHuRecord(
        string action, int hepaiPlayerIndex, int hepaiTile, bool multiRon,
        int? ronDiscarderIndex, bool recycleDiscard, bool isQianggang) {
        yield return HepaiRevealDirector.PlaySichuanMidGame(
            hepaiPlayerIndex, action, hepaiTile, multiRon, ronDiscarderIndex, recycleDiscard, isQianggang);
        if (action != "hu_self" && recycleDiscard) {
            SyncRecordRonDiscardRemoved(ResolveRecordRonDiscarderPosition(ronDiscarderIndex), hepaiTile);
        }
        yield return new WaitForSeconds(1.5f);
    }

    private void HandleSichuanLiujuStepReplay(List<string> tick) {
        if (tick == null || tick.Count < 2) {
            RoundEndPresentation.Instance.PresentLiuju("流局", false);
            StartCoroutine(AutoNextActionAfterDelay(2f));
            return;
        }

        string step = tick[1];
        switch (step) {
            case "reveal_hu":
                HandleSichuanRecordRevealHu(tick);
                StartCoroutine(AutoNextActionAfterDelay(RoundEndTiming.RoundEndHandRevealSeconds));
                break;
            case "settle_hu":
                HandleSichuanRecordSettleHu(tick);
                break;
            case "chajiao":
                HandleSichuanRecordChajiao(tick);
                break;
            case "cha_refund":
                HandleSichuanRecordChaRefund(tick);
                break;
            case "final":
                HandleSichuanRecordFinal(tick);
                StartCoroutine(AutoNextActionAfterDelay(RoundEndTiming.HuConfirmCountdownSeconds));
                break;
            default:
                RoundEndPresentation.Instance.PresentLiuju("流局", false);
                StartCoroutine(AutoNextActionAfterDelay(2f));
                break;
        }
    }

    private Dictionary<int, int[]> ParseRecordHuHandsJson(string json) {
        var result = new Dictionary<int, int[]>();
        if (string.IsNullOrEmpty(json)) return result;
        try {
            JObject obj = JObject.Parse(json);
            foreach (var prop in obj.Properties()) {
                if (!int.TryParse(prop.Name, out int idx)) continue;
                if (prop.Value is JArray arr) {
                    var tiles = new int[arr.Count];
                    for (int i = 0; i < arr.Count; i++) tiles[i] = arr[i].Value<int>();
                    result[idx] = tiles;
                }
            }
        } catch (Exception e) {
            Debug.LogWarning($"[GameRecord] reveal_hu hands parse failed: {e.Message}");
        }
        return result;
    }

    private void HandleSichuanRecordRevealHu(List<string> tick) {
        if (tick.Count < 3) return;
        Dictionary<int, int[]> allHands = ParseRecordHuHandsJson(tick[2]);
        if (allHands.Count > 0) {
            RoundEndPresentation.Instance.ResetSichuanEndgameQueue();
            RoundEndPresentation.Instance.EnqueueSichuanRevealHu(allHands);
        }
    }

    private void HandleSichuanRecordSettleHu(List<string> tick) {
        if (tick.Count < 7) return;
        string huClass = tick[2];
        int winner = ParseTickInt(tick, 3);
        int huScore = ParseTickInt(tick, 4);
        string[] huFan = ParseHuFanList(tick, 5);
        int[] changesArr = ParseTickScoreChanges(tick, 6);
        var deltas = new Dictionary<int, int>();
        if (changesArr != null) MapTickScoreChangesToDeltas(changesArr, deltas);
        ApplyScoreDeltas(deltas, out Dictionary<int, int> before, out Dictionary<int, int> after);
        BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);

        RoundEndPresentation.Instance.EnqueueSichuanSettleHu(
            winner, after, huScore, huFan, huClass,
            null, null, deltas, isFinalPanel: false);
        GameSceneUIManager.Instance.UpdateScoreRecord();
        float wait = RoundEndTiming.GetSichuanSettleHuPanelDuration(huFan?.Length ?? 0, isFinalPanel: false);
        StartCoroutine(AutoNextActionAfterDelay(wait));
    }

    private void HandleSichuanRecordChajiao(List<string> tick) {
        if (tick.Count < 6) return;
        int focusIndex = ParseTickInt(tick, 2);
        string statusKey = tick[3];
        int[] hand = ParseRecordHandJson(tick[4]);
        int[] changesArr = ParseTickScoreChanges(tick, 5);
        var scoreChanges = new Dictionary<int, int>();
        if (changesArr != null) MapTickScoreChangesToDeltas(changesArr, scoreChanges);
        ApplyScoreDeltas(scoreChanges, out _, out Dictionary<int, int> after);
        BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
        bool isFinal = tick.Count > 6 && ParseTickInt(tick, 6) != 0;
        RoundEndPresentation.Instance.EnqueueSichuanChajiao(
            focusIndex, statusKey, hand, null, after, scoreChanges, isFinal);
        GameSceneUIManager.Instance.UpdateScoreRecord();
        float wait = RoundEndTiming.GetSichuanChajiaoPanelDuration(isFinal);
        StartCoroutine(AutoNextActionAfterDelay(wait));
    }

    private static int[] ParseRecordHandJson(string json) {
        if (string.IsNullOrEmpty(json)) return null;
        try {
            JArray arr = JArray.Parse(json);
            var tiles = new int[arr.Count];
            for (int i = 0; i < arr.Count; i++) tiles[i] = arr[i].Value<int>();
            return tiles;
        } catch (Exception e) {
            Debug.LogWarning($"[GameRecord] chajiao hand parse failed: {e.Message}");
            return null;
        }
    }

    private static Dictionary<int, string> ParseRecordStatusJson(string json) {
        var result = new Dictionary<int, string>();
        if (string.IsNullOrEmpty(json)) return result;
        try {
            JObject obj = JObject.Parse(json);
            foreach (var prop in obj.Properties()) {
                if (!int.TryParse(prop.Name, out int idx)) continue;
                result[idx] = prop.Value?.Value<string>() ?? "no_ting";
            }
        } catch (Exception e) {
            Debug.LogWarning($"[GameRecord] chajiao status parse failed: {e.Message}");
        }
        return result;
    }

    private void HandleSichuanRecordChaRefund(List<string> tick) {
        if (tick.Count < 3) return;
        int[] changesArr = ParseTickScoreChanges(tick, 2);
        var scoreChanges = new Dictionary<int, int>();
        if (changesArr != null) MapTickScoreChangesToDeltas(changesArr, scoreChanges);
        ApplyScoreDeltas(scoreChanges, out _, out Dictionary<int, int> after);
        BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
        if (GameCanvas.HasNonZeroGangScoreChanges(scoreChanges)) {
            GameCanvas.Instance.ShowGangScoreFloats(scoreChanges);
        }
        RoundEndPresentation.Instance.EnqueueSichuanChaRefund(after, scoreChanges, isFinalPanel: true);
        GameSceneUIManager.Instance.UpdateScoreRecord();
        float wait = RoundEndTiming.RoundEndPresentationFadeSeconds + RoundEndTiming.HuConfirmCountdownSeconds;
        StartCoroutine(AutoNextActionAfterDelay(wait));
    }

    private void HandleSichuanRecordFinal(List<string> tick) {
        if (tick.Count >= 3) {
            int[] scores = ParseTickScoreChanges(tick, 2);
            if (scores != null) {
                var after = new Dictionary<int, int>();
                for (int i = 0; i < scores.Length && i < 4; i++) after[i] = scores[i];
                BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
            }
        }
    }
}
