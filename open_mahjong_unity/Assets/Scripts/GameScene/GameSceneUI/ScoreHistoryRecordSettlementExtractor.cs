using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// 从牌谱 JSON 提取每局结算快照（含轻量 tick 重放以还原和牌手牌）。
/// 核心只保留标准麻将模拟骨架，所有规则专属分值与 tick 差异均通过 RuleManifest.CreateSettlementCodec 分发。
/// </summary>
public static partial class ScoreHistoryRecordSettlementExtractor {
    /// <summary>
    /// 牌谱计分板单行：每次结算（含国标同一局内多次错和）各占一行，与实时对局逐次结算追加行的行为对齐。
    /// </summary>
    public sealed class RecordScoreRow {
        public RoundSettlementSnapshot snapshot;
        public int[] scoreChangesByOriginal; // 长度 4，按 original_player_index 排列
        public int roundNumber;              // 局号(current_round)，连庄/错和会重复
        public int roundIndex;               // 实际牌谱局索引，点击计分板时不能用显示局号代替
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
    public static List<RecordScoreRow> ExtractScoreRows(GameRecord gameRecord, bool includeUnsettledRounds = false) {
        var result = new List<RecordScoreRow>();
        if (gameRecord?.gameRound?.rounds == null) return result;
        RuleRecordCodecRegistry.EnsureInitialized();

        string rule = ReadTitleString(gameRecord.gameTitle, "rule", "").ToLowerInvariant();
        string subRule = ReadTitleString(gameRecord.gameTitle, "sub_rule", "");
        subRule = ScoreHistorySettlementHelper.ResolveSubRule(rule, subRule);
        RuleManifest manifest = RuleRegistry.Resolve(rule, subRule);

        foreach (Round round in gameRecord.gameRound.GetRoundsList()) {
            if (round == null) continue;
            int firstRow = result.Count;
            ExtractRoundRows(round, rule, subRule, manifest, gameRecord.gameTitle, result);
            // 阅览列表只使用实际存在的局；尚未结算的观战局也保留入口，分数留空而不是伪造零分结算。
            if (includeUnsettledRounds && result.Count == firstRow) {
                result.Add(new RecordScoreRow {
                    roundNumber = round.currentRound > 0 ? round.currentRound : round.roundIndex,
                });
            }
            for (int i = firstRow; i < result.Count; i++) {
                result[i].roundIndex = round.roundIndex;
                if (result[i].snapshot != null)
                    result[i].snapshot.handJokerTiles = TileIdOrder.RecordHandJokers(rule, subRule, round);
            }
        }
        manifest?.CreateSettlementCodec?.Invoke()?.OnRecordFinished(gameRecord.gameTitle, rule, result);
        return result;
    }

    private static void ExtractRoundRows(
        Round round,
        string rule,
        string subRule,
        RuleManifest roundManifest,
        Dictionary<string, object> gameTitle,
        List<RecordScoreRow> output) {
        int playerCount = MahjongPlayerCount.ForSubRule(subRule);
        if (round?.actionTicks == null || round.seats == null || round.seats.Count < playerCount) {
            return;
        }

        RuleRecordSettlementCodec codec = roundManifest?.CreateSettlementCodec?.Invoke();
        if (codec != null && codec.TryExtractWholeRound(round, subRule, gameTitle, output)) {
            return;
        }

        var players = new RecordSimPlayer[4];
        for (int i = 0; i < 4; i++) players[i] = new RecordSimPlayer();
        InitHands(players, round);

        var sim = new RecordSettlementSimContext {
            Round = round,
            Rule = rule,
            SubRule = subRule,
            Manifest = roundManifest,
            GameTitle = gameTitle,
            PlayerCount = playerCount,
            RoundNumber = round.currentRound > 0 ? round.currentRound : round.roundIndex,
            FirstRoundRow = output.Count,
            Players = players,
            Output = output,
            CurrentPlayerIndex = round.startPlayerIndex,
        };

        foreach (List<string> tick in round.actionTicks) {
            if (tick == null || tick.Count == 0) continue;
            string action = tick[0];
            codec?.OnBeforeStandardTick(sim, tick);

            if (codec != null && codec.TryHandleCustomTick(sim, tick)) {
                continue;
            }
            if (action == "ask_hand" || action == "ask_other" || action == "ca" || action == "end" || action == "dora" || action == "state") {
                continue;
            }
            if (action == "reset") {
                sim.CurrentPlayerIndex = ParseInt(tick, 1);
                continue;
            }
            if (action == "bh" || action == "bd" || action == "nuki" || action == "nd") {
                int flowerActingIndex = GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(tick, action, sim.CurrentPlayerIndex);
                RecordSimPlayer flowerActor = players[flowerActingIndex];
                if (action == "bh" || action == "nuki") {
                    bool isMoBuhua = GameRecordJsonDecoder.ParseBuhuaMoFlag(tick);
                    RemoveTileForBuhua(flowerActor.tileList, ParseInt(tick, action == "nuki" ? 2 : 1), isMoBuhua);
                } else {
                    flowerActor.tileList.Add(ParseInt(tick, 1));
                }
                sim.CurrentPlayerIndex = flowerActingIndex;
                continue;
            }

            int previousPlayerIndex = sim.CurrentPlayerIndex;
            int actingPlayerIndex = GameRecordJsonDecoder.ResolveRecordActingPlayerIndex(tick, action, sim.CurrentPlayerIndex);
            RecordSimPlayer actor = players[actingPlayerIndex];

            switch (action) {
                case "d":
                case "gd":
                    actor.tileList.Add(ParseInt(tick, 1));
                    sim.CurrentPlayerIndex = actingPlayerIndex;
                    break;
                case "c":
                    RemoveTileForCut(actor.tileList, ParseInt(tick, 1), ParseBool(tick, 2));
                    sim.LastDiscardPlayerIndex = actingPlayerIndex;
                    sim.LastDiscardTileId = ParseInt(tick, 1);
                    sim.LastWinnableTileId = ParseInt(tick, 1);
                    sim.CurrentPlayerIndex = (actingPlayerIndex + 1) % playerCount;
                    break;
                case "ag": {
                    int tile = ParseInt(tick, 1);
                    bool isMoGang = GameRecordJsonDecoder.ParseKanMoGangFlag(tick);
                    List<int> removedTiles = GameRecordMeldCodec.ResolveAngangRemovedTiles(tick, actor.tileList, tile, isMoGang);
                    actor.combinationTiles.Add($"G{tile}");
                    actor.combinationMasks.Add(GameRecordMeldCodec.BuildAngangMaskFromRemoved(removedTiles, subRule));
                    sim.CurrentPlayerIndex = actingPlayerIndex;
                    break;
                }
                case "rk": {
                    int tile = ParseInt(tick, 2);
                    int normalized = roundManifest?.NormalizeTileId != null ? roundManifest.NormalizeTileId(tile) : tile;
                    GameRecordMeldCodec.RemoveOneJiagangTile(actor.tileList, tile, normalized, ParseInt(tick, 3) != 0);
                    sim.LastWinnableTileId = tile;
                    sim.CurrentPlayerIndex = actingPlayerIndex;
                    break;
                }
                case "jg": {
                    if (codec != null && codec.TryHandleAddedKong(sim, tick, actingPlayerIndex)) break;
                    int tile = ParseInt(tick, 1);
                    bool isMoGang = GameRecordJsonDecoder.ParseKanMoGangFlag(tick);
                    List<int> removedTiles = GameRecordMeldCodec.RemoveNTilesByNormalized(actor.tileList, tile, 1, preferDrawSlotFirst: isMoGang);
                    int actualJia = removedTiles.Count > 0 ? removedTiles[0] : tile;
                    sim.LastWinnableTileId = actualJia;
                    BuildJiagangMask(actor, tile, actualJia);
                    sim.CurrentPlayerIndex = actingPlayerIndex;
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
                        RecordSettlementSimContext.RemoveOneTile(actor.tileList, t);
                    }
                    sim.LastWinnableTileId = -1;
                    int discardPlayerIndex = sim.LastDiscardPlayerIndex >= 0 ? sim.LastDiscardPlayerIndex : previousPlayerIndex;
                    string relative = TableMirror.RelativeSeat(actingPlayerIndex, discardPlayerIndex);
                    actor.combinationTiles.Add(GameRecordMeldCodec.BuildCombinationTarget(action, mingTile));
                    actor.combinationMasks.Add(GameRecordMeldCodec.BuildMingpaiMask(action, mingTile, removedTiles, relative));
                    sim.CurrentPlayerIndex = actingPlayerIndex;
                    break;
                }
                case "hu_self":
                case "hu_first":
                case "hu_second":
                case "hu_third": {
                    // 古典牌谱顺序为 shuhewei → hu；数和尾已占一行，勿重复追加。
                    if (roundManifest?.HuTickFollowsShuhewei == true && sim.LastRow != null) break;
                    if (codec != null && codec.TryHandleHuTick(sim, tick, action, actingPlayerIndex)) break;
                    RoundSettlementSnapshot snap = BuildHuSnapshot(tick, action, sim, actingPlayerIndex);
                    codec?.OnAfterHuSnapshotBuilt(sim, tick, snap);
                    int[] seatChanges = ParseScoreChanges(tick, 4);
                    if (codec != null) seatChanges = codec.AdjustRowScoreChanges(sim, action, seatChanges);
                    sim.LastRow = new RecordScoreRow {
                        snapshot = snap,
                        scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(seatChanges, round.seats),
                        roundNumber = sim.RoundNumber,
                    };
                    output.Add(sim.LastRow);
                    break;
                }
                case "shuhewei": {
                    if (sim.LastRow == null) {
                        sim.LastRow = new RecordScoreRow {
                            snapshot = new RoundSettlementSnapshot { subRule = subRule, isLiuju = false, hasWin = false },
                            roundNumber = sim.RoundNumber,
                        };
                        output.Add(sim.LastRow);
                    }
                    ApplyShuhewei(tick, sim.LastRow.snapshot, players, sim.LastDiscardTileId);
                    int[] shScores = ParseScoreChanges(tick, 2);
                    if (shScores != null) {
                        sim.LastRow.scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(shScores, round.seats);
                    }
                    break;
                }
                case "ryuukyoku": {
                    int[] seatChanges = ParseScoreChanges(tick, 2);
                    if (codec != null) seatChanges = codec.AdjustRowScoreChanges(sim, action, seatChanges);
                    sim.LastRow = new RecordScoreRow {
                        snapshot = new RoundSettlementSnapshot { subRule = subRule, isLiuju = true, hasWin = false, huClass = action },
                        scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(seatChanges, round.seats),
                        roundNumber = sim.RoundNumber,
                    };
                    output.Add(sim.LastRow);
                    break;
                }
                case "blood":
                case "liuju":
                case "jiuzhongjiupai": {
                    if (codec != null && codec.TryHandleLiujuTick(sim, tick, action)) break;
                    // 古典流局：shuhewei 已记一行，后续 liuju tick 勿重复。
                    if (roundManifest?.HuTickFollowsShuhewei == true && sim.LastRow != null) break;
                    int[] seatChanges = new int[4];
                    if (codec != null) seatChanges = codec.AdjustRowScoreChanges(sim, action, seatChanges);
                    sim.LastRow = new RecordScoreRow {
                        snapshot = new RoundSettlementSnapshot { subRule = subRule, isLiuju = true, hasWin = false, huClass = action },
                        scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(seatChanges, round.seats),
                        roundNumber = sim.RoundNumber,
                    };
                    output.Add(sim.LastRow);
                    break;
                }
            }
        }

        if (sim.LastRow != null) {
            if (sim.LastRow.scoreChangesByOriginal == null) sim.LastRow.scoreChangesByOriginal = new int[4];
            RecordSettlementSimContext.AccumulateSeatScores(
                sim.LastRow.scoreChangesByOriginal,
                GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(sim.ImmediateScores, round.seats));
            codec?.OnRoundFinished(sim);
        }
    }

    private static void InitHands(RecordSimPlayer[] players, Round round) {
        if (round.p0Tiles != null) players[0].tileList = new List<int>(round.p0Tiles);
        if (round.p1Tiles != null) players[1].tileList = new List<int>(round.p1Tiles);
        if (round.p2Tiles != null) players[2].tileList = new List<int>(round.p2Tiles);
        if (round.p3Tiles != null) players[3].tileList = new List<int>(round.p3Tiles);
    }

    private static RoundSettlementSnapshot BuildHuSnapshot(
        List<string> tick,
        string huClass,
        RecordSettlementSimContext sim,
        int hepaiPlayerIndex) {
        string[] huFan = ParseFanList(tick, 3);
        double huScore = ParseFanScore(tick, 2);
        int? baseFu = null;
        string[] fuFanList = null;
        if (sim.Manifest?.HuTickFollowsShuhewei == true) {
            baseFu = tick.Count > 5 ? ParseInt(tick, 5) : (int?)null;
            fuFanList = tick.Count > 6 ? ParseFanList(tick, 6) : null;
        }

        RecordSimPlayer huPlayer = sim.Players[hepaiPlayerIndex];
        string rule = ResolveRecordRule(sim.GameTitle, sim.SubRule);
        RecordHuHandBuilder.TryParseHepaiTile(tick, rule, out int hepaiTile);
        string displayHuClass = RecordHuHandBuilder.IsFlowerWin(tick, rule) ? "hu_self" : huClass;
        int[] hand = RecordSettlementSimContext.BuildScoreboardHuHand(huPlayer.tileList, displayHuClass, hepaiTile, sim.LastWinnableTileId);

        int winnerDelta = 0;
        int[] scoreChanges = ParseScoreChanges(tick, 4);
        if (scoreChanges != null && hepaiPlayerIndex >= 0 && hepaiPlayerIndex < scoreChanges.Length) {
            winnerDelta = scoreChanges[hepaiPlayerIndex];
        }

        return new RoundSettlementSnapshot {
            hasWin = huFan != null && huFan.Length > 0 && hand.Length > 0,
            isLiuju = false,
            huClass = huClass,
            hepaiPlayerIndex = hepaiPlayerIndex,
            winnerUsername = ResolveUsernameForSeat(hepaiPlayerIndex, sim.Round, sim.GameTitle),
            huScore = huScore,
            winnerScoreDelta = winnerDelta,
            huFan = huFan,
            fuFanList = fuFanList,
            baseFu = baseFu,
            hepaiPlayerHand = hand,
            combinationMask = CloneMasks(huPlayer.combinationMasks),
            subRule = sim.SubRule,
        };
    }

    private static void ApplyShuhewei(
        List<string> tick,
        RoundSettlementSnapshot target,
        RecordSimPlayer[] players,
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

        RecordSimPlayer huPlayer = players[hepaiIndex];
        string huClass = string.IsNullOrEmpty(target.huClass) ? "hu_self" : target.huClass;
        target.hepaiPlayerHand = RecordSettlementSimContext.BuildScoreboardHuHand(huPlayer.tileList, huClass, 0, lastDiscardTileId);
        target.combinationMask = CloneMasks(huPlayer.combinationMasks);
    }

    private static string ResolveRecordRule(Dictionary<string, object> gameTitle, string subRule) {
        if (gameTitle != null && gameTitle.TryGetValue("rule", out object ruleObj) && ruleObj != null) {
            return ruleObj.ToString();
        }
        return subRule;
    }

    internal static string ResolveUsernameForSeat(int seatIndex, Round round, Dictionary<string, object> gameTitle) {
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

    internal static int[][] CloneMasks(List<int[]> masks) {
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

    private static double ParseFanScore(List<string> tick, int index) {
        if (index >= tick.Count) return 0;
        return double.TryParse(tick[index], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : 0;
    }

    private static int ParseInt(List<string> tick, int index) => RecordSettlementSimContext.ParseInt(tick, index);

    private static bool ParseBool(List<string> tick, int index) {
        if (index >= tick.Count) return false;
        return tick[index] == "T" || tick[index] == "True" || tick[index] == "true";
    }

    internal static string[] ParseFanList(List<string> tick, int index) {
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

    private static int[] ParseScoreChanges(List<string> tick, int index) =>
        RuleRecordCodecRegistry.ParseScoreChangesArray(tick, index);

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

    private static void RemoveTileForBuhua(List<int> tileList, int tileId, bool isMoBuhua) {
        if (tileList.Count == 0) return;
        if (isMoBuhua && tileList[tileList.Count - 1] == tileId) {
            tileList.RemoveAt(tileList.Count - 1);
            return;
        }
        RecordSettlementSimContext.RemoveOneTile(tileList, tileId);
    }

    private static void RemoveTileForCut(List<int> tileList, int tileId, bool isMoqie) {
        if (tileList.Count == 0) return;
        if (isMoqie && tileList[tileList.Count - 1] == tileId) {
            tileList.RemoveAt(tileList.Count - 1);
            return;
        }
        RecordSettlementSimContext.RemoveOneTile(tileList, tileId);
    }

    internal static int[] BuildJiagangMask(RecordSimPlayer player, int jiagangTile, int actualJiaTile) {
        string kCombo = GameRecordMeldCodec.BuildCombinationKey('k', jiagangTile);
        int idx = GameRecordMeldCodec.FindCombinationIndex(player.combinationTiles, kCombo);
        if (idx >= 0) {
            player.combinationTiles[idx] = GameRecordMeldCodec.BuildCombinationKey('g', jiagangTile);
            var updatedMask = new List<int>(player.combinationMasks[idx]);
            for (int j = 0; j < updatedMask.Count; j++) {
                if (updatedMask[j] != 1) continue;
                updatedMask.Insert(j, actualJiaTile);
                updatedMask.Insert(j, 3);
                break;
            }
            player.combinationMasks[idx] = updatedMask.ToArray();
            return player.combinationMasks[idx];
        }

        int[] fallback = { 0, jiagangTile, 3, actualJiaTile, 1, jiagangTile, 0, jiagangTile };
        player.combinationTiles.Add($"g{jiagangTile}");
        player.combinationMasks.Add(fallback);
        return fallback;
    }
}
