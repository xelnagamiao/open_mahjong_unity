using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class NanjingRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterRoundProperty("nanjing", (round, token) =>
            round.nanjing = (token as JObject)?.ToObject<NanjingInfo>());
        RuleRecordCodecRegistry.RegisterTickScoreHandler("nanjing", (round, tick) => {
            if (tick.Count >= 4 && tick[1] == "state" && round.nanjing?.start_scores?.Length == 4)
                return RuleRecordCodecRegistry.ApplyStartScoreDiff(round, tick, round.nanjing.start_scores);
            if (tick.Count >= 3 && tick[1] == "score") {
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, GameRecordJsonDecoder.ParseInlineGangScoreChanges(tick));
                return true;
            }
            return false;
        });
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "nanjing" || tick.Count < 3) return false;
        if (tick[1] == "win_source") {
            sim.LastWinnableTileId = RecordSettlementSimContext.ParseInt(tick, 5);
            int payer = RecordSettlementSimContext.ParseInt(tick, 3);
            sim.LastDiscardPlayerIndex = payer;
            if (tick[4] == "rob_kong" && payer >= 0)
                RecordSettlementSimContext.RemoveOneTile(sim.Players[payer].tileList, sim.LastWinnableTileId);
        } else if (tick[1] == "state" && sim.LastRow != null && sim.Round.nanjing?.start_scores?.Length == 4 && tick.Count > 3) {
            RecordSettlementSimContext.ApplyStartScoreDiffToLastRow(sim, tick, sim.Round.nanjing.start_scores);
            var info = JsonConvert.DeserializeObject<NanjingInfo>(tick[2]);
            if (sim.LastRow.snapshot?.hepaiPlayerIndex >= 0 && info?.final_hands != null
                && info.final_hands.TryGetValue(sim.LastRow.snapshot.hepaiPlayerIndex, out int[] hand))
                sim.LastRow.snapshot.hepaiPlayerHand = hand;
            while (sim.Output.Count > sim.FirstRoundRow + 1) sim.Output.RemoveAt(sim.FirstRoundRow);
        }
        return true;
    }
}
