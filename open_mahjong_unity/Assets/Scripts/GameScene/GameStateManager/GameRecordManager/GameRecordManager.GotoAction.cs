using System.Collections.Generic;
using UnityEngine;

public partial class GameRecordManager
{
    public void GotoAction(int actionIndex){
        CancelRecordHuPresentation();
        // 获取当前局数的node列表
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData) || roundData.actionTicks == null) {
            return;
        }
        int safeActionIndex = Mathf.Clamp(actionIndex, 0, roundData.actionTicks.Count);

        // 重新推理手牌前清空所有正在执行的3D动画，避免与重建画面冲突
        Game3DManager.Instance.StopAllRunningAnimations();
        // 跳转/快进会中断渐隐协程，先销毁残留的操作文本，避免「补花」等字卡死
        GameCanvas.Instance.ClearActionDisplay();
        // 重置到局初始状态（UI/2D/3D）
        Game3DManager.Instance.Clear3DTile();
        currentNode = 0;
        currentPlayerIndex = roundData.startPlayerIndex;
        lastDiscardPlayerIndex = -1;
        lastDiscardTileId = -1;
        lastWinnableTileId = -1;
        waitingForDrawAfterCut = false;
        
        // 重置牌山列表到初始状态
        if (roundData.tilesList != null) {
            currentTilesList = new List<int>(roundData.tilesList);
        } else {
            currentTilesList = new List<int>();
        }
        consumedFromFront = 0;
        consumedBackIndices = new HashSet<int>();
        currentOriginalIndices = new List<int>();
        for (int i = 0; i < originalTilesList.Count; i++) currentOriginalIndices.Add(i);
        backwardTilesType = "double";
        ResetRecordRiichiFieldState(roundData);

        // 重置 RecordPlayer.score 到本局开始时的累计分数
        if (gameRecord?.gameRound?.rounds != null) {
            int[] cumulativeByOrig = new int[4];
            for (int r = 1; r < currentRoundIndex; r++) {
                if (gameRecord.gameRound.rounds.TryGetValue(r, out Round prevRound) &&
                    prevRound.scoreChanges != null && prevRound.scoreChanges.Count >= 4) {
                    for (int p = 0; p < 4; p++) cumulativeByOrig[p] += prevRound.scoreChanges[p];
                }
            }
            foreach (var rp in recordPlayerList) {
                rp.score = cumulativeByOrig[rp.originalPlayerIndex];
                userIdToScore[rp.userId] = rp.score;
            }
        }
        
        GotoSelectPlayer(false);

        // 推演当前目标节点信息
        for (int i = 0; i < safeActionIndex; i++) {
            ApplyActionToRecordState(roundData.actionTicks[i], i);
            currentNode++;
        }

        // 重建当前目标节点画面
        Game3DManager.Instance.Clear3DTile();
        GameCanvas.Instance.ChangeHandCards("InitHandCardsFromRecord", 0, recordPlayer_to_info["self"].tileList.ToArray(), null);
        
        Game3DManager.Instance.Change3DTile("InitHandCardsFromRecord", 0, 0, null, false, null);
        RebuildRecord3DTableWithoutAnimation();
        if (recordRiichiTenbousClearedAfterHu) {
            Game3DManager.Instance.ClearAllRiichiTenbous();
        }
        BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex], currentTilesList.Count);
        RefreshTileListViewIfVisible();
        RefreshRecordRiichiRoundPanel();
        UpdateCurrentXunmuText();
        RefreshRecordChongHint();
        RefreshRecordTips();
    }

    // 推演行动节点
    private void ApplyActionToRecordState(List<string> tick, int nodeIndex) {
        if (tick == null || tick.Count == 0) {
            return;
        }

        string action = tick[0];

        // 观战 ask 事件不改变对局状态，快进时跳过
        if (action == "ask_hand" || action == "ask_other" || action == "ca") {
            return;
        }

        if (nodeIndex == startIndex) {
            if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData)) {
                currentPlayerIndex = roundData.startPlayerIndex;
            }
        }

        int actingPlayerIndex = GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(tick, action, currentPlayerIndex);

        string actingPlayerPosition = indexToPosition[actingPlayerIndex];
        RecordPlayer actingPlayer = recordPlayer_to_info[actingPlayerPosition];
        int nextPlayerIndex = currentPlayerIndex;

        if (action == "d" || action == "gd" || action == "bd") {
            int dealTile = ParseTickInt(tick, 1);
            actingPlayer.tileList.Add(dealTile);
            actingPlayer.showHandDrawSlotActive = true;
            waitingForDrawAfterCut = false;
            
            if (currentTilesList.Count > 0) {
                if (action == "gd" || action == "bd") {
                    int removePos;
                    if (backwardTilesType == "double" && currentTilesList.Count > 1) {
                        removePos = currentTilesList.Count - 2;
                    } else {
                        removePos = currentTilesList.Count - 1;
                    }
                    int origIdx = currentOriginalIndices[removePos];
                    currentTilesList.RemoveAt(removePos);
                    currentOriginalIndices.RemoveAt(removePos);
                    consumedBackIndices.Add(origIdx);
                    backwardTilesType = backwardTilesType == "double" ? "single" : "double";
                } else {
                    currentTilesList.RemoveAt(0);
                    currentOriginalIndices.RemoveAt(0);
                    consumedFromFront++;
                }
            }
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "c") {
            int cutTile = ParseTickInt(tick, 1);
            bool isMoqie = ParseTickBool(tick, 2);
            bool isRiichiHorizontal = tick.Count > 3 && tick[3] == "H";
            RemoveTileForCut(actingPlayer.tileList, cutTile, isMoqie);
            actingPlayer.showHandDrawSlotActive = false;
            actingPlayer.discardTiles.Add(cutTile);
            actingPlayer.discardIsMoqie.Add(isMoqie);
            actingPlayer.discardRiichiFlags.Add(isRiichiHorizontal);
            lastDiscardPlayerIndex = actingPlayerIndex;
            lastDiscardTileId = cutTile;
            lastWinnableTileId = cutTile;
            waitingForDrawAfterCut = true;
            OnRecordPlayerCut(actingPlayer);
            nextPlayerIndex = (actingPlayerIndex + 1) % 4;
        }
        else if (action == "bh") {
            int buhuaTile = ParseTickInt(tick, 1);
            bool isMoBuhua = GameRecordJsonDecoder.ParseBuhuaMoFlag(tick);
            RemoveTileForBuhua(actingPlayer.tileList, buhuaTile, isMoBuhua);
            if (isMoBuhua) {
                actingPlayer.showHandDrawSlotActive = false;
            }
            actingPlayer.huapaiList.Add(buhuaTile);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "ag") {
            int angangTile = ParseTickInt(tick, 1);
            bool isMoGang = GameRecordJsonDecoder.ParseKanMoGangFlag(tick);
            string rule = ReadGameTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
            List<int> removedTiles = GameRecordMeldCodec.ResolveAngangRemovedTiles(
                tick, actingPlayer.tileList, angangTile, isMoGang);
            int[] combinationMask = GameRecordMeldCodec.BuildAngangMaskFromRemoved(removedTiles, rule);
            actingPlayer.combinationTiles.Add($"G{angangTile}");
            actingPlayer.combinationMasks.Add(combinationMask);
            if (isMoGang) {
                actingPlayer.showHandDrawSlotActive = false;
            }
            ApplyRecordGangScoreDeltasFromTick(tick);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "jg") {
            int jiagangTile = ParseTickInt(tick, 1);
            bool isMoGang = GameRecordJsonDecoder.ParseKanMoGangFlag(tick);
            List<int> removedTiles = GameRecordMeldCodec.RemoveNTilesByNormalized(
                actingPlayer.tileList, jiagangTile, 1, preferDrawSlotFirst: isMoGang);
            int actualJia = removedTiles.Count > 0 ? removedTiles[0] : jiagangTile;
            lastWinnableTileId = actualJia;
            BuildJiagangMask(actingPlayer, jiagangTile, actualJia);
            if (isMoGang) {
                actingPlayer.showHandDrawSlotActive = false;
            }
            ApplyRecordGangScoreDeltasFromTick(tick);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g") {
            int mingpaiTile = ParseTickInt(tick, 1);
            List<int> removedTiles = GameRecordMeldCodec.ResolveHandTiles(tick, action, mingpaiTile);
            foreach (int tileId in removedTiles) {
                RemoveOneTile(actingPlayer.tileList, tileId);
            }
            lastWinnableTileId = -1;

            if (lastDiscardPlayerIndex >= 0 && indexToPosition.ContainsKey(lastDiscardPlayerIndex)) {
                string discardPlayerPosition = indexToPosition[lastDiscardPlayerIndex];
                var dpRecord = recordPlayer_to_info[discardPlayerPosition];
                RemoveOneTile(dpRecord.discardTiles, mingpaiTile);
                if (dpRecord.discardRiichiFlags.Count > 0){
                    dpRecord.discardRiichiFlags.RemoveAt(dpRecord.discardRiichiFlags.Count - 1);
                }
            }

            int discardPlayerIndex = lastDiscardPlayerIndex >= 0 ? lastDiscardPlayerIndex : currentPlayerIndex;
            string relative = GetRelativePosition(actingPlayerIndex, discardPlayerIndex);
            int[] combinationMask = GameRecordMeldCodec.BuildMingpaiMask(action, mingpaiTile, removedTiles, relative);
            actingPlayer.combinationTiles.Add(GameRecordMeldCodec.BuildCombinationTarget(action, mingpaiTile));
            actingPlayer.combinationMasks.Add(combinationMask);
            actingPlayer.showHandDrawSlotActive = false;
            if (action == "g") {
                ApplyRecordGangScoreDeltasFromTick(tick);
            }
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third") {
            int hepaiPlayerIndex = ParseTickInt(tick, 1);
            MarkRecordPlayerHu(hepaiPlayerIndex);
            int[] sc = ParseTickScoreChanges(tick, 4);
            if (sc != null && sc.Length >= 4) {
                var deltas = new Dictionary<int, int>();
                MapTickScoreChangesToDeltas(sc, deltas);
                ApplyScoreDeltas(deltas, out _, out _);
            }
        }
        else if (action == "hu_riichi") {
            int[] sc = tick.Count > 6 ? ParseTickScoreChanges(tick, 6) : null;
            if (sc != null && sc.Length >= 4) {
                var deltas = new Dictionary<int, int>();
                MapTickScoreChangesToDeltas(sc, deltas);
                ApplyScoreDeltas(deltas, out _, out _);
            }
            int riichiSticksCollected = tick.Count > 11 ? ParseTickInt(tick, 11) : 0;
            ApplyRecordRiichiSticksCollected(riichiSticksCollected);
        }
        else if (action == "dora") {
            ApplyRecordRiichiDoraTick(ParseTickInt(tick, 1));
        }
        else if (action == "ryuukyoku") {
            int[] sc = ParseTickScoreChanges(tick, 2);
            if (sc != null && sc.Length >= 4) {
                var deltas = new Dictionary<int, int>();
                MapTickScoreChangesToDeltas(sc, deltas);
                ApplyScoreDeltas(deltas, out _, out _);
            }
        }
        else if (action == "shuhewei") {
            int[] changesArray = ParseTickScoreChanges(tick, 2);
            if (changesArray != null && changesArray.Length >= 4) {
                var deltas = new Dictionary<int, int>();
                foreach (var rp in recordPlayerList) {
                    deltas[rp.playerIndex] = changesArray[rp.originalPlayerIndex];
                }
                ApplyScoreDeltas(deltas, out _, out _);
            }
        }
        else if (action == "riichi") {
            // 跳转重建路径同样需要标记立直，便于 RebuildRecord3DTableWithoutAnimation 复原立直棒
            int riichiPlayer = ParseTickInt(tick, 1);
            foreach (var rp in recordPlayerList){
                if (rp.playerIndex == riichiPlayer){ rp.isRiichi = true; break; }
            }
            ApplyRecordRiichiDeclare();
        }

        currentPlayerIndex = nextPlayerIndex;
    }

    private void RebuildRecord3DTableWithoutAnimation() {
        foreach (var kv in recordPlayer_to_info) {
            string position = kv.Key;
            RecordPlayer player = kv.Value;

            for (int i = 0; i < player.discardTiles.Count; i++) {
                bool moqie = i < player.discardIsMoqie.Count && player.discardIsMoqie[i];
                bool horizontal = i < player.discardRiichiFlags.Count && player.discardRiichiFlags[i];
                Game3DManager.Instance.Change3DTile("SetRecordDiscardWithoutAnimation", player.discardTiles[i], 0, position, moqie, null, horizontal);
            }
            // 跳转回放：已立直的玩家立刻放置立直棒（不播放飞行动画）
            if (player.isRiichi){
                Game3DManager.Instance.PlaceRiichiTenbouAt(position);
            }
            foreach (int tileId in player.huapaiList) {
                Game3DManager.Instance.Change3DTile("SetBuhuacardWithoutAnimation", tileId, 0, position, false, null);
            }

            for (int i = 0; i < player.combinationTiles.Count && i < player.combinationMasks.Count; i++) {
                string combinationStr = player.combinationTiles[i];
                int[] combinationMask = player.combinationMasks[i];
                if (string.IsNullOrEmpty(combinationStr) || combinationMask == null || combinationMask.Length == 0) {
                    continue;
                }

                int jiagangCount = 0;
                foreach (int value in combinationMask) {
                    if (value == 3) {
                        jiagangCount++;
                    }
                }

                if (combinationStr.Contains("k")) {
                    Game3DManager.Instance.StartCoroutine(Game3DManager.Instance.ActionAnimationCoroutine(position, "peng", combinationMask, false));
                }
                else if (jiagangCount > 0) {
                    Game3DManager.Instance.StartCoroutine(Game3DManager.Instance.ActionAnimationCoroutine(position, "peng", combinationMask, false));
                    Game3DManager.Instance.StartCoroutine(Game3DManager.Instance.ActionAnimationCoroutine(position, "jiagang", combinationMask, false));
                }
                else {
                    Game3DManager.Instance.StartCoroutine(Game3DManager.Instance.ActionAnimationCoroutine(position, "None", combinationMask, false));
                }
            }
        }
    }
}
