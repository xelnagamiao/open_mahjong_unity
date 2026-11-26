using System;
using System.Collections.Generic;

public sealed class HongqueRecordSettlementCodec : RuleRecordSettlementCodec {
    public override bool TryHandleAddedKong(RecordSettlementSimContext sim, List<string> tick, int actingPlayerIndex) {
        RecordSimPlayer actor = sim.Players[actingPlayerIndex];
        int tile = RecordSettlementSimContext.ParseInt(tick, 1);
        List<int> removedTiles = HongqueReplayKong.RemoveTiles(actor.tileList, tick);
        sim.LastWinnableTileId = removedTiles.Count > 0 ? removedTiles[0] : tile;
        if ((tick.Count != 4 && tick.Count != 5) || !int.TryParse(tick[3], out int targetMeld))
            throw new InvalidOperationException("虹雀牌谱缺少杠牌目标组，请先运行一次性牌谱迁移脚本。");
        HongqueDisplayOrder.ExtendRecordedMeld(actor.combinationMasks, actor.combinationTiles, targetMeld,
            HongqueReplayKong.AddedTiles(tick), sim.SubRule == "hongque/w-v1.0");
        sim.CurrentPlayerIndex = actingPlayerIndex;
        return true;
    }
}
