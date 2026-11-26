using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public sealed class NanchangRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterRoundProperty("nanchang", (round, token) =>
            round.nanchang = (token as JObject)?.ToObject<NanchangInfo>());
        RuleRecordCodecRegistry.RegisterRoundProperty("nanchang_hints", (round, token) =>
            round.nanchangHints = (token as JObject)?.ToObject<Dictionary<int, Dictionary<string, NanchangWait[]>>>());
        RuleRecordCodecRegistry.RegisterTickScoreHandler("nanchang", (round, tick) =>
            RuleRecordCodecRegistry.ApplyStartScoreDiff(round, tick, round.nanchang?.start_scores));
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "nanchang" || tick.Count < 3) return false;
        if (tick[1] == "meld" && tick.Count >= 5) {
            var player = sim.Players[RecordSettlementSimContext.ParseInt(tick, 2)];
            string code = tick[4];
            int index = player.combinationTiles.FindLastIndex(c => c == code);
            if (index < 0) index = player.combinationTiles.Count - 1;
            if (index >= 0) {
                player.combinationTiles[index] = code;
                player.combinationMasks[index] = JArray.Parse(tick[3]).ToObject<int[]>();
            }
        } else if (tick[1] == "kong_claim_source") {
            sim.LastDiscardPlayerIndex = RecordSettlementSimContext.ParseInt(tick, 2);
            sim.LastWinnableTileId = RecordSettlementSimContext.ParseInt(tick, 3);
            RecordSettlementSimContext.RemoveOneTile(sim.Players[sim.LastDiscardPlayerIndex].tileList, sim.LastWinnableTileId);
        } else if (tick[1] == "win_source") {
            sim.LastWinnableTileId = RecordSettlementSimContext.ParseInt(tick, 5);
            int payer = RecordSettlementSimContext.ParseInt(tick, 3);
            if (payer >= 0) sim.LastDiscardPlayerIndex = payer;
            if (tick[4] == "rob_kong" && payer >= 0)
                RecordSettlementSimContext.RemoveOneTile(sim.Players[payer].tileList, sim.LastWinnableTileId);
        } else if (tick[1] == "state") {
            RecordSettlementSimContext.ApplyStartScoreDiffToLastRow(sim, tick, sim.Round.nanchang?.start_scores);
        }
        return true;
    }
}
