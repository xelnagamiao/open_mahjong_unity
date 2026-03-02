using System.Collections.Generic;
using UnityEngine;

public partial class GameRecordManager
{
    public void GotoAction(int actionIndex){
        // 获取当前局数的node列表
        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData) || roundData.actionTicks == null) {
            return;
        }
        int safeActionIndex = Mathf.Clamp(actionIndex, 0, roundData.actionTicks.Count);

        // 重置到局初始状态（UI/2D/3D）
        Game3DManager.Instance.Clear3DTile();
        currentNode = 0;
        currentPlayerIndex = 0;
        lastDiscardPlayerIndex = -1;
        lastDiscardTileId = -1;
        
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
        BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex], currentTilesList.Count);
        RefreshTileListViewIfVisible();
        UpdateCurrentXunmuText();
    }

    // 推演行动节点
    private void ApplyActionToRecordState(List<string> tick, int nodeIndex) {
        if (tick == null || tick.Count == 0) {
            return;
        }

        string action = tick[0];

        // 观战 ask 事件不改变对局状态，快进时跳过
        if (action == "ask_hand" || action == "ask_other") {
            return;
        }

        if (nodeIndex == startIndex) {
            currentPlayerIndex = 0;
        }

        int actingPlayerIndex = currentPlayerIndex;
        if (action == "bh" && tick.Count >= 3) {
            actingPlayerIndex = ParseTickInt(tick, 2);
        } else if ((action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g") && tick.Count >= 3) {
            actingPlayerIndex = ParseTickInt(tick, 2);
        }

        string actingPlayerPosition = indexToPosition[actingPlayerIndex];
        RecordPlayer actingPlayer = recordPlayer_to_info[actingPlayerPosition];
        int nextPlayerIndex = currentPlayerIndex;

        if (action == "d" || action == "gd" || action == "bd") {
            int dealTile = ParseTickInt(tick, 1);
            actingPlayer.tileList.Add(dealTile);
            
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
            RemoveTileForCut(actingPlayer.tileList, cutTile, isMoqie);
            actingPlayer.discardTiles.Add(cutTile);
            actingPlayer.discardIsMoqie.Add(isMoqie);
            lastDiscardPlayerIndex = actingPlayerIndex;
            lastDiscardTileId = cutTile;
            nextPlayerIndex = (actingPlayerIndex + 1) % 4;
        }
        else if (action == "bh") {
            int buhuaTile = ParseTickInt(tick, 1);
            RemoveOneTile(actingPlayer.tileList, buhuaTile);
            actingPlayer.huapaiList.Add(buhuaTile);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "ag") {
            int angangTile = ParseTickInt(tick, 1);
            RemoveNTiles(actingPlayer.tileList, angangTile, 4);
            int[] combinationMask = new int[] { 2, angangTile, 2, angangTile, 2, angangTile, 2, angangTile };
            actingPlayer.combinationTiles.Add($"G{angangTile}");
            actingPlayer.combinationMasks.Add(combinationMask);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "jg") {
            int jiagangTile = ParseTickInt(tick, 1);
            RemoveNTiles(actingPlayer.tileList, jiagangTile, 1);
            BuildJiagangMask(actingPlayer, jiagangTile);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g") {
            int mingpaiTile = ParseTickInt(tick, 1);
            List<int> removedTiles = BuildRemovedTilesForMingpai(action, mingpaiTile);
            foreach (int tileId in removedTiles) {
                RemoveOneTile(actingPlayer.tileList, tileId);
            }

            if (lastDiscardPlayerIndex >= 0 && indexToPosition.ContainsKey(lastDiscardPlayerIndex)) {
                string discardPlayerPosition = indexToPosition[lastDiscardPlayerIndex];
                RemoveOneTile(recordPlayer_to_info[discardPlayerPosition].discardTiles, mingpaiTile);
            }

            int discardPlayerIndex = lastDiscardPlayerIndex >= 0 ? lastDiscardPlayerIndex : currentPlayerIndex;
            int[] combinationMask = BuildMingpaiMask(action, mingpaiTile, actingPlayerIndex, discardPlayerIndex);
            actingPlayer.combinationTiles.Add(BuildCombinationTarget(action, mingpaiTile));
            actingPlayer.combinationMasks.Add(combinationMask);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third") {
            // tick 格式: [hu_class, hepai_player_index, hu_score, hu_fan_json, score_changes_json]
            int[] sc = ParseTickScoreChanges(tick, 4);
            if (sc != null && sc.Length >= 4) {
                var deltas = new Dictionary<int, int>();
                foreach (var rp in recordPlayerList) {
                    deltas[rp.playerIndex] = sc[rp.originalPlayerIndex];
                }
                ApplyScoreDeltas(deltas, out _, out _);
            }
        }
        // "end" 和 "liuju" 在快进模式下无需额外处理

        currentPlayerIndex = nextPlayerIndex;
    }

    private void RebuildRecord3DTableWithoutAnimation() {
        foreach (var kv in recordPlayer_to_info) {
            string position = kv.Key;
            RecordPlayer player = kv.Value;

            for (int i = 0; i < player.discardTiles.Count; i++) {
                bool moqie = i < player.discardIsMoqie.Count && player.discardIsMoqie[i];
                Game3DManager.Instance.Change3DTile("SetRecordDiscardWithoutAnimation", player.discardTiles[i], 0, position, moqie, null);
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
                    Game3DManager.Instance.ActionAnimation(position, "peng", combinationMask, false);
                }
                else if (jiagangCount > 0) {
                    Game3DManager.Instance.ActionAnimation(position, "peng", combinationMask, false);
                    Game3DManager.Instance.ActionAnimation(position, "jiagang", combinationMask, false);
                }
                else {
                    Game3DManager.Instance.ActionAnimation(position, "None", combinationMask, false);
                }
            }
        }
    }
}
