using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public sealed class ChangchunRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterTickScoreHandler("cc", (round, tick) => {
            if (tick.Count < 2) return false;
            var e = JObject.Parse(tick[1]);
            if ((string)e["kind"] == "kong_score") {
                var sc = new int[4];
                for (int i = 0; i < 4; i++) sc[i] = (int?)e["delta"]?[i.ToString()] ?? 0;
                RuleRecordCodecRegistry.AddSeatScoreChanges(round, sc);
                return true;
            }
            return false;
        });
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick.Count < 2 || tick[0] != "cc") return false;
        var e = JObject.Parse(tick[1]);
        string kind = (string)e["kind"];
        int index = (int?)e["player"] ?? -1;
        var p = index >= 0 && index < 4 ? sim.Players[index] : null;
        if (kind == "kong_score") {
            foreach (var delta in e["delta"].ToObject<Dictionary<int, int>>())
                if (delta.Key >= 0 && delta.Key < 4) sim.ImmediateScores[delta.Key] += delta.Value;
        } else if (kind == "special" && p != null) {
            var tiles = e["physical"].ToObject<int[]>();
            foreach (int tile in tiles) RecordSettlementSimContext.RemoveOneTile(p.tileList, tile);
            p.combinationTiles.Add((string)e["code"]);
            p.combinationMasks.Add(tiles.SelectMany(t => new[] { 0, t }).ToArray());
        } else if ((kind == "added_offer" || kind == "tail_pass") && p != null) {
            if (e["tile"] != null) RecordSettlementSimContext.RemoveOneTile(p.tileList, (int)e["tile"]);
            if (kind == "tail_pass") sim.CurrentPlayerIndex = (index + 1) % 4;
        } else if (kind == "added_commit" && p != null) {
            int pos = (int)e["position"];
            string code = (string)e["code"];
            p.combinationTiles[pos] = code;
            p.combinationMasks[pos] = ChangchunHandCalculator.MeldTiles(code).SelectMany(t => new[] { 0, t }).ToArray();
        } else if (kind == "added_robbed" && p != null && (bool?)e["special"] != true) {
            int tile = (int)e["tile"];
            int pos = p.combinationTiles.IndexOf("g" + tile);
            if (pos >= 0) {
                p.combinationTiles[pos] = "k" + tile;
                var mask = p.combinationMasks[pos].ToList();
                int offset = Enumerable.Range(0, mask.Count / 2).FirstOrDefault(i => mask[2 * i] == 3) * 2;
                mask.RemoveRange(offset, 2);
                p.combinationMasks[pos] = mask.ToArray();
            }
        } else if (kind == "settlement") {
            sim.LastWinnableTileId = (int?)e["tile"] ?? -1;
            if ((string)e["source"] == "bao_indicator" && sim.LastWinnableTileId > 0)
                sim.Players[(int)e["winner"]].tileList.Add(sim.LastWinnableTileId);
        }
        return true;
    }
}
