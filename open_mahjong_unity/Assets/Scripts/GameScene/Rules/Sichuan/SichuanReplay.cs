using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 四川血战牌谱/观战回放：仍是 GameRecordManager 的 partial（共用私有推演状态），
/// 文件放在族目录便于合并。拆程序集时再改成独立 SichuanReplay 助手。
/// </summary>
public partial class GameRecordManager {
    /// <summary>终局分步结算的分差累加（回放自己持有，不借用对局族实例）。</summary>
    private readonly SichuanEndgameLedger sichuanRecordLedger = new SichuanEndgameLedger();

    /// <summary>本局 <c>reveal_hu</c> 缓存的四家手牌，供随后 settle_hu 面板使用。</summary>
    private Dictionary<int, int[]> sichuanRecordRevealHands;

    private bool TryFlushSichuanRecordLedger() {
        return sichuanRecordLedger.TryFlushToHistory(
            NormalGameStateManager.Instance != null ? NormalGameStateManager.Instance.subRule : null,
            TableMirror.Current.RoundSettlementHistory);
    }

    /// <summary>四川规则牌谱（含血战/非血战）。杠后摸牌与普通摸牌同向从头取，不用倒序岭上。</summary>
    private bool IsSichuanRecord() {
        return ReadGameTitleString(gameRecord.gameTitle, "rule", "") == "sichuan"
            || RecordManifest()?.KongReplacementFromFront == true;
    }

    private bool IsXueliuRecord() => SichuanLobby.IsXueliu(ReadGameTitleString(gameRecord.gameTitle, "sub_rule", ""));

    private bool UsesSichuanSettlementRecord() => IsBloodBattleRecord() || IsXueliuRecord();

    // 川麻和牌后由最后一位和牌者的下一家续打；血战还要跳过本局已和退场者。
    // 巡目索引用自己的退场集合，不能读取当前回放节点的 isHu。
    private int NextRecordPlayerIndex(int fromIndex, HashSet<int> huPlayers = null) {
        for (int offset = 1; offset <= 4; offset++) {
            int next = (fromIndex + offset) % 4;
            if (!IsBloodBattleRecord()) return next;
            bool isHu = huPlayers != null
                ? huPlayers.Contains(next)
                : indexToPosition.TryGetValue(next, out string position)
                    && recordPlayer_to_info.TryGetValue(position, out RecordPlayer player) && player.isHu;
            if (!isHu) return next;
        }
        return fromIndex;
    }

    // 血流保留听牌手牌，逐次和牌张累计到花区供连续播放和跳转重建。
    private void ApplyXueliuRecordWin(List<string> tick, string action) {
        int winner = ParseTickInt(tick, 1);
        if (!indexToPosition.TryGetValue(winner, out string position)) return;
        RecordPlayer player = recordPlayer_to_info[position];
        RecordHuHandBuilder.ParseSichuanHuExtras(tick, out int tile, out bool multi, out int? source, out bool recycle);
        if (action == "hu_self" && player.tileList.Count % 3 == 2) {
            player.tileList.RemoveAt(player.tileList.Count - 1);
        }
        player.showHandDrawSlotActive = false;
        if (tile >= 10) player.huapaiList.Add(tile);
        if (action == "hu_self") return;
        string sourcePosition = ResolveRecordRonDiscarderPosition(source);
        bool qianggang = lastJiagangPlayerIndex >= 0 && lastJiagangPlayerIndex == source;
        if (qianggang && recordPlayer_to_info.TryGetValue(sourcePosition ?? "", out RecordPlayer from)) {
            for (int i = 0; i < from.combinationTiles.Count; i++) {
                if (from.combinationTiles[i] != $"g{tile}") continue;
                var mask = new List<int>(from.combinationMasks[i]);
                for (int j = 0; j < mask.Count; j += 2) {
                    if (mask[j] != 3) continue;
                    mask.RemoveRange(j, 2);
                    from.combinationMasks[i] = mask.ToArray();
                    from.combinationTiles[i] = $"k{tile}";
                    break;
                }
            }
        } else if (recycle || !multi) {
            SyncRecordRonDiscardRemoved(sourcePosition, tile);
        }
    }

    private void PlayXueliuWinRecord(string action, int winner, int tile, bool multi, int? source, bool recycle, bool qianggang,
        Dictionary<int, int> after, Dictionary<int, int> changes) {
        BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
        GameCanvas.Instance.ShowGangScoreFloats(changes, indexToPosition);
        if (!indexToPosition.TryGetValue(winner, out string seat)) return;
        string from = ResolveRecordRonDiscarderPosition(source);
        if (action != "hu_self") Game3DManager.Instance.SyncRecordLastDiscardForRon(from, tile);
        if (seat == "self" && action == "hu_self") {
            TableMirror.Current.SelfHandTiles.Remove(tile);
            GameCanvas.Instance.ChangeHandCards("RemoveHuWinTile", tile, null, null);
        }
        GameCanvas.Instance.ShowActionDisplay(seat, action);
        SoundManager.Instance.PlayActionSound(seat, action);
        CancelRecordHuPresentation();
        _recordHuPresentationCoroutine = StartCoroutine(CoPlayXueliuWinRecord(seat, tile, action == "hu_self", multi, from, recycle, qianggang));
        StartCoroutine(AutoNextActionAfterDelay(0.5f));
    }

    private IEnumerator CoPlayXueliuWinRecord(string seat, int tile, bool zimo, bool multi, string source, bool recycle, bool qianggang) {
        _recordHuPresentationActive = true;
        try {
            yield return Game3DManager.Instance.PlayXueliuWinTile(seat, tile, zimo, multi, source, recycle, qianggang, syncLiveState: false);
        } finally {
            EndRecordHuPresentation();
        }
    }

    private bool IsBloodBattleRecord() {
        if (IsXueliuRecord()) return false;
        string subRule = ReadGameTitleString(gameRecord.gameTitle, "sub_rule", "");
        if (subRule == "zhongyong/nanque") return true;
        if (ReadGameTitleString(gameRecord.gameTitle, "sub_rule", "") == GuobiaoGameState.BloodBattleSubRule) return true;
        if (!IsSichuanRecord()) return false;
        return ReadGameTitleBool(gameRecord.gameTitle, "blood_battle", true);
    }

    private bool TryParseGangScoreChangesFromTick(List<string> tick, out Dictionary<int, int> changes) {
        changes = null;
        if (!UsesSichuanSettlementRecord() && RecordManifest()?.RecordUsesInlineKongScores != true) return false;
        int[] arr = GameRecordJsonDecoder.ParseInlineGangScoreChanges(tick);
        if (arr == null) return false;
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
        GameCanvas.Instance.ShowGangScoreFloats(changes, indexToPosition);
    }

    private bool ApplyRecordGangRefundState(List<string> tick) {
        if (tick == null || tick.Count < 6 || tick[1] != "gs") return false;
        var arr = new int[4];
        for (int i = 0; i < 4; i++) {
            if (!int.TryParse(tick[2 + i]?.Trim(), out arr[i])) return false;
        }
        var changes = new Dictionary<int, int>();
        MapTickScoreChangesToDeltas(arr, changes);
        if (!GameCanvas.HasNonZeroGangScoreChanges(changes)) return false;
        ApplyScoreDeltas(changes, out _, out _);
        return true;
    }

    private void PlayRecordGangRefundTick(List<string> tick) {
        if (!ApplyRecordGangRefundState(tick)) return;
        Dictionary<int, int> after = new Dictionary<int, int>();
        foreach (var rp in recordPlayerList) {
            after[rp.playerIndex] = rp.score;
        }
        var changes = new Dictionary<int, int>();
        var arr = new int[4];
        for (int i = 0; i < 4; i++) {
            int.TryParse(tick[2 + i]?.Trim(), out arr[i]);
        }
        MapTickScoreChangesToDeltas(arr, changes);
        BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
        GameCanvas.Instance.ShowGangScoreFloats(changes, indexToPosition);
    }

    /// <summary>血战中途和牌：仅当 score_changes 全 0 时视为延迟入账。</summary>
    private static bool IsDeferredSichuanHuScore(int huScore, int[] scoreChanges) {
        if (scoreChanges == null) return true;
        foreach (int c in scoreChanges) {
            if (c != 0) return false;
        }
        return true;
    }

    private static bool ContainsSichuanQianggangFan(string[] huFan) {
        if (huFan == null) return false;
        for (int i = 0; i < huFan.Length; i++) {
            if (huFan[i] == "抢杠" || huFan[i] == "抢杠和" || huFan[i] == "chankan") return true;
            if (huFan[i]?.StartsWith("GD|") == true && huFan[i].EndsWith("|抢杠")) return true;
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
        // 荣和回收河牌与吃碰一致：末张优先 + 同步摸切/横置并行数组
        RemoveClaimedDiscardFromRecordRiver(rp, tileId, capturePendingRiichiHorizontal: false);
    }

    private string ResolveRecordRonDiscarderPosition(int? ronDiscarderIndex) {
        if (ronDiscarderIndex.HasValue && indexToPosition.TryGetValue(ronDiscarderIndex.Value, out string fromTick)) {
            return fromTick;
        }
        if (lastJiagangPlayerIndex >= 0 && indexToPosition.TryGetValue(lastJiagangPlayerIndex, out string jgPos)) {
            return jgPos;
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
        CancelRecordHuPresentation();
        _recordHuPresentationCoroutine = StartCoroutine(CoPlaySichuanMidGameHuRecord(
            action, hepaiPlayerIndex, hepaiTile, multiRon, ronDiscarderIndex, recycleDiscard, isQianggang));
    }

    private IEnumerator CoPlaySichuanMidGameHuRecord(
        string action, int hepaiPlayerIndex, int hepaiTile, bool multiRon,
        int? ronDiscarderIndex, bool recycleDiscard, bool isQianggang) {
        _recordHuPresentationActive = true;
        try {
            string discardPos = ResolveRecordRonDiscarderPosition(ronDiscarderIndex);
            if (action != "hu_self") {
                int syncTile = hepaiTile >= 10 ? hepaiTile : lastWinnableTileId;
                Game3DManager.Instance.SyncRecordLastDiscardForRon(discardPos, syncTile);
            }
            if (!indexToPosition.TryGetValue(hepaiPlayerIndex, out string winnerPos)) yield break;

            HepaiPresentationRequest request = HepaiRevealDirector.BuildSichuanMidGameRequest(
                winnerPos, action, hepaiTile, multiRon, ronDiscarderIndex, recycleDiscard, isQianggang);
            request.DiscardPlayerPosition = discardPos;
            yield return Game3DManager.Instance.PlaySichuanMidGameHu(request);
            if (action != "hu_self" && recycleDiscard && !isQianggang) {
                SyncRecordRonDiscardRemoved(discardPos, hepaiTile);
            }
            yield return new WaitForSeconds(RoundEndTiming.RoundEndHandRevealSeconds);
        } finally {
            EndRecordHuPresentation();
        }
    }

    /// <summary>跳转推演：只改分数，不播结算动画、不自动步进。</summary>
    private void ApplySichuanLiujuStepState(List<string> tick) {
        if (tick == null || tick.Count < 2) return;
        switch (tick[1]) {
            case "reveal_hu":
                if (IsBloodBattleRecord() && tick.Count > 2) {
                    bloodRecordRevealedHands = ParseRecordHuHandsJson(tick[2]);
                    sichuanRecordRevealHands = bloodRecordRevealedHands;
                }
                break;
            case "settle_hu":
                if (tick.Count < 7) return;
                ApplyScoreDeltasFromTickIndex(tick, 6);
                break;
            case "chajiao":
                if (tick.Count < 6) return;
                ApplyScoreDeltasFromTickIndex(tick, 5);
                break;
            case "cha_refund":
                if (tick.Count < 3) return;
                ApplyScoreDeltasFromTickIndex(tick, 2);
                break;
            case "final":
                HandleSichuanRecordFinal(tick);
                break;
        }
    }

    private void ApplyScoreDeltasFromTickIndex(List<string> tick, int scoreIndex) {
        int[] changesArr = ParseTickScoreChanges(tick, scoreIndex);
        var deltas = new Dictionary<int, int>();
        if (changesArr != null) MapTickScoreChangesToDeltas(changesArr, deltas);
        ApplyScoreDeltas(deltas, out _, out _);
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
        sichuanRecordLedger.Begin();
        Dictionary<int, int[]> allHands = ParseRecordHuHandsJson(tick[2]);
        sichuanRecordRevealHands = allHands;
        if (IsBloodBattleRecord()) bloodRecordRevealedHands = allHands;
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

        sichuanRecordLedger.Accumulate(deltas);
        sichuanRecordLedger.RecordHu();
        bool isFinal = tick.Count > 7 && ParseTickInt(tick, 7) != 0;
        if (isFinal && TryFlushSichuanRecordLedger()) {
            GameSceneUIManager.Instance?.UpdateScoreRecord();
        }

        int[] hand = ResolveSichuanRecordHuHand(winner);
        int[][] melds = ResolveRecordCombinationMasks(winner);
        RecordSettlementView recordView = BuildRecordSettlementView(before, after);
        bool waitForRecordConfirm = !(IsSpectating && IsLiveSpectatorMode);
        RoundEndPresentation.Instance.EnqueueSichuanSettleHu(
            winner, after, huScore, huFan, huClass,
            hand, melds, deltas, isFinalPanel: isFinal, recordView: recordView,
            waitForRecordConfirm: waitForRecordConfirm);
        if (!waitForRecordConfirm) {
            float wait = RoundEndTiming.GetSichuanSettleHuPanelDuration(huFan?.Length ?? 0, isFinalPanel: isFinal);
            StartCoroutine(AutoNextActionAfterDelay(wait));
        }
    }

    private void HandleSichuanRecordChajiao(List<string> tick) {
        if (tick.Count < 6) return;
        int focusIndex = ParseTickInt(tick, 2);
        string statusKey = tick[3];
        int[] hand = ParseRecordHandJson(tick[4]);
        int[] changesArr = ParseTickScoreChanges(tick, 5);
        var scoreChanges = new Dictionary<int, int>();
        if (changesArr != null) MapTickScoreChangesToDeltas(changesArr, scoreChanges);
        ApplyScoreDeltas(scoreChanges, out Dictionary<int, int> before, out Dictionary<int, int> after);
        BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
        bool isFinal = tick.Count > 6 && ParseTickInt(tick, 6) != 0;

        sichuanRecordLedger.MarkChajiaoStep();
        sichuanRecordLedger.Accumulate(scoreChanges);
        if (isFinal && TryFlushSichuanRecordLedger()) {
            GameSceneUIManager.Instance?.UpdateScoreRecord();
        }

        if (hand == null || hand.Length == 0) {
            hand = ResolveSichuanRecordHuHand(focusIndex);
        }
        int[][] melds = ResolveRecordCombinationMasks(focusIndex);
        RecordSettlementView recordView = BuildRecordSettlementView(before, after);
        RoundEndPresentation.Instance.EnqueueSichuanChajiao(
            focusIndex, statusKey, hand, melds, after, scoreChanges, isFinal, recordView: recordView);
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

    private void HandleSichuanRecordChaRefund(List<string> tick) {
        if (tick.Count < 3) return;
        int[] changesArr = ParseTickScoreChanges(tick, 2);
        var scoreChanges = new Dictionary<int, int>();
        if (changesArr != null) MapTickScoreChangesToDeltas(changesArr, scoreChanges);
        ApplyScoreDeltas(scoreChanges, out Dictionary<int, int> before, out Dictionary<int, int> after);
        BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
        if (GameCanvas.HasNonZeroGangScoreChanges(scoreChanges)) {
            GameCanvas.Instance.ShowGangScoreFloats(scoreChanges, indexToPosition);
        }
        RecordSettlementView recordView = BuildRecordSettlementView(before, after);
        RoundEndPresentation.Instance.EnqueueSichuanChaRefund(after, scoreChanges, isFinalPanel: true, recordView: recordView);
        GameSceneUIManager.Instance?.UpdateScoreRecord();
        float wait = RoundEndTiming.RoundEndPresentationFadeSeconds + RoundEndTiming.HuConfirmCountdownSeconds;
        StartCoroutine(AutoNextActionAfterDelay(wait));
    }

    private RecordSettlementView BuildRecordSettlementView(Dictionary<int, int> before, Dictionary<int, int> after) {
        var names = new Dictionary<string, string>();
        foreach (var kv in recordPlayer_to_info) {
            int uid = kv.Value.userId;
            names[kv.Key] = userIdToUsername.TryGetValue(uid, out string name) ? name : uid.ToString();
        }
        return new RecordSettlementView {
            IndexToPosition = indexToPosition,
            PositionToUsername = names,
            ScoresBefore = before,
            ScoresAfter = after,
        };
    }

    private int[] ResolveSichuanRecordHuHand(int playerIndex) {
        if (sichuanRecordRevealHands != null
            && sichuanRecordRevealHands.TryGetValue(playerIndex, out int[] cached)
            && cached != null && cached.Length > 0) {
            return cached;
        }
        if (indexToPosition.TryGetValue(playerIndex, out string pos)
            && recordPlayer_to_info.TryGetValue(pos, out RecordPlayer rp)
            && rp.tileList != null && rp.tileList.Count > 0) {
            return rp.tileList.ToArray();
        }
        return null;
    }

    private int[][] ResolveRecordCombinationMasks(int playerIndex) {
        if (!indexToPosition.TryGetValue(playerIndex, out string pos)) return null;
        if (!recordPlayer_to_info.TryGetValue(pos, out RecordPlayer rp)) return null;
        if (rp.combinationMasks == null || rp.combinationMasks.Count == 0) return null;
        return rp.combinationMasks.ToArray();
    }

    private void HandleSichuanRecordFinal(List<string> tick) {
        if (tick.Count >= 3) {
            int[] scores = ParseTickScoreChanges(tick, 2);
            if (scores != null) {
                var after = new Dictionary<int, int>();
                for (int i = 0; i < scores.Length && i < 4; i++) after[i] = scores[i];
                foreach (var rp in recordPlayerList) {
                    if (after.TryGetValue(rp.playerIndex, out int score)) {
                        rp.score = score;
                        userIdToScore[rp.userId] = score;
                    }
                }
                BoardCanvas.Instance.UpdatePlayerScores(after, indexToPosition);
            }
        }
    }
}
