using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public sealed class ShangraoRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterRoundProperty("shangrao", (round, token) =>
            round.shangrao = (token as JObject)?.ToObject<ShangraoInfo>());
        RuleRecordCodecRegistry.RegisterRoundProperty("shangrao_hints", (round, token) =>
            round.shangraoHints = (token as JObject)?.ToObject<Dictionary<int, ShangraoHints>>());
        RuleRecordCodecRegistry.RegisterTickScoreHandler("shangrao", (round, tick) =>
            RuleRecordCodecRegistry.ApplyStartScoreDiff(round, tick, round.shangrao?.start_scores));
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "shangrao" || tick.Count < 3) return false;
        if (tick[1] == "payment") {
            RecordSettlementSimContext.AccumulateSeatScores(sim.ImmediateScores, JObject.Parse(tick[2])["changes"].ToObject<int[]>());
        } else if (tick[1] == "win_source") {
            sim.LastWinnableTileId = RecordSettlementSimContext.ParseInt(tick, 5);
        } else if (tick[1] == "state") {
            RecordSettlementSimContext.ApplyStartScoreDiffToLastRow(sim, tick, sim.Round.shangrao?.start_scores);
        }
        return true;
    }
}
