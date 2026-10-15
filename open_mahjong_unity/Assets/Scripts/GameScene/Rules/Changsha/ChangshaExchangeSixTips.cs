using System;
using System.Collections.Generic;
using System.Linq;

// 实战使用服务器给出的出牌后听牌提示；回放按记录恢复报听、相公和锁定牌型。
internal static class ChangshaExchangeSixTips {
    internal static string Key(IEnumerable<int> hand, IEnumerable<string> melds) =>
        string.Join(",", (hand ?? Array.Empty<int>()).OrderBy(tile => tile)) + "|" +
        string.Join(",", (melds ?? Array.Empty<string>()).OrderBy(meld => meld));

    internal static ChangshaExchangeSixWait[] Live(List<int> hand, List<string> melds) {
        var hints = ChangshaGameState.Active?.ExchangeSixSnapshot?.private_state?.hints;
        if (hints?.hand == null) return Array.Empty<ChangshaExchangeSixWait>();
        string key = Key(hand, melds);
        if (key == Key(hints.hand, hints.melds)) return hints.waits ?? Array.Empty<ChangshaExchangeSixWait>();
        if (hints.discard_waits != null) foreach (var item in hints.discard_waits) {
            var remaining = new List<int>(hints.hand);
            if (remaining.Remove(item.Key) && key == Key(remaining, hints.melds)) return item.Value;
        }
        return Array.Empty<ChangshaExchangeSixWait>();
    }

    internal static HashSet<int> Waiting(TingpaiQuery query) {
        var waits = query.RecordPlayerIndex.HasValue
            ? GameRecordManager.Instance.ExchangeSixRecordWaits(query.RecordPlayerIndex.Value, query.Hand, query.Melds)
            : Live(query.Hand, query.Melds);
        return new HashSet<int>(waits.Select(wait => wait.tile));
    }

    internal static WaitTileHint Describe(WaitHintQuery query) {
        var hand = new List<int>(query.HandWithWin);
        hand.Remove(query.HepaiTile);
        var waits = query.Record != null
            ? GameRecordManager.Instance.ExchangeSixRecordWaits(query.Record.SelfPlayerIndex, hand, query.Melds)
            : Live(hand, query.Melds);
        var wait = waits.FirstOrDefault(item => item.tile == query.HepaiTile);
        return wait == null ? WaitTileHint.None("暂无提示") : wait.blocked_small_hu
            ? WaitTileHint.None("本局不能胡普通小胡，升为大胡后可胡") : WaitTileHint.Ron(string.Join(" + ", wait.fans) + " " + wait.score + "分");
    }
}

// 仅用于牌谱提示的固定玩法基础算牌；不改变普通长沙算牌器。
internal static class ChangshaExchangeSixShape {
    internal static readonly int[] Tiles = Enumerable.Range(11, 9).Concat(Enumerable.Range(21, 9)).Concat(Enumerable.Range(31, 9)).ToArray();
    private static readonly HashSet<int> Terminals = new HashSet<int> {11, 19, 21, 29, 31, 39};
    private static bool Jiang(int tile) => tile % 10 == 2 || tile % 10 == 5 || tile % 10 == 8;
    internal static int Score(IEnumerable<string> fans) {
        int weight = fans.Where(fan => fan != "小胡").Sum(fan => fan == "三豪华七小对" ? 4 : fan == "双豪华七小对" ? 3
            : fan == "豪华七小对" || fan == "无对十三将" || fan == "全幺九" ? 2 : 1);
        return weight == 0 ? 2 : 8 * (1 << (weight - 1));
    }

    internal static List<string> Fans(List<int> hand, List<string> melds, bool reportedReady) {
        melds = melds ?? new List<string>();
        if (melds.Count > 4 || hand == null || hand.Count != 14 - 3 * melds.Count) return new List<string>();
        var all = new List<int>(hand);
        foreach (string meld in melds) {
            if (string.IsNullOrEmpty(meld) || !"sSkKgG".Contains(meld[0]) || !int.TryParse(meld.Substring(1), out int tile)) return new List<string>();
            if (meld[0] == 's' || meld[0] == 'S') all.AddRange(new[] {tile - 1, tile, tile + 1});
            else all.AddRange(Enumerable.Repeat(tile, meld[0] == 'g' || meld[0] == 'G' ? 4 : 3));
        }
        if (all.Any(tile => !Tiles.Contains(tile)) || all.GroupBy(tile => tile).Any(group => group.Count() > 4)) return new List<string>();
        var fans = new List<string>(ChangshaExternal.HepaiCheck(hand, melds, new List<string>(), 0, false).Item2);
        if (!melds.All(meld => "skg".Contains(meld[0]))) fans.Remove("全求人");
        if (all.All(Jiang) && !fans.Contains("将将胡")) fans.Add("将将胡");
        if (melds.Count == 0 && Terminals.All(tile => hand.Count(value => value == tile) == 1)) {
            var remaining = hand.Where(tile => !Terminals.Contains(tile)).ToList();
            if (remaining.All(Jiang)) {
                int distinct = remaining.Distinct().Count();
                if (distinct == 7) fans.Add("十三将");
                if (distinct == 8) fans.Add("无对十三将");
            }
        }
        if (all.All(Terminals.Contains)) fans.Add("全幺九");
        if (fans.Count == 0 && StandardShape(hand, melds.Count, !reportedReady)) fans.Add("小胡");
        if (fans.Count > 1) fans.Remove("小胡");
        return fans;
    }

    private static bool StandardShape(List<int> hand, int meldCount, bool jiangPair) {
        foreach (int pair in hand.Distinct()) {
            if (jiangPair && !Jiang(pair)) continue;
            if (hand.Count(tile => tile == pair) < 2) continue;
            var remaining = new List<int>(hand);
            remaining.Remove(pair); remaining.Remove(pair);
            if (Sets(remaining.OrderBy(tile => tile).ToList(), 4 - meldCount)) return true;
        }
        return false;
    }

    private static bool Sets(List<int> hand, int count) {
        if (count == 0) return hand.Count == 0;
        if (hand.Count == 0) return false;
        int tile = hand[0];
        if (hand.Count(value => value == tile) >= 3) {
            var remaining = new List<int>(hand);
            for (int i = 0; i < 3; i++) remaining.Remove(tile);
            if (Sets(remaining, count - 1)) return true;
        }
        if (tile % 10 <= 7 && hand.Contains(tile + 1) && hand.Contains(tile + 2)) {
            var remaining = new List<int>(hand);
            remaining.Remove(tile); remaining.Remove(tile + 1); remaining.Remove(tile + 2);
            if (Sets(remaining, count - 1)) return true;
        }
        return false;
    }
}
