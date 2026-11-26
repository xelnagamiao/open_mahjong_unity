using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed class MalaysiaRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterRoundProperty("malaysia", (round, token) =>
            round.malaysia = (token as JObject)?.ToObject<MalaysiaInfo>());
        RuleRecordCodecRegistry.RegisterTickScoreHandler("malaysia", (round, tick) => {
            if (tick.Count >= 3 && tick[1] == "payment") {
                int[] payment = JArray.Parse(tick[2]).ToObject<int[]>();
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, payment);
                return true;
            }
            return false;
        });
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "malaysia" || tick.Count < 3) return false;
        if (tick[1] == "win_source") {
            sim.LastWinnableTileId = RecordSettlementSimContext.ParseInt(tick, 5);
            int payer = RecordSettlementSimContext.ParseInt(tick, 3);
            if (payer >= 0) sim.LastDiscardPlayerIndex = payer;
            if (tick[4] == "rob_kong" && payer >= 0)
                RecordSettlementSimContext.RemoveOneTile(sim.Players[payer].tileList, sim.LastWinnableTileId);
        } else if (tick[1] == "redeem") {
            var p = sim.Players[RecordSettlementSimContext.ParseInt(tick, 2)];
            p.tileList[p.tileList.Count - 1] = 81;
            p.combinationMasks[RecordSettlementSimContext.ParseInt(tick, 4)][2 * RecordSettlementSimContext.ParseInt(tick, 5) + 1] =
                RecordSettlementSimContext.ParseInt(tick, 3);
        } else if (tick[1] == "state" && sim.LastRow != null) {
            var info = JsonConvert.DeserializeObject<MalaysiaInfo>(tick[2]);
            var changes = new int[4];
            foreach (var pair in info.round_changes) changes[pair.Key] = pair.Value;
            sim.LastRow.scoreChangesByOriginal = changes;
        }
        return true;
    }
}
