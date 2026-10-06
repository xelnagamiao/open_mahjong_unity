using System.Collections.Generic;

/// <summary>
/// 四川听牌与和牌张提示：默认允许 0 番平和，自定义起和番按房间配置判断；
/// 手牌或副露仍含定缺牌时不提示听牌，并剔除定缺花色的和牌张。
/// </summary>
internal static class SichuanTips {
    public static HashSet<int> Tingpai(TingpaiQuery q) {
        int dingque = q.ExcludedSuit;
        if (dingque >= 1 && dingque <= 3) {
            if (q.Hand.Exists(t => t / 10 == dingque)) return new HashSet<int>();
            if (q.Melds != null && q.Melds.Exists(m => m.Length > 1 && int.TryParse(m.Substring(1), out int tile) && tile / 10 == dingque)) return new HashSet<int>();
        }
        // 血流弃三张后的逻辑手牌为 10 张；包含副露的标准川麻始终为 13 张。
        if (SichuanLobby.IsXueliu(q.SubRule) || q.Hand.Count + (q.Melds?.Count ?? 0) * 3 == 10) {
            HashSet<int> result = XueliuTips.Tingpai(q.Hand, q.Melds, q.SubRule == "sichuan/xueliu_exchange" ? 4 : 3, UsesExchangeScoring(q.SubRule, q.DetailedConfig));
            result.RemoveWhere(t => t / 10 == dingque);
            return result;
        }
        HashSet<int> waiting = SichuanExternal.TingpaiCheck(q.Hand, q.Melds ?? new List<string>());
        if (dingque >= 1 && dingque <= 3) {
            waiting.RemoveWhere(w => (w / 10) == dingque);
        }
        return waiting;
    }

    internal static bool UsesExchangeScoring(string subRule, Dictionary<string, object> config) {
        if (subRule != "sichuan/xueliu_exchange") return false;
        return config == null || !config.TryGetValue("xueliu_exchange_scoring", out object value)
            || !bool.TryParse(value?.ToString(), out bool enabled) || enabled;
    }

    public static WaitTileHint Describe(WaitHintQuery q) {
        if (SichuanLobby.IsXueliu(q.SubRule)) {
            int meldCount = q.SubRule == "sichuan/xueliu_exchange" ? 4 : 3;
            bool exchange = UsesExchangeScoring(q.SubRule, q.DetailedConfig);
            int ron = XueliuTips.Fan(q.HandWithWin, q.Melds, false, meldCount, exchange);
            if (ron > 0 && ron >= q.HepaiLimit) return WaitTileHint.Ron($"{ron}番");
            int zimo = XueliuTips.Fan(q.HandWithWin, q.Melds, true, meldCount, exchange);
            return zimo > 0 && zimo >= q.HepaiLimit
                ? WaitTileHint.TsumoOnly($"仅自摸 {zimo}番")
                : WaitTileHint.None("未起和");
        }
        var result = SichuanExternal.HepaiCheck(q.HandWithWin, q.Melds, new List<string>(), q.HepaiTile, q.ExcludedSuit, false);
        return result.Item2.Count > 0 && result.Item1 >= q.HepaiLimit
            ? WaitTileHint.Ron($"{result.Item1}番")
            : WaitTileHint.None("未起和");
    }
}

/// <summary>血流本地听牌提示，与服务端三面子加一对将的牌型及番表一致。</summary>
internal static class XueliuTips {
    public static HashSet<int> Tingpai(List<int> hand, List<string> melds, int meldCount = 3, bool modernExchange = false) {
        var waits = new HashSet<int>();
        var candidate = new List<int>(hand);
        for (int suit = 1; suit <= 3; suit++) {
            for (int rank = 1; rank <= 9; rank++) {
                int tile = suit * 10 + rank;
                candidate.Add(tile);
                if (Fan(candidate, melds, false, meldCount, modernExchange) > 0) waits.Add(tile);
                candidate.RemoveAt(candidate.Count - 1);
            }
        }
        return waits;
    }

    public static int Fan(List<int> hand, List<string> melds, bool zimo, int meldCount = 3, bool modernExchange = false) {
        melds = melds ?? new List<string>();
        if ((meldCount != 3 && meldCount != 4) || melds.Count > meldCount || hand.Count != (meldCount - melds.Count) * 3 + 2) return 0;
        var counts = new int[40];
        var physical = new int[40];
        var suits = new HashSet<int>();
        bool concealed = true;
        int kongFan = 0;
        foreach (int tile in hand) {
            if (!ValidTile(tile)) return 0;
            counts[tile]++; physical[tile]++; suits.Add(tile / 10);
        }
        foreach (string meld in melds) {
            if (string.IsNullOrEmpty(meld) || !int.TryParse(meld.Substring(1), out int tile) || !ValidTile(tile)) return 0;
            char sign = meld[0];
            if (sign != 'k' && sign != 'g' && sign != 'G') return 0;
            physical[tile] += sign == 'k' ? 3 : 4;
            suits.Add(tile / 10);
            concealed &= sign == 'G';
            kongFan += sign == 'G' ? 2 : sign == 'g' ? 1 : 0;
        }
        if (suits.Count > 2) return 0;
        foreach (int count in physical) if (count > 4) return 0;
        int roots = 0;
        bool noTerminals = true;
        bool sevenPairs = modernExchange && melds.Count == 0 && hand.Count == 14;
        for (int tile = 11; tile < 40; tile++) {
            if (physical[tile] == 4) roots++;
            if (physical[tile] > 0 && (tile % 10 == 1 || tile % 10 == 9)) noTerminals = false;
            if (counts[tile] % 2 != 0) sevenPairs = false;
        }
        int extra = (concealed ? 1 : 0) + (noTerminals ? 1 : 0);
        int best = sevenPairs ? ((suits.Count == 1 ? 12 : 6) + (noTerminals ? 1 : 0)) * (1 + roots) : 0;
        for (int pair = 11; pair < 40; pair++) {
            if (counts[pair] < 2) continue;
            counts[pair] -= 2;
            if (Groups(counts, meldCount - melds.Count, false)) {
                bool allPungs = Groups(counts, meldCount - melds.Count, true);
                if (modernExchange) {
                    int value = melds.Count == 4 ? (suits.Count == 1 ? 16 : 8)
                        : allPungs ? (suits.Count == 1 ? 10 : 4) : (suits.Count == 1 ? 6 : 2);
                    best = System.Math.Max(best, (value + extra) * (1 + roots));
                } else {
                    int value = allPungs ? (suits.Count == 1 ? 12 : 4) : (suits.Count == 1 ? 6 : 1);
                    best = System.Math.Max(best, value + kongFan + (concealed && zimo ? 1 : 0));
                }
            }
            counts[pair] += 2;
        }
        return best;
    }

    private static bool ValidTile(int tile) => tile >= 11 && tile <= 39 && tile % 10 >= 1 && tile % 10 <= 9;

    private static bool Groups(int[] counts, int remaining, bool pungsOnly) {
        int tile = 0;
        for (int i = 11; i < 40; i++) if (counts[i] > 0) { tile = i; break; }
        if (remaining == 0) return tile == 0;
        if (tile == 0) return false;
        if (counts[tile] >= 3) {
            counts[tile] -= 3;
            bool matched = Groups(counts, remaining - 1, pungsOnly);
            counts[tile] += 3;
            if (matched) return true;
        }
        if (!pungsOnly && tile % 10 <= 7 && counts[tile+1] > 0 && counts[tile+2] > 0) {
            counts[tile]--; counts[tile+1]--; counts[tile+2]--;
            bool matched = Groups(counts, remaining - 1, false);
            counts[tile]++; counts[tile+1]++; counts[tile+2]++;
            if (matched) return true;
        }
        return false;
    }
}
