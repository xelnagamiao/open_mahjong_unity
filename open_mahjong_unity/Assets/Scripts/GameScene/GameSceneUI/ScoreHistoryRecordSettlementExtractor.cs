using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// 从牌谱 JSON 提取每局结算快照（含轻量 tick 重放以还原和牌手牌）。
/// </summary>
public static class ScoreHistoryRecordSettlementExtractor {
    private sealed class SimPlayer {
        public List<int> tileList = new List<int>();
        public List<string> combinationTiles = new List<string>();
        public List<int[]> combinationMasks = new List<int[]>();
    }

    /// <summary>
    /// 牌谱计分板单行：每次结算（含国标同一局内多次错和）各占一行，与实时对局逐次结算追加行的行为对齐。
    /// </summary>
    public sealed class RecordScoreRow {
        public RoundSettlementSnapshot snapshot;
        public int[] scoreChangesByOriginal; // 长度 4，按 original_player_index 排列
        public int roundNumber;              // 局号(current_round)，连庄/错和会重复
    }

    /// <summary>仅返回主番快照（向后兼容）。每次结算一项，与 <see cref="ExtractScoreRows"/> 行序一致。</summary>
    public static List<RoundSettlementSnapshot> Extract(GameRecord gameRecord) {
        var result = new List<RoundSettlementSnapshot>();
        foreach (RecordScoreRow row in ExtractScoreRows(gameRecord)) {
            result.Add(row.snapshot);
        }
        return result;
    }

    /// <summary>
    /// 展开为"每次结算一行"的计分板行：分值变化、局号、主番快照三者严格对齐。
    /// 国标同一 round_index 内的多次错和与最终和牌会各占一行（局号相同）。
    /// </summary>
    public static List<RecordScoreRow> ExtractScoreRows(GameRecord gameRecord) {
        var result = new List<RecordScoreRow>();
        if (gameRecord?.gameRound?.rounds == null) return result;

        string rule = ReadTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
        string subRule = ReadTitleString(gameRecord.gameTitle, "sub_rule", "");
        subRule = ScoreHistorySettlementHelper.ResolveSubRule(rule, subRule);

        foreach (Round round in gameRecord.gameRound.GetRoundsList()) {
            ExtractRoundRows(round, subRule, gameRecord.gameTitle, result);
        }
        return result;
    }

    private static void ExtractRoundRows(
        Round round,
        string subRule,
        Dictionary<string, object> gameTitle,
        List<RecordScoreRow> output) {
        if (round?.actionTicks == null || round.seats == null || round.seats.Count < 4) {
            return;
        }

        int roundNumber = round.currentRound > 0 ? round.currentRound : round.roundIndex;

        var players = new SimPlayer[4];
        for (int i = 0; i < 4; i++) players[i] = new SimPlayer();

        InitHands(players, round);
        int lastDiscardPlayerIndex = -1;
        int lastDiscardTileId = -1;
        RecordScoreRow lastRow = null;
        int currentPlayerIndex = round.startPlayerIndex;
        bool mainPhaseStarted = false;

        foreach (List<string> tick in round.actionTicks) {
            if (tick == null || tick.Count == 0) continue;
            string action = tick[0];
            if (action == "ask_hand" || action == "ask_other" || action == "end" || action == "dora" || action == "riichi") {
                continue;
            }

            // 开局补花阶段：与 GameRecordManager.InferAndMarkStartIndex 对齐，不推进主巡目
            if (action == "bh" || action == "bd") {
                int flowerActingIndex = action == "bh" && tick.Count >= 3
                    ? ParseInt(tick, 2)
                    : currentPlayerIndex;
                SimPlayer flowerActor = players[flowerActingIndex];
                if (action == "bh") {
                    RemoveOneTile(flowerActor.tileList, ParseInt(tick, 1));
                } else {
                    flowerActor.tileList.Add(ParseInt(tick, 1));
                }
                currentPlayerIndex = flowerActingIndex;
                continue;
            }

            // 主局首个摸切/鸣牌前，巡目回到庄家（补花结束后并非最后补花者出牌）
            if (!mainPhaseStarted) {
                currentPlayerIndex = round.startPlayerIndex;
                mainPhaseStarted = true;
            }

            int previousPlayerIndex = currentPlayerIndex;
            int actingPlayerIndex = ResolveActingPlayerIndex(action, tick, currentPlayerIndex);
            SimPlayer actor = players[actingPlayerIndex];

            switch (action) {
                case "d":
                case "gd":
                    actor.tileList.Add(ParseInt(tick, 1));
                    currentPlayerIndex = actingPlayerIndex;
                    break;
                case "c":
                    RemoveTileForCut(actor.tileList, ParseInt(tick, 1), ParseBool(tick, 2));
                    lastDiscardPlayerIndex = actingPlayerIndex;
                    lastDiscardTileId = ParseInt(tick, 1);
                    currentPlayerIndex = (actingPlayerIndex + 1) % 4;
                    break;
                case "ag": {
                    int tile = ParseInt(tick, 1);
                    bool isMoGang = GameRecordJsonDecoder.ParseKanMoGangFlag(tick);
                    List<int> removedTiles = GameRecordMeldCodec.ResolveAngangRemovedTiles(
                        tick, actor.tileList, tile, isMoGang);
                    actor.combinationTiles.Add($"G{tile}");
                    actor.combinationMasks.Add(GameRecordMeldCodec.BuildAngangMaskFromRemoved(removedTiles, subRule));
                    currentPlayerIndex = actingPlayerIndex;
                    break;
                }
                case "jg": {
                    int tile = ParseInt(tick, 1);
                    bool isMoGang = GameRecordJsonDecoder.ParseKanMoGangFlag(tick);
                    List<int> removedTiles = GameRecordMeldCodec.RemoveNTilesByNormalized(
                        actor.tileList, tile, 1, preferDrawSlotFirst: isMoGang);
                    int actualJia = removedTiles.Count > 0 ? removedTiles[0] : tile;
                    BuildJiagangMask(actor, tile, actualJia);
                    currentPlayerIndex = actingPlayerIndex;
                    break;
                }
                case "cl":
                case "cm":
                case "cr":
                case "p":
                case "g": {
                    int mingTile = ParseInt(tick, 1);
                    List<int> removedTiles = GameRecordMeldCodec.ResolveHandTiles(tick, action, mingTile);
                    foreach (int t in removedTiles) {
                        RemoveOneTile(actor.tileList, t);
                    }
                    int discardPlayerIndex = lastDiscardPlayerIndex >= 0 ? lastDiscardPlayerIndex : previousPlayerIndex;
                    string relative = GetRelativePosition(actingPlayerIndex, discardPlayerIndex);
                    actor.combinationTiles.Add(GameRecordMeldCodec.BuildCombinationTarget(action, mingTile));
                    actor.combinationMasks.Add(GameRecordMeldCodec.BuildMingpaiMask(action, mingTile, removedTiles, relative));
                    currentPlayerIndex = actingPlayerIndex;
                    break;
                }
                case "hu_self":
                case "hu_first":
                case "hu_second":
                case "hu_third": {
                    RoundSettlementSnapshot snap = BuildHuSnapshot(tick, action, subRule, round, actingPlayerIndex, players, lastDiscardTileId, action != "hu_self", gameTitle);
                    lastRow = new RecordScoreRow {
                        snapshot = snap,
                        scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(ParseScoreChanges(tick, 4), round.seats),
                        roundNumber = roundNumber,
                    };
                    output.Add(lastRow);
                    break;
                }
                case "hu_riichi": {
                    RoundSettlementSnapshot snap = BuildRiichiHuSnapshot(tick, subRule, round, players, lastDiscardTileId, gameTitle);
                    lastRow = new RecordScoreRow {
                        snapshot = snap,
                        scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(ParseScoreChanges(tick, 6), round.seats),
                        roundNumber = roundNumber,
                    };
                    output.Add(lastRow);
                    break;
                }
                case "shuhewei": {
                    if (lastRow == null) {
                        lastRow = new RecordScoreRow {
                            snapshot = new RoundSettlementSnapshot { subRule = subRule, isLiuju = false, hasWin = false },
                            roundNumber = roundNumber,
                        };
                        output.Add(lastRow);
                    }
                    ApplyShuhewei(tick, lastRow.snapshot, players, lastDiscardTileId);
                    int[] shScores = ParseScoreChanges(tick, 2);
                    if (shScores != null) {
                        lastRow.scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(shScores, round.seats);
                    }
                    break;
                }
                case "ryuukyoku": {
                    lastRow = new RecordScoreRow {
                        snapshot = new RoundSettlementSnapshot { subRule = subRule, isLiuju = true, hasWin = false, huClass = action },
                        scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(ParseScoreChanges(tick, 2), round.seats),
                        roundNumber = roundNumber,
                    };
                    output.Add(lastRow);
                    break;
                }
                case "liuju":
                case "jiuzhongjiupai": {
                    lastRow = new RecordScoreRow {
                        snapshot = new RoundSettlementSnapshot { subRule = subRule, isLiuju = true, hasWin = false, huClass = action },
                        scoreChangesByOriginal = new int[4],
                        roundNumber = roundNumber,
                    };
                    output.Add(lastRow);
                    break;
                }
            }
        }
    }

    private static void InitHands(SimPlayer[] players, Round round) {
        // 与 GameRecordManager 一致：pN_tiles 即当局 player_index N 的起手牌
        if (round.p0Tiles != null) players[0].tileList = new List<int>(round.p0Tiles);
        if (round.p1Tiles != null) players[1].tileList = new List<int>(round.p1Tiles);
        if (round.p2Tiles != null) players[2].tileList = new List<int>(round.p2Tiles);
        if (round.p3Tiles != null) players[3].tileList = new List<int>(round.p3Tiles);
    }

    private static RoundSettlementSnapshot BuildHuSnapshot(
        List<string> tick,
        string huClass,
        string subRule,
        Round round,
        int hepaiPlayerIndex,
        SimPlayer[] players,
        int lastDiscardTileId,
        bool isRon,
        Dictionary<string, object> gameTitle) {
        string[] huFan = ParseFanList(tick, 3);
        int huScore = ParseInt(tick, 2);
        int? baseFu = tick.Count > 5 ? ParseInt(tick, 5) : (int?)null;
        string[] fuFanList = tick.Count > 6 ? ParseFanList(tick, 6) : null;

        SimPlayer huPlayer = players[hepaiPlayerIndex];
        int[] hand = BuildWinnerHandArray(huPlayer, isRon, lastDiscardTileId);

        int winnerDelta = 0;
        int[] scoreChanges = ParseScoreChanges(tick, 4);
        if (scoreChanges != null && hepaiPlayerIndex >= 0 && hepaiPlayerIndex < scoreChanges.Length) {
            winnerDelta = scoreChanges[hepaiPlayerIndex];
        }

        string username = ResolveUsernameForSeat(hepaiPlayerIndex, round, gameTitle);

        return new RoundSettlementSnapshot {
            hasWin = huFan != null && huFan.Length > 0 && hand.Length > 0,
            isLiuju = false,
            huClass = huClass,
            hepaiPlayerIndex = hepaiPlayerIndex,
            winnerUsername = username,
            huScore = huScore,
            winnerScoreDelta = winnerDelta,
            huFan = huFan,
            fuFanList = fuFanList,
            baseFu = baseFu,
            hepaiPlayerHand = hand,
            combinationMask = CloneMasks(huPlayer.combinationMasks),
            subRule = subRule,
        };
    }

    private static RoundSettlementSnapshot BuildRiichiHuSnapshot(
        List<string> tick,
        string subRule,
        Round round,
        SimPlayer[] players,
        int lastDiscardTileId,
        Dictionary<string, object> gameTitle) {
        int hepaiPlayerIndex = ParseInt(tick, 1);
        string huClass = tick.Count > 2 ? tick[2] : "hu_self";
        int han = tick.Count > 3 ? ParseInt(tick, 3) : 0;
        int fu = tick.Count > 4 ? ParseInt(tick, 4) : 0;
        string[] yaku = tick.Count > 5 ? ParseFanList(tick, 5) : Array.Empty<string>();

        SimPlayer huPlayer = players[hepaiPlayerIndex];
        bool isRon = huClass != "hu_self" && lastDiscardTileId >= 0;
        int[] hand = BuildWinnerHandArray(huPlayer, isRon, lastDiscardTileId);

        int winnerDelta = 0;
        int[] scoreChanges = tick.Count > 6 ? ParseScoreChanges(tick, 6) : null;
        if (scoreChanges != null && hepaiPlayerIndex >= 0 && hepaiPlayerIndex < scoreChanges.Length) {
            winnerDelta = scoreChanges[hepaiPlayerIndex];
        }

        return new RoundSettlementSnapshot {
            hasWin = yaku.Length > 0 && hand.Length > 0,
            isLiuju = false,
            huClass = huClass,
            hepaiPlayerIndex = hepaiPlayerIndex,
            winnerUsername = ResolveUsernameForSeat(hepaiPlayerIndex, round, gameTitle),
            huScore = winnerDelta,
            winnerScoreDelta = winnerDelta,
            huFan = yaku,
            han = han,
            fu = fu,
            hepaiPlayerHand = hand,
            combinationMask = CloneMasks(huPlayer.combinationMasks),
            subRule = subRule,
        };
    }

    private static void ApplyShuhewei(
        List<string> tick,
        RoundSettlementSnapshot target,
        SimPlayer[] players,
        int lastDiscardTileId) {
        if (tick.Count < 7) return;
        int hepaiIndex = ParseInt(tick, 6);
        if (hepaiIndex < 0 || hepaiIndex >= players.Length) return;

        if (tick.Count > 5 && !string.IsNullOrEmpty(tick[5])) {
            target.huClass = tick[5];
        }

        string[][] fanMatrix = ParseFanMatrix(tick, 3);
        if (fanMatrix != null && hepaiIndex < fanMatrix.Length && fanMatrix[hepaiIndex] != null && fanMatrix[hepaiIndex].Length > 0) {
            target.huFan = fanMatrix[hepaiIndex];
            target.hasWin = true;
            target.isLiuju = false;
        }

        int[] changes = ParseScoreChanges(tick, 2);
        if (changes != null && hepaiIndex < changes.Length) {
            target.winnerScoreDelta = changes[hepaiIndex];
        }
        target.hepaiPlayerIndex = hepaiIndex;

        SimPlayer huPlayer = players[hepaiIndex];
        bool isRon = !string.IsNullOrEmpty(target.huClass)
            && target.huClass != "hu_self"
            && lastDiscardTileId >= 0;
        target.hepaiPlayerHand = BuildWinnerHandArray(huPlayer, isRon, lastDiscardTileId);
        target.combinationMask = CloneMasks(huPlayer.combinationMasks);
    }

    /// <summary>荣和时和牌张不在手牌列表中，需追加到末尾（与 GameRecordManager / 服务端一致）。</summary>
    private static int[] BuildWinnerHandArray(SimPlayer huPlayer, bool isRon, int lastDiscardTileId) {
        if (huPlayer?.tileList == null) return Array.Empty<int>();
        int[] hand = huPlayer.tileList.ToArray();
        if (!isRon || lastDiscardTileId < 0) return hand;
        for (int i = 0; i < hand.Length; i++) {
            if (hand[i] == lastDiscardTileId) return hand;
        }
        int[] extended = new int[hand.Length + 1];
        Array.Copy(hand, extended, hand.Length);
        extended[hand.Length] = lastDiscardTileId;
        return extended;
    }

    /// <summary>与 GameRecordManager.NextAction 一致：摸切/补花/副露/和牌等 tick 自带行动者，其余沿用当前巡目玩家。</summary>
    private static int ResolveActingPlayerIndex(string action, List<string> tick, int currentPlayerIndex) {
        if (action == "bh" && tick.Count >= 3) return ParseInt(tick, 2);
        if ((action == "cl" || action == "cm" || action == "cr" || action == "p" || action == "g") && tick.Count >= 3) {
            return ParseInt(tick, 2);
        }
        if ((action == "hu_self" || action == "hu_first" || action == "hu_second" || action == "hu_third" || action == "hu_riichi") && tick.Count >= 2) {
            return ParseInt(tick, 1);
        }
        return currentPlayerIndex;
    }

    private static string ResolveUsernameForSeat(int seatIndex, Round round, Dictionary<string, object> gameTitle) {
        if (round?.seats != null) {
            for (int orig = 0; orig < round.seats.Count && orig < 4; orig++) {
                if (round.seats[orig] != seatIndex) continue;
                string key = $"p{orig}_name";
                if (gameTitle != null && gameTitle.TryGetValue(key, out object nameObj) && nameObj != null) {
                    return nameObj.ToString();
                }
                break;
            }
        }
        return seatIndex.ToString();
    }

    private static int[][] CloneMasks(List<int[]> masks) {
        if (masks == null) return Array.Empty<int[]>();
        var result = new int[masks.Count][];
        for (int i = 0; i < masks.Count; i++) {
            result[i] = masks[i] != null ? (int[])masks[i].Clone() : Array.Empty<int>();
        }
        return result;
    }

    private static string ReadTitleString(Dictionary<string, object> title, string key, string fallback) {
        if (title == null || !title.TryGetValue(key, out object val) || val == null) return fallback;
        return val.ToString();
    }

    private static int ParseInt(List<string> tick, int index) {
        if (index >= tick.Count || string.IsNullOrEmpty(tick[index])) return 0;
        int.TryParse(tick[index], out int val);
        return val;
    }

    private static bool ParseBool(List<string> tick, int index) {
        if (index >= tick.Count) return false;
        return tick[index] == "T" || tick[index] == "True" || tick[index] == "true";
    }

    private static string[] ParseFanList(List<string> tick, int index) {
        if (index >= tick.Count || string.IsNullOrEmpty(tick[index])) return Array.Empty<string>();
        string raw = tick[index].Trim();
        if (raw.StartsWith("[") && raw.EndsWith("]")) {
            try {
                JArray arr = JArray.Parse(raw);
                var fans = new string[arr.Count];
                for (int i = 0; i < arr.Count; i++) fans[i] = arr[i]?.ToString() ?? "";
                return fans;
            } catch {
                // fall through
            }
        }
        if (raw.StartsWith("[") && raw.EndsWith("]") && raw.Length >= 2) {
            raw = raw.Substring(1, raw.Length - 2);
        }
        string[] split = raw.Split(',');
        var list = new List<string>();
        foreach (string part in split) {
            string fan = part.Trim().Trim('"').Trim('\'');
            if (!string.IsNullOrEmpty(fan)) list.Add(fan);
        }
        return list.ToArray();
    }

    private static int[] ParseScoreChanges(List<string> tick, int index) {
        if (index >= tick.Count || string.IsNullOrEmpty(tick[index])) return null;
        try {
            JArray arr = JArray.Parse(tick[index]);
            int[] result = new int[arr.Count];
            for (int i = 0; i < arr.Count; i++) result[i] = arr[i].Value<int>();
            return result;
        } catch {
            return null;
        }
    }

    private static string[][] ParseFanMatrix(List<string> tick, int index) {
        if (index >= tick.Count || string.IsNullOrEmpty(tick[index])) return null;
        try {
            JArray outer = JArray.Parse(tick[index]);
            var result = new string[outer.Count][];
            for (int i = 0; i < outer.Count; i++) {
                if (outer[i] is JArray inner) {
                    result[i] = new string[inner.Count];
                    for (int j = 0; j < inner.Count; j++) result[i][j] = inner[j]?.ToString() ?? "";
                } else {
                    result[i] = Array.Empty<string>();
                }
            }
            return result;
        } catch {
            return null;
        }
    }

    private static void RemoveOneTile(List<int> tileList, int tileId) {
        int idx = tileList.IndexOf(tileId);
        if (idx >= 0) tileList.RemoveAt(idx);
    }

    private static void RemoveTileForCut(List<int> tileList, int tileId, bool isMoqie) {
        if (tileList.Count == 0) return;
        if (isMoqie && tileList[tileList.Count - 1] == tileId) {
            tileList.RemoveAt(tileList.Count - 1);
            return;
        }
        RemoveOneTile(tileList, tileId);
    }

    private static int[] BuildJiagangMask(SimPlayer player, int jiagangTile, int actualJiaTile) {
        for (int i = 0; i < player.combinationTiles.Count; i++) {
            if (player.combinationTiles[i] != $"k{jiagangTile}") continue;
            player.combinationTiles[i] = $"g{jiagangTile}";
            var updatedMask = new List<int>(player.combinationMasks[i]);
            for (int j = 0; j < updatedMask.Count; j++) {
                if (updatedMask[j] != 1) continue;
                updatedMask.Insert(j, actualJiaTile);
                updatedMask.Insert(j, 3);
                break;
            }
            player.combinationMasks[i] = updatedMask.ToArray();
            return player.combinationMasks[i];
        }

        int[] fallback = { 0, jiagangTile, 3, actualJiaTile, 1, jiagangTile, 0, jiagangTile };
        player.combinationTiles.Add($"g{jiagangTile}");
        player.combinationMasks.Add(fallback);
        return fallback;
    }

    private static string GetRelativePosition(int selfIndex, int otherIndex) {
        if (selfIndex == otherIndex) return "self";
        if (selfIndex == 0) {
            if (otherIndex == 1) return "right";
            if (otherIndex == 2) return "top";
            return "left";
        }
        if (selfIndex == 1) {
            if (otherIndex == 0) return "left";
            if (otherIndex == 2) return "right";
            return "top";
        }
        if (selfIndex == 2) {
            if (otherIndex == 0) return "top";
            if (otherIndex == 1) return "left";
            return "right";
        }
        if (otherIndex == 0) return "right";
        if (otherIndex == 1) return "top";
        return "left";
    }
}
