using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>日麻牌谱解码与计分板提取编解码器。</summary>
public sealed class RiichiRecordSettlementCodec : RuleRecordSettlementCodec {
    private readonly int[] riichiPayments = new int[4];

    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterRoundProperty("washizu", (round, token) =>
            round.washizu = (token as JObject)?.ToObject<WashizuRoundExtras>());
        RuleRecordCodecRegistry.RegisterRoundProperty("riichi", (round, token) => {
            if (token is JObject riichiObj) {
                round.riichi = new RiichiRoundExtras {
                    honba = riichiObj["honba"]?.Value<int>() ?? 0,
                    riichiSticks = riichiObj["riichi_sticks"]?.Value<int>() ?? 0,
                    doraMarker = riichiObj["dora_marker"]?.Value<int>() ?? 0,
                };
            }
        });
        RuleRecordCodecRegistry.RegisterTickScoreHandler("riichi", (round, tick) => {
            if (tick.Count >= 2 && int.TryParse(tick[1], out int seat) && seat >= 0 && seat < 4) {
                var sc = new int[4];
                sc[seat] = -1000;
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, sc);
            }
            return true;
        });
        RuleRecordCodecRegistry.RegisterTickScoreHandler("hu_riichi", (round, tick) => {
            if (tick.Count >= 7) {
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, RuleRecordCodecRegistry.ParseScoreChangesArray(tick, 6));
            }
            return true;
        });
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        string action = tick[0];
        if (action == "riichi") {
            if (tick.Count >= 2 && int.TryParse(tick[1], out int payer) && payer >= 0 && payer < 4)
                riichiPayments[payer] -= 1000;
            return true;
        }
        if (action == "hu_riichi") {
            RoundSettlementSnapshot snap = BuildRiichiHuSnapshot(tick, sim);
            sim.LastRow = new ScoreHistoryRecordSettlementExtractor.RecordScoreRow {
                snapshot = snap,
                scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(
                    IncludeRiichiPayments(RecordSettlementSimContext.ParseScoreChanges(tick, 6)), sim.Round.seats),
                roundNumber = sim.RoundNumber,
            };
            sim.Output.Add(sim.LastRow);
            return true;
        }
        return false;
    }

    public override int[] AdjustRowScoreChanges(RecordSettlementSimContext sim, string action, int[] seatChanges) {
        if (action == "ryuukyoku" || action == "liuju" || action == "jiuzhongjiupai" || action == "blood") {
            return IncludeRiichiPayments(seatChanges ?? new int[4]);
        }
        return seatChanges;
    }

    public override void OnRecordFinished(
        Dictionary<string, object> title,
        string rule,
        List<ScoreHistoryRecordSettlementExtractor.RecordScoreRow> rows) {
        if (rows.Count == 0 || title == null
            || !title.TryGetValue("riichi_final_scores", out object finalRaw) || finalRaw == null) return;
        int[] finalScores = JArray.FromObject(finalRaw).ToObject<int[]>();
        string subRule = title.TryGetValue("sub_rule", out object sr) && sr != null ? sr.ToString() : "";
        int playerCount = MahjongPlayerCount.ForSubRule(subRule);
        if (finalScores == null || finalScores.Length != playerCount) return;
        int[] totals;
        if (title.TryGetValue("starting_scores", out object startingRaw)) {
            totals = JArray.FromObject(startingRaw).ToObject<int[]>();
            if (totals == null || totals.Length != playerCount) return;
        } else if (title.TryGetValue("starting_score", out object startingScore)) {
            int start = Convert.ToInt32(startingScore);
            totals = new[] { start, start, start, start };
        } else {
            return;
        }
        foreach (var row in rows) RecordSettlementSimContext.AccumulateSeatScores(totals, row.scoreChangesByOriginal);
        var last = rows[rows.Count - 1];
        last.scoreChangesByOriginal ??= new int[4];
        for (int i = 0; i < playerCount; i++) last.scoreChangesByOriginal[i] += finalScores[i] - totals[i];
    }

    private int[] IncludeRiichiPayments(int[] changes) {
        var result = new int[4];
        RecordSettlementSimContext.AccumulateSeatScores(result, changes);
        RecordSettlementSimContext.AccumulateSeatScores(result, riichiPayments);
        Array.Clear(riichiPayments, 0, riichiPayments.Length);
        return result;
    }

    private static RoundSettlementSnapshot BuildRiichiHuSnapshot(List<string> tick, RecordSettlementSimContext sim) {
        int hepaiPlayerIndex = RecordSettlementSimContext.ParseInt(tick, 1);
        string huClass = tick.Count > 2 ? tick[2] : "hu_self";
        int han = tick.Count > 3 ? RecordSettlementSimContext.ParseInt(tick, 3) : 0;
        int fu = tick.Count > 4 ? RecordSettlementSimContext.ParseInt(tick, 4) : 0;
        string[] yaku = tick.Count > 5 ? RecordSettlementSimContext.ParseFanList(tick, 5) : Array.Empty<string>();

        RecordSimPlayer huPlayer = sim.Players[hepaiPlayerIndex];
        RecordHuHandBuilder.TryParseHepaiTile(tick, sim.SubRule, out int hepaiTile);
        int[] hand = RecordSettlementSimContext.BuildScoreboardHuHand(huPlayer.tileList, huClass, hepaiTile, sim.LastWinnableTileId);

        int winnerDelta = 0;
        int[] scoreChanges = tick.Count > 6 ? RecordSettlementSimContext.ParseScoreChanges(tick, 6) : null;
        if (scoreChanges != null && hepaiPlayerIndex >= 0 && hepaiPlayerIndex < scoreChanges.Length) {
            winnerDelta = scoreChanges[hepaiPlayerIndex];
        }

        return new RoundSettlementSnapshot {
            hasWin = yaku.Length > 0 && hand.Length > 0,
            isLiuju = false,
            huClass = huClass,
            hepaiPlayerIndex = hepaiPlayerIndex,
            winnerUsername = RecordSettlementSimContext.ResolveUsernameForSeat(hepaiPlayerIndex, sim.Round, sim.GameTitle),
            huScore = winnerDelta,
            winnerScoreDelta = winnerDelta,
            huFan = yaku,
            han = han,
            fu = fu,
            hepaiPlayerHand = hand,
            combinationMask = RecordSettlementSimContext.CloneMasks(huPlayer.combinationMasks),
            subRule = sim.SubRule,
        };
    }
}
