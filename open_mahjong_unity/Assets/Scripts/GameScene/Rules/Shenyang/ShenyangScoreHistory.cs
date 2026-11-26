using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public sealed class ShenyangRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterTickScoreHandler("sy", (round, tick) => {
            if (tick.Count < 2) return false;
            var e = JObject.Parse(tick[1]);
            if ((string)e["kind"] == "egg") {
                var sc = new int[4];
                for (int i = 0; i < 4; i++) sc[i] = (int?)e["changes"]?[i.ToString()] ?? 0;
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, sc);
                return true;
            }
            return false;
        });
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick.Count < 2 || tick[0] != "sy") return false;
        var e = JObject.Parse(tick[1]);
        string kind = (string)e["kind"];
        int actor = (int?)e["player"] ?? -1;
        var p = actor >= 0 && actor < 4 ? sim.Players[actor] : null;
        if (kind == "egg") {
            foreach (var delta in e["changes"].ToObject<Dictionary<int, int>>())
                if (delta.Key >= 0 && delta.Key < 4) sim.ImmediateScores[delta.Key] += delta.Value;
        } else if (kind == "special" && p != null) {
            foreach (int tile in e["physical"].ToObject<int[]>()) RecordSettlementSimContext.RemoveOneTile(p.tileList, tile);
            p.combinationTiles.Add((string)e["code"]);
            p.combinationMasks.Add(e["mask"].ToObject<int[]>());
        } else if (kind == "robbed" && p != null) {
            p.combinationTiles = e["melds"].ToObject<List<string>>();
            p.combinationMasks = e["masks"].ToObject<List<int[]>>();
        } else if (kind == "tail_pass" && p != null) {
            RecordSettlementSimContext.RemoveOneTile(p.tileList, (int)e["tile"]);
            sim.CurrentPlayerIndex = (actor + 1) % 4;
        }
        return true;
    }
}
