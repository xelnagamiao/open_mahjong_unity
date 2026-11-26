using System.Collections.Generic;

public sealed class SingaporeRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterTickScoreHandler("singapore", (round, tick) => {
            if (tick.Count >= 3 && tick[1] == "payment") {
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, RuleRecordCodecRegistry.ParseScoreChangesArray(tick, 2));
                return true;
            }
            return false;
        });
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "singapore" || tick.Count < 3) return false;
        if (tick[1] == "payment")
            RecordSettlementSimContext.AccumulateSeatScores(sim.ImmediateScores, RecordSettlementSimContext.ParseScoreChanges(tick, 2));
        return true;
    }
}
