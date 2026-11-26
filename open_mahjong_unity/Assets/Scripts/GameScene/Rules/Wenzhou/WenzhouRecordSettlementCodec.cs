using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>温州麻将牌谱解码与计分板提取编解码器。</summary>
public sealed class WenzhouRecordSettlementCodec : RuleRecordSettlementCodec {
    public static void RegisterRecordCodecs() {
        RuleRecordCodecRegistry.RegisterRoundProperty("wenzhou", (round, token) =>
            round.wenzhou = (token as JObject)?.ToObject<WenzhouInfo>());
        RuleRecordCodecRegistry.RegisterRoundProperty("wenzhou_waits", (round, token) =>
            round.wenzhouWaits = (token as JObject)?.ToObject<Dictionary<int, Dictionary<string, WenzhouWait[]>>>());
        RuleRecordCodecRegistry.RegisterTickScoreHandler("wenzhou", (round, tick) =>
            RuleRecordCodecRegistry.ApplyStartScoreDiff(round, tick, round.wenzhou?.start_scores));
    }

    public override bool TryHandleCustomTick(RecordSettlementSimContext sim, List<string> tick) {
        if (tick[0] != "wenzhou" || tick.Count < 3) return false;
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
            RecordSettlementSimContext.ApplyStartScoreDiffToLastRow(sim, tick, sim.Round.wenzhou?.start_scores);
        }
        return true;
    }

    public override bool TryHandleAddedKong(RecordSettlementSimContext sim, List<string> tick, int actingPlayerIndex) {
        RecordSimPlayer actor = sim.Players[actingPlayerIndex];
        int tile = RecordSettlementSimContext.ParseInt(tick, 1);
        if (tile == 46 && sim.Round.wenzhou?.caishen != 46) tile = sim.Round.wenzhou.white_natural;
        bool isMoGang = GameRecordJsonDecoder.ParseKanMoGangFlag(tick);
        int removeTarget = tick.Count > 3 ? RecordSettlementSimContext.ParseInt(tick, 3) : tile;
        List<int> removedTiles = GameRecordMeldCodec.RemoveNTilesByNormalized(actor.tileList, removeTarget, 1, preferDrawSlotFirst: isMoGang);
        int actualJia = removedTiles.Count > 0 ? removedTiles[0] : tile;
        sim.LastWinnableTileId = actualJia;
        ScoreHistoryRecordSettlementExtractor.BuildJiagangMask(actor, tile, actualJia);
        sim.CurrentPlayerIndex = actingPlayerIndex;
        return true;
    }
}
