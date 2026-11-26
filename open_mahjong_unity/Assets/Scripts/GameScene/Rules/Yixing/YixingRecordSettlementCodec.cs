using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public sealed class YixingRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterRoundProperty("yixing", (round, token) =>
            round.yixing = (token as JObject)?.ToObject<YixingInfo>());
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "yixing" || tick.Count < 2) return false;
        if (tick[1] == "win_source") {
            sim.LastWinnableTileId = RecordSettlementSimContext.ParseInt(tick, 5);
            int payer = RecordSettlementSimContext.ParseInt(tick, 3);
            if (payer >= 0) sim.LastDiscardPlayerIndex = payer;
            if (tick[4] == "rob_kong" && payer >= 0)
                RecordSettlementSimContext.RemoveOneTile(sim.Players[payer].tileList, sim.LastWinnableTileId);
        }
        return true;
    }
}
