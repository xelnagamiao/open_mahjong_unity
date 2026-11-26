using System.Collections.Generic;

public sealed class HongzhongRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterTickScoreHandler("hongzhong", (round, tick) => {
            if (tick.Count >= 3 && (tick[1] == "kong_score" || tick[1] == "kong_refund")) {
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, RuleRecordCodecRegistry.ParseScoreChangesArray(tick, 2));
                return true;
            }
            return false;
        });
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "hongzhong" || tick.Count < 2) return false;
        if (tick.Count >= 3 && (tick[1] == "kong_score" || tick[1] == "kong_refund"))
            RecordSettlementSimContext.AccumulateSeatScores(sim.ImmediateScores, RecordSettlementSimContext.ParseScoreChanges(tick, 2));
        return true;
    }

    public override void OnRoundFinished(RecordSettlementSimContext sim) {
        if (sim.LastRow?.snapshot?.hepaiPlayerIndex >= 0 && sim.LastRow.snapshot.hepaiPlayerIndex < 4) {
            sim.LastRow.snapshot.winnerScoreDelta += sim.ImmediateScores[sim.LastRow.snapshot.hepaiPlayerIndex];
        }
    }
}
