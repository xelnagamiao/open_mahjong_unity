using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public sealed class HongKongRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterRoundProperty("hongkong", (round, token) =>
            round.hongkong = (token as JObject)?.ToObject<HongKongInfo>());
        RuleRecordCodecRegistry.RegisterTickScoreHandler("hongkong", (round, tick) => {
            if (tick.Count >= 3 && tick[1] == "score") {
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, RuleRecordCodecRegistry.ParseScoreChangesArray(tick, 2));
                return true;
            }
            return false;
        });
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "hongkong" || tick.Count < 2) return false;
        if (tick[1] == "score") {
            RecordSettlementSimContext.AccumulateSeatScores(sim.ImmediateScores, RecordSettlementSimContext.ParseScoreChanges(tick, 2));
        } else if (tick[1] == "opening_complete") {
            int[][] hands = JArray.Parse(tick[2]).ToObject<int[][]>();
            for (int i = 0; i < 4; i++) sim.Players[i].tileList = new List<int>(hands[i]);
            sim.CurrentPlayerIndex = 0;
        } else if (tick[1] == "win_source") {
            sim.LastWinnableTileId = RecordSettlementSimContext.ParseInt(tick, 5);
            int payer = RecordSettlementSimContext.ParseInt(tick, 3);
            if (payer >= 0) sim.LastDiscardPlayerIndex = payer;
            if (tick[4] == "rob_kong" && payer >= 0 && bool.Parse(tick[7]))
                RecordSettlementSimContext.RemoveOneTile(sim.Players[payer].tileList, sim.LastWinnableTileId);
        }
        return true;
    }
}
