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
        BoardCanvas.Instance.ShowCurrentPlayer(indexToPosition[currentPlayerIndex]);
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
            nextPlayerIndex = actingPlayerIndex;
        }
        else if (action == "jiagang") {
            int jiagangTile = ParseTickInt(tick, 1);
            RemoveNTiles(actingPlayer.tileList, jiagangTile, 1);
            BuildJiagangMask(actingPlayer, jiagangTile);
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
            nextPlayerIndex = actingPlayerIndex;
        }

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
