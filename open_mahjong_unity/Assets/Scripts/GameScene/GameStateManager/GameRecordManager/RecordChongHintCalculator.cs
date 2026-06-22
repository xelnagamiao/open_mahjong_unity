using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 牌谱铳牌/自摸提示：听牌计算、放铳危险牌、下一摸预测。
/// </summary>
public static class RecordChongHintCalculator {
  private static readonly HashSet<string> ChiPengGangActions = new HashSet<string> {
    "cl", "cm", "cr", "p", "g",
  };

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

  /// <summary>
  /// 自摸提示：自当前节点起，若无吃碰明杠，预测视角玩家下一次摸牌在 originalTilesList 中的索引。
  /// </summary>
  public static bool TryPredictNextSelfDrawOriginalIndex(
    GameRecordManager mgr,
    out int originalIndex) {
    originalIndex = -1;
    if (mgr == null || mgr.gameRecord?.gameRound?.rounds == null) return false;
    if (!mgr.gameRecord.gameRound.rounds.TryGetValue(mgr.currentRoundIndex, out Round roundData)) return false;
    if (roundData.actionTicks == null) return false;

    var wall = new WallSimState {
      currentTiles = new List<int>(mgr.GetCurrentTilesListForSim()),
      origIndices = new List<int>(mgr.GetCurrentOriginalIndicesForSim()),
      consumedBack = new HashSet<int>(mgr.GetConsumedBackIndicesForSim()),
      backwardType = mgr.GetBackwardTilesTypeForSim(),
    };

    int actingPlayer = mgr.currentPlayerIndex;
    int selfIndex = mgr.selectedPlayerIndex;

    for (int node = mgr.currentNode; node < roundData.actionTicks.Count; node++) {
      List<string> tick = roundData.actionTicks[node];
      if (tick == null || tick.Count == 0) continue;

      string action = tick[0];
      if (action == "ask_hand" || action == "ask_other") continue;

      actingPlayer = ResolveActingPlayerIndex(tick, action, actingPlayer);

      if (ChiPengGangActions.Contains(action)) {
        return false;
      }

      if (action == "d" || action == "gd" || action == "bd") {
        if (actingPlayer == selfIndex) {
          if (!TryPeekDrawOriginalIndex(wall, action, out originalIndex)) return false;
          return true;
        }
        if (!SimulateDraw(wall, action)) return false;
        continue;
      }

      if (action == "c") {
        actingPlayer = (actingPlayer + 1) % 4;
        continue;
      }

      if (action == "bh") {
        continue;
      }

      if (action == "ag" || action == "jg" || action == "riichi" || action == "dora") {
        continue;
      }

      if (action.StartsWith("hu") || action == "liuju" || action == "ryuukyoku"
          || action == "end" || action == "shuhewei" || action == "gr"
          || action == "jiuzhongjiupai" || action == "chajiao") {
        return false;
      }
    }

    return false;
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

  private static int ParseTickInt(List<string> tick, int index) {
    if (tick == null || index < 0 || index >= tick.Count) return 0;
    if (!int.TryParse(tick[index]?.Trim(), out int value)) return 0;
    return value;
  }

  private static int ResolveActingPlayerIndex(List<string> tick, string action, int defaultPlayer) {
    return GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(tick, action, defaultPlayer);
  }

  private static bool TryPeekDrawOriginalIndex(WallSimState wall, string action, out int originalIndex) {
    originalIndex = -1;
    if (wall.currentTiles.Count == 0 || wall.origIndices.Count == 0) return false;

    if (action == "d") {
      originalIndex = wall.origIndices[0];
      return true;
    }

    if (action == "gd" || action == "bd") {
      int removePos;
      if (wall.backwardType == "double" && wall.currentTiles.Count > 1) {
        removePos = wall.currentTiles.Count - 2;
      }
      else {
        removePos = wall.currentTiles.Count - 1;
      }
      if (removePos < 0 || removePos >= wall.origIndices.Count) return false;
      originalIndex = wall.origIndices[removePos];
      return true;
    }

    return false;
  }

  private static bool SimulateDraw(WallSimState wall, string action) {
    if (wall.currentTiles.Count == 0 || wall.origIndices.Count == 0) return false;

    if (action == "d") {
      wall.currentTiles.RemoveAt(0);
      wall.origIndices.RemoveAt(0);
      return true;
    }

    if (action == "gd" || action == "bd") {
      int removePos;
      if (wall.backwardType == "double" && wall.currentTiles.Count > 1) {
        removePos = wall.currentTiles.Count - 2;
      }
      else {
        removePos = wall.currentTiles.Count - 1;
      }
      if (removePos < 0 || removePos >= wall.origIndices.Count) return false;
      int origIdx = wall.origIndices[removePos];
      wall.currentTiles.RemoveAt(removePos);
      wall.origIndices.RemoveAt(removePos);
      wall.consumedBack.Add(origIdx);
      wall.backwardType = wall.backwardType == "double" ? "single" : "double";
      return true;
    }

    return false;
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

  private class WallSimState {
    public List<int> currentTiles;
    public List<int> origIndices;
    public HashSet<int> consumedBack;
    public string backwardType;
  }
}
