using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 牌谱铳牌/自摸提示：听牌计算、放铳危险牌、下一摸预测。
/// </summary>
public static class RecordChongHintCalculator {
  /// <summary>合并所有未和牌玩家的待牌（用于牌山铳牌标红）。</summary>
  public static HashSet<int> ComputeDangerTiles(
    Dictionary<string, GameRecordManager.RecordPlayer> players,
    string roomRule) {
    var danger = new HashSet<int>();
    if (players == null || string.IsNullOrEmpty(roomRule)) return danger;

    foreach (var kv in players) {
      if (kv.Value == null || kv.Value.isHu) continue;
      foreach (int tileId in ComputeWaitingTilesForPlayer(kv.Value, roomRule)) {
        danger.Add(tileId);
      }
    }
    return danger;
  }

  /// <summary>
  /// 某玩家手牌上的放铳危险牌：仅统计其他玩家的待牌（不含自身听牌，避免误标自摸张）。
  /// </summary>
  public static HashSet<int> ComputeRonDangerForHandOwner(
    Dictionary<string, GameRecordManager.RecordPlayer> players,
    string handOwnerPosition,
    string roomRule) {
    var danger = new HashSet<int>();
    if (players == null || string.IsNullOrEmpty(roomRule) || string.IsNullOrEmpty(handOwnerPosition)) {
      return danger;
    }

    foreach (var kv in players) {
      if (kv.Key == handOwnerPosition) continue;
      if (kv.Value == null || kv.Value.isHu) continue;
      foreach (int tileId in ComputeWaitingTilesForPlayer(kv.Value, roomRule)) {
        danger.Add(tileId);
      }
    }
    return danger;
  }

  public static HashSet<int> ComputeWaitingTilesForPlayer(
    GameRecordManager.RecordPlayer player,
    string roomRule) {
    if (player == null) return new HashSet<int>();

    List<int> handForCheck = NormalizeHandForTingpai(player.tileList);
    if (handForCheck == null) return new HashSet<int>();

    List<string> combinations = player.combinationTiles ?? new List<string>();
    HashSet<int> waitingTiles;

    try {
      if (roomRule == "guobiao") {
        waitingTiles = GBtingpai.TingpaiCheck(handForCheck, combinations, false);
      }
      else if (roomRule == "qingque") {
        waitingTiles = Qingque13External.TingpaiCheck(handForCheck, combinations, false);
      }
      else if (roomRule == "classical") {
        waitingTiles = ClassicalExternal.TingpaiCheck(handForCheck, combinations, false);
      }
      else if (roomRule == "riichi") {
        waitingTiles = RiichiExternal.TingpaiCheck(handForCheck, combinations, false);
      }
      else if (roomRule == "sichuan") {
        waitingTiles = SichuanExternal.TingpaiCheck(handForCheck, combinations);
        int dingque = player.dingqueSuit;
        if (dingque == 1 || dingque == 2 || dingque == 3) {
          waitingTiles.RemoveWhere(w => (w / 10) == dingque);
        }
      }
      else if (roomRule == "changsha") {
        waitingTiles = ChangshaExternal.TingpaiCheck(handForCheck, combinations);
      }
      else if (roomRule == "jiandan") {
        waitingTiles = JiandanExternal.TingpaiCheck(handForCheck, combinations);
      }
      else {
        waitingTiles = new HashSet<int>();
      }
    }
    catch (Exception e) {
      Debug.LogWarning($"[RecordChongHint] 听牌计算失败 rule={roomRule}: {e.Message}");
      waitingTiles = new HashSet<int>();
    }

    var normalized = new HashSet<int>();
    foreach (int tileId in waitingTiles) {
      normalized.Add(TileIdOrder.Normalize(tileId));
    }
    return normalized;
  }

  /// <summary>
  /// 听牌 API 需要不含和牌张的手牌；刚摸牌未切时去掉最后一张（摸牌位）。
  /// </summary>
  public static List<int> NormalizeHandForTingpai(List<int> handTiles) {
    if (handTiles == null || handTiles.Count == 0) return null;

    int count = handTiles.Count;
    if ((count - 1) % 3 == 0) {
      return new List<int>(handTiles);
    }
    if (count % 3 == 2) {
      var trimmed = new List<int>(handTiles);
      trimmed.RemoveAt(trimmed.Count - 1);
      return trimmed;
    }
    return null;
  }

  private const int MoqieZimoDrawInterval = 4;
  private const int MoqieZimoDrawPredictionCount = 6;

  /// <summary>
  /// 摸切假设下的自摸预测：从牌山头部下一张起算，间隔 4 张标蓝（不模拟补花/鸣牌）。
  /// 锚点为当前操作玩家；出牌后 current 已切到下家，此时仍按出牌者计（lastDiscard）。
  /// 例：下家摸牌/出牌时 offset=3，标 2、6、10、14、18、22（相对 front 的下一张）。
  /// </summary>
  public static void ComputeMoqieZimoDrawOriginalIndices(
    GameRecordManager mgr,
    ICollection<int> outIndices) {
    outIndices?.Clear();
    if (mgr == null || outIndices == null) return;

    int wallCount = mgr.OriginalWallTileCountForChongHint;
    if (wallCount <= 0) return;

    int selfIndex = mgr.selectedPlayerIndex;
    int anchorIndex = mgr.currentPlayerIndex;
    if (mgr.IsWaitingForDrawAfterCutForChongHint && mgr.LastDiscardPlayerIndexForChongHint >= 0) {
      anchorIndex = mgr.LastDiscardPlayerIndexForChongHint;
    }
    int offset = (selfIndex - anchorIndex + MoqieZimoDrawInterval) % MoqieZimoDrawInterval;

    int front = mgr.ConsumedFromFrontForChongHint;
    for (int n = 0; n < MoqieZimoDrawPredictionCount; n++) {
      int originalIndex = front + offset + n * MoqieZimoDrawInterval - 1;
      if (originalIndex < 0) continue;
      if (originalIndex >= wallCount) break;
      if (originalIndex < front) continue;
      if (mgr.IsOriginalWallIndexBackConsumed(originalIndex)) continue;
      outIndices.Add(originalIndex);
    }
  }

  public static bool IsRiichiDeadWallBlockingZimo(
    string roomRule,
    int originalIndex,
    int wallTileCount,
    IReadOnlyCollection<int> consumedBackIndices,
    int revealedKanDoraCount,
    int rinshanCount) {
    if (roomRule != "riichi" || wallTileCount <= 0) return false;
    return BuildRiichiDeadWallBlockingIndices(
      wallTileCount, consumedBackIndices, revealedKanDoraCount, rinshanCount).Contains(originalIndex);
  }

  /// <summary>
  /// 日麻王牌区：从牌山末尾向前取 14 张，跳过已翻开的杠宝牌指示牌位（与服务端 _reveal_kan_dora 布局一致）。
  /// </summary>
  public static HashSet<int> BuildRiichiDeadWallBlockingIndices(
    int wallTileCount,
    IReadOnlyCollection<int> consumedBackIndices,
    int revealedKanDoraCount,
    int rinshanCount) {
    var blocking = new HashSet<int>();
    if (wallTileCount <= 0) return blocking;

    int need = 14;
    for (int i = wallTileCount - 1; i >= 0 && need > 0; i--) {
      if (IsRevealedKanDoraIndicatorIndex(i, wallTileCount, revealedKanDoraCount, rinshanCount)) {
        continue;
      }
      blocking.Add(i);
      need--;
    }
    return blocking;
  }

  private static bool IsRevealedKanDoraIndicatorIndex(
    int originalIndex,
    int wallTileCount,
    int revealedKanDoraCount,
    int rinshanCount) {
    for (int k = 1; k <= revealedKanDoraCount; k++) {
      int kanIdx = wallTileCount - (6 + 2 * k) + rinshanCount;
      if (kanIdx == originalIndex) return true;
    }
    return false;
  }

  public static void TryInferRecordDingqueSuit(GameRecordManager.RecordPlayer player) {
    if (player == null || player.dingqueSuit != 0) return;

    int missingSuit = 0;
    int missingCount = 0;
    for (int suit = 1; suit <= 3; suit++) {
      if (!PlayerHasSuit(player, suit)) {
        missingCount++;
        missingSuit = suit;
      }
    }
    if (missingCount == 1) {
      player.dingqueSuit = missingSuit;
    }
  }

  private static bool PlayerHasSuit(GameRecordManager.RecordPlayer player, int suit) {
    foreach (int tileId in player.tileList) {
      if (tileId / 10 == suit) return true;
    }
    foreach (string combo in player.combinationTiles) {
      if (GameRecordMeldCodec.NormalizeCombinationTileId(combo) / 10 == suit) return true;
    }
    return false;
  }
}
