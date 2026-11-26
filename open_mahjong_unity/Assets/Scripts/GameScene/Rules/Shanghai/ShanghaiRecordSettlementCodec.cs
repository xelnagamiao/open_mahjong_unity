using System.Collections.Generic;

public sealed class ShanghaiRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterTickScoreHandler("lajihe_eight_flowers", (round, tick) => {
            if (tick.Count >= 5) {
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, RuleRecordCodecRegistry.ParseScoreChangesArray(tick, 4));
                return true;
            }
            return false;
        });
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "lajihe_eight_flowers") return false;
        int winner = RecordSettlementSimContext.ParseInt(tick, 1);
        var snapshot = new RoundSettlementSnapshot {
            subRule = sim.SubRule,
            huClass = "lajihe_eight_flowers",
            hasWin = false,
            huScore = 30,
            huFan = new[] { "八花齐" },
            hepaiPlayerIndex = winner,
            winnerScoreDelta = 90,
        };
        sim.LastRow = new ScoreHistoryRecordSettlementExtractor.RecordScoreRow {
            snapshot = snapshot,
            scoreChangesByOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(
                RecordSettlementSimContext.ParseScoreChanges(tick, 4), sim.Round.seats),
            roundNumber = sim.RoundNumber,
        };
        sim.Output.Add(sim.LastRow);
        return true;
    }
}
