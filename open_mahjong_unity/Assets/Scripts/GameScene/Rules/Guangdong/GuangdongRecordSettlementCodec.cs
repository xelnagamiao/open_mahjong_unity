using System.Collections.Generic;

/// <summary>广东（推倒和 / MIL 2023 花鬼 / 2V2 双打）牌谱计分板编解码器。</summary>
public sealed class GuangdongRecordSettlementCodec : SichuanRecordSettlementCodec {
    public GuangdongRecordSettlementCodec() : base(isSichuanFamily: false) { }

    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterTickScoreHandler("guangdong", (round, tick) => {
            if (tick.Count >= 3 && (tick[1] == "kong_score" || tick[1] == "refund_kongs" || tick[1] == "penalty")) {
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, RuleRecordCodecRegistry.ParseScoreChangesArray(tick, 2));
                return true;
            }
            return false;
        });
        RuleRecordCodecRegistry.RegisterTickScoreHandler("tuidao", (round, tick) => {
            if (tick.Count >= 3 && tick[1] == "kong_score") {
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, RuleRecordCodecRegistry.ParseScoreChangesArray(tick, 2));
                return true;
            }
            return false;
        });
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        string action = tick[0];
        if (action == "guangdong" && GuangdongMilRules.IsMil(sim.SubRule)) {
            if (tick.Count >= 3 && (tick[1] == "kong_score" || tick[1] == "refund_kongs" || tick[1] == "penalty"))
                RecordSettlementSimContext.AccumulateSeatScores(sim.ImmediateScores, RecordSettlementSimContext.ParseScoreChanges(tick, 2));
            return true;
        }
        if (action == "tuidao" && tick.Count >= 3 && tick[1] == "kong_score") {
            RecordSettlementSimContext.AccumulateSeatScores(sim.ImmediateScores, RecordSettlementSimContext.ParseScoreChanges(tick, 2));
            return true;
        }
        return false;
    }

    public override void OnRoundFinished(RecordSettlementSimContext sim) {
        if (GuangdongMilRules.IsMil(sim.SubRule)
            && sim.LastRow?.snapshot?.hepaiPlayerIndex >= 0
            && sim.LastRow.snapshot.hepaiPlayerIndex < 4) {
            sim.LastRow.snapshot.winnerScoreDelta += sim.ImmediateScores[sim.LastRow.snapshot.hepaiPlayerIndex];
        }
    }
}
