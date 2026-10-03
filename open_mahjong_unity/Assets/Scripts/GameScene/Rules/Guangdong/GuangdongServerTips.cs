using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>只索引已收到的权威提示，不在客户端重新解释癞子与起和门槛。</summary>
internal static class GuangdongServerTips {
    private static readonly Dictionary<string, GuangdongWait[]> live = new Dictionary<string, GuangdongWait[]>();
    private static readonly Dictionary<int, Dictionary<string, GuangdongWait[]>> replay = new Dictionary<int, Dictionary<string, GuangdongWait[]>>();
    private static string Key(IEnumerable<int> hand, IEnumerable<string> melds) =>
        string.Join(",", (hand ?? Array.Empty<int>()).OrderBy(t => t)) + "|" + string.Join(",", (melds ?? Array.Empty<string>()).OrderBy(m => m, StringComparer.Ordinal));
    private static Dictionary<string, GuangdongWait[]> Cache(bool record, int seat = 0) {
        if (!record) return live;
        if (!replay.TryGetValue(seat, out var cache)) replay[seat] = cache = new Dictionary<string, GuangdongWait[]>();
        return cache;
    }
    internal static void Clear(bool record = false) { if(record) replay.Clear(); else live.Clear(); }
    internal static void Accept(GuangdongTips tips, bool record = false, int seat = 0) {
        var cache = Cache(record, seat);
        cache.Clear();
        if (tips?.hand == null) return;
        if (tips.hand.Length % 3 == 1) cache[Key(tips.hand, tips.melds)] = tips.waits ?? Array.Empty<GuangdongWait>();
        if (tips.discard_waits != null) foreach (var entry in tips.discard_waits) {
            var hand = new List<int>(tips.hand);
            if (hand.Remove(entry.Key)) cache[Key(hand, tips.melds)] = entry.Value ?? Array.Empty<GuangdongWait>();
        }
    }
    internal static HashSet<int> Waiting(TingpaiQuery query) {
        // The record adapter supplies an absolute seat; live callers never read record hints.
        return Waiting(query, query.RecordPlayerIndex.HasValue, query.RecordPlayerIndex ?? 0);
    }
    internal static HashSet<int> Waiting(TingpaiQuery query, bool record, int seat = 0) {
        string key = Key(query.Hand, query.Melds);
        var cache = Cache(record, seat);
        return cache.TryGetValue(key, out var waits) ? new HashSet<int>(waits.Select(w => w.tile)) : new HashSet<int>();
    }
    private static string Format(string label, int? fan, int? score) => label
        + (fan.HasValue ? fan.Value + "番" : "") + (score.HasValue ? score.Value + "分" : "");
    internal static WaitTileHint Describe(WaitHintQuery query) {
        var hand = new List<int>(query.HandWithWin ?? new List<int>());
        hand.Remove(query.HepaiTile);
        var cache = Cache(query.Record != null, query.Record?.SelfPlayerIndex ?? 0);
        if (!cache.TryGetValue(Key(hand, query.Melds), out var waits)) return WaitTileHint.None("暂无提示");
        var wait = waits.FirstOrDefault(item => item.tile == query.HepaiTile);
        if (wait == null) return WaitTileHint.None("暂无提示");
        // Legacy fan/score fields describe the better outcome, never a specific ron value.
        string ron = Format("点和", wait.ron_fan, wait.ron_score);
        string self = Format("自摸", wait.self_draw_fan, wait.self_draw_score);
        if (wait.ron) return WaitTileHint.Ron(wait.self_draw ? ron + "\n" + self : ron);
        if (wait.self_draw) return WaitTileHint.TsumoOnly(self);
        return WaitTileHint.None("未起和");
    }
}
