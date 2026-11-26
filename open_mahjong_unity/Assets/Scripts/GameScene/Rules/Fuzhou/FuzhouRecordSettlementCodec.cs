using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class FuzhouRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterRoundProperty("fuzhou", (round, token) =>
            round.fuzhou = (token as JObject)?.ToObject<FuzhouInfo>());
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "fuzhou" || tick.Count < 3) return false;
        if (tick[1] == "qiangjin_take") {
            sim.Players[RecordSettlementSimContext.ParseInt(tick, 2)].tileList.Add(RecordSettlementSimContext.ParseInt(tick, 3));
        } else if (tick[1] == "win_source") {
            sim.LastWinnableTileId = RecordSettlementSimContext.ParseInt(tick, 5);
            sim.LastDiscardPlayerIndex = RecordSettlementSimContext.ParseInt(tick, 3);
            var info = JsonConvert.DeserializeObject<FuzhouInfo>(tick[6]);
            if (info?.final_hands != null)
                foreach (var pair in info.final_hands) sim.Players[pair.Key].tileList = new List<int>(pair.Value);
        } else if (tick[1] == "state" && sim.LastRow != null && sim.Round.fuzhou?.start_scores?.Length == 4 && tick.Count > 3) {
            RecordSettlementSimContext.ApplyStartScoreDiffToLastRow(sim, tick, sim.Round.fuzhou.start_scores);
            var info = JsonConvert.DeserializeObject<FuzhouInfo>(tick[2]);
            if (sim.LastRow.snapshot?.hepaiPlayerIndex >= 0 && info?.final_hands != null
                && info.final_hands.TryGetValue(sim.LastRow.snapshot.hepaiPlayerIndex, out int[] hand))
                sim.LastRow.snapshot.hepaiPlayerHand = hand;
        }
        return true;
    }
}
