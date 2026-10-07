using System.Collections.Generic;
using UnityEngine;

/// <summary>头像听牌条与展开的和牌提示共用同一份牌谱查询、可见牌计数。</summary>
public static class RecordWaitHintCalculator {
    public static List<WaitHintQuery> BuildQueries(
        RecordTipsContext ctx, List<int> hand, List<int> waiting, int? pendingCut = null) {
        var result = new List<WaitHintQuery>();
        var way = new List<string>();
        if (ctx.SelfHuapaiList != null) {
            foreach (int _ in ctx.SelfHuapaiList) way.Add("花牌");
        }
        int wind = (ctx.CurrentRound - 1) / MahjongPlayerCount.ForSubRule(ctx.SubRule);
        if (wind >= 0 && wind < 4) way.Add("场风" + "东南西北"[wind]);
        switch (ctx.SelfPlayerIndex) {
            case 0: way.Add("自风东"); break;
            case 1: way.Add("自风南"); break;
            case 2: way.Add("自风西"); break;
            case 3: way.Add("自风北"); break;
        }
        if (waiting.Count == 1) way.Add("和单张");

        var discards = new List<IReadOnlyList<int>>();
        var combinations = new List<string>();
        var melds = new List<string>();
        if (ctx.PlayersByPosition != null) {
            foreach (var player in ctx.PlayersByPosition.Values) {
                discards.Add(player?.DiscardTiles);
                if (player?.CombinationTiles != null) combinations.AddRange(player.CombinationTiles);
            }
            if (ctx.PlayersByPosition.TryGetValue("self", out var self) && self?.CombinationTiles != null) {
                melds.AddRange(self.CombinationTiles);
            }
        }
        var sortedWaiting = new List<int>(waiting);
        sortedWaiting.Sort();
        foreach (int tile in sortedWaiting) {
            var single = new List<string>();
            int shown = HeJuezhangTableCounter.CountShowTilesOnTable(
                tile, discards, combinations, pendingCut, strictCombinationMatch: true);
            if (HeJuezhangTableCounter.ShouldAddHeJuezhangForTips(shown)) single.Add("和绝张");
            var merged = new List<string>(way);
            merged.AddRange(single);
            merged.Add("点和");
            result.Add(new WaitHintQuery {
                HepaiTile = tile,
                HandWithWin = new List<int>(hand) { tile },
                Melds = new List<string>(melds),
                MeldMasks = ctx.SelfCombinationMasks,
                WayToHepai = new List<string>(way),
                SingleTileWay = single,
                MergedWay = merged,
                HuapaiCount = ctx.SelfHuapaiList?.Count ?? 0,
                SubRule = ctx.SubRule,
                HepaiLimit = ctx.HepaiLimit,
                SelfIndex = ctx.SelfPlayerIndex,
                CurrentRound = ctx.CurrentRound,
                SelfFlowers = ctx.SelfHuapaiList != null ? new List<int>(ctx.SelfHuapaiList) : new List<int>(),
                DetailedConfig = ctx.DetailedConfig,
                ExcludedSuit = ctx.SelfDingqueSuit,
                Record = ctx,
            });
        }
        return result;
    }

    public static Dictionary<int, int> CountVisibleTiles(
        RecordTipsContext ctx, List<int> hand, int? pendingCut = null) {
        var counts = new Dictionary<int, int>();
        RuleRegistry.TryResolve(ctx.RoomRule, ctx.SubRule, out RuleManifest manifest);
        void Add(int tile) {
            int key = manifest?.NormalizeTileId != null ? manifest.NormalizeTileId(tile) : tile;
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }
        if (hand != null) foreach (int tile in hand) Add(tile);
        if (ctx.SelfKnownConcealedDiscards != null) foreach (int tile in ctx.SelfKnownConcealedDiscards) Add(tile);
        if (pendingCut.HasValue) Add(pendingCut.Value);
        if (ctx.DoraIndicators != null) foreach (int tile in ctx.DoraIndicators) Add(tile);
        if (ctx.PlayersByPosition == null) return counts;
        foreach (var entry in ctx.PlayersByPosition) {
            var player = entry.Value;
            if (player?.DiscardTiles != null) foreach (int tile in player.DiscardTiles) Add(tile);
            if (ctx.RoomRule == WenzhouGameState.RuleId && player?.CombinationMasks != null) {
                foreach (var mask in player.CombinationMasks) for (int i = 1; mask != null && i < mask.Length; i += 2) Add(mask[i]);
                continue;
            }
            if (player?.CombinationTiles == null) continue;
            foreach (string meld in player.CombinationTiles) {
                int[] explicitTiles = manifest?.VisibleMeldTiles?.Invoke(meld, entry.Key == "self");
                if (explicitTiles != null) { foreach (int physicalTile in explicitTiles) Add(physicalTile); continue; }
                if (string.IsNullOrEmpty(meld) || meld.Length < 2 || !int.TryParse(meld.Substring(1), out int tile)) continue;
                switch (char.ToLowerInvariant(meld[0])) {
                    case 's': Add(tile - 1); Add(tile); Add(tile + 1); break;
                    case 'k': Add(tile); Add(tile); Add(tile); break;
                    case 'g': Add(tile); Add(tile); Add(tile); Add(tile); break;
                    case 'q': Add(tile); Add(tile); break;
                }
            }
        }
        return counts;
    }

    public static int Remaining(int tile, Dictionary<int, int> visible, RuleManifest manifest) {
        int key = manifest?.NormalizeTileId != null ? manifest.NormalizeTileId(tile) : tile;
        int supply = manifest?.RuleId == "guangdong" && GuangdongMilRules.IsGhost(tile) ? 1 : 4;
        return Mathf.Clamp(supply - (visible.TryGetValue(key, out int used) ? used : 0), 0, supply);
    }
}
