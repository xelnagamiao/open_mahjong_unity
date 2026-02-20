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
        
        // 重置牌山列表到初始状态
        if (roundData.tilesList != null) {
            currentTilesList = new List<int>(roundData.tilesList);
        } else {
            currentTilesList = new List<int>();
        }
        lastActionBeforeDeal = "";
        consumedFromFront = 0;
        consumedFromBack = 0;
        
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
    }

    // 推演行动节点
    private void ApplyActionToRecordState(List<string> tick, int nodeIndex) {
        if (tick == null || tick.Count == 0) {
            return;
        }

        string action = tick[0];
        if (nodeIndex == startIndex) {
            currentPlayerIndex = 0;
        }

        int actingPlayerIndex = currentPlayerIndex;
        if (action == "buhua" && tick.Count >= 3) {
            actingPlayerIndex = ParseTickInt(tick, 2);
        } else if ((action == "chi_left" || action == "chi_mid" || action == "chi_right" || action == "peng" || action == "gang") && tick.Count >= 3) {
            actingPlayerIndex = ParseTickInt(tick, 2);
        }

        string actingPlayerPosition = indexToPosition[actingPlayerIndex];
        RecordPlayer actingPlayer = recordPlayer_to_info[actingPlayerPosition];
        int nextPlayerIndex = currentPlayerIndex;

        if (action == "deal") {
            int dealTile = ParseTickInt(tick, 1);
            actingPlayer.tileList.Add(dealTile);
            
            // 处理牌山：根据前一个操作决定删除第一张还是最后一张
            if (currentTilesList.Count > 0) {
                if (lastActionBeforeDeal == "gang" || lastActionBeforeDeal == "angang" || lastActionBeforeDeal == "jiagang") {
                    // 杠后摸牌：删除最后一张
                    currentTilesList.RemoveAt(currentTilesList.Count - 1);
                    consumedFromBack++;
                } else {
                    // 普通摸牌：删除第一张
                    currentTilesList.RemoveAt(0);
                    consumedFromFront++;
                }
            }
            
            // 重置前一个操作标记
            lastActionBeforeDeal = "";
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "cut") {
            int cutTile = ParseTickInt(tick, 1);
            bool isMoqie = ParseTickBool(tick, 2);
            RemoveTileForCut(actingPlayer.tileList, cutTile, isMoqie);
            actingPlayer.discardTiles.Add(cutTile);
            lastDiscardPlayerIndex = actingPlayerIndex;
            nextPlayerIndex = (actingPlayerIndex + 1) % 4;
        }
        else if (action == "buhua") {
            int buhuaTile = ParseTickInt(tick, 1);
            RemoveOneTile(actingPlayer.tileList, buhuaTile);
            actingPlayer.huapaiList.Add(buhuaTile);
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "angang") {
            int angangTile = ParseTickInt(tick, 1);
            RemoveNTiles(actingPlayer.tileList, angangTile, 4);
            int[] combinationMask = new int[] { 2, angangTile, 2, angangTile, 2, angangTile, 2, angangTile };
            actingPlayer.combinationTiles.Add($"G{angangTile}");
            actingPlayer.combinationMasks.Add(combinationMask);
            lastActionBeforeDeal = "angang"; // 记录暗杠操作，下次摸牌时删除最后一张
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "jiagang") {
            int jiagangTile = ParseTickInt(tick, 1);
            RemoveNTiles(actingPlayer.tileList, jiagangTile, 1);
            BuildJiagangMask(actingPlayer, jiagangTile);
            lastActionBeforeDeal = "jiagang"; // 记录加杠操作，下次摸牌时删除最后一张
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "chi_left" || action == "chi_mid" || action == "chi_right" || action == "peng" || action == "gang") {
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
            if (action == "gang") {
                lastActionBeforeDeal = "gang"; // 记录明杠操作，下次摸牌时删除最后一张
            } else {
                // 吃、碰操作重置标记（因为它们不是杠操作）
                lastActionBeforeDeal = "";
            }
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "cut") {
            // cut 操作不重置 lastActionBeforeDeal，因为杠操作可能在 cut 之前
        }
        else if (action == "buhua") {
            // buhua 操作不重置 lastActionBeforeDeal，因为杠操作可能在 buhua 之前
        }
        else if (action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third" || action == "liuju") {
            // 和牌/流局操作：重置标记
            lastActionBeforeDeal = "";
        }
        // 其他未知操作不处理 lastActionBeforeDeal，保持当前状态

        currentPlayerIndex = nextPlayerIndex;
    }

    private void RebuildRecord3DTableWithoutAnimation() {
        foreach (var kv in recordPlayer_to_info) {
            string position = kv.Key;
            RecordPlayer player = kv.Value;

            foreach (int tileId in player.discardTiles) {
                Game3DManager.Instance.Change3DTile("SetDiscardWithoutAnimation", tileId, 0, position, false, null);
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
