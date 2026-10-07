using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>贵州普通和牌的基本分提示；鸡杠另计，过水与临时和牌条件由服务端裁定。</summary>
public static class GuizhouTips {
    public sealed class BasicScore {
        public int Points { get; internal set; }
        public bool CanRon { get; internal set; }
    }

    public static BasicScore Evaluate(IList<int> hand, IList<string> melds, string ready = null) {
        melds = melds ?? Array.Empty<string>();
        if (hand == null || melds.Count > 4 || hand.Count != 14 - 3 * melds.Count) return null;
        var hidden = new int[40];
        var physical = new int[40];
        foreach (int tile in hand) {
            if (!Valid(tile) || ++physical[tile] > 4) return null;
            hidden[tile]++;
        }
        bool passport = false;
        foreach (string code in melds) {
            if (string.IsNullOrEmpty(code) || code.Length != 3 || !"kgG".Contains(code[0]) ||
                !int.TryParse(code.Substring(1), out int tile) || !Valid(tile)) return null;
            bool kong = code[0] != 'k';
            physical[tile] += kong ? 4 : 3;
            if (physical[tile] > 4) return null;
            passport |= kong;
        }
        bool standard = GuizhouShape.Standard(hidden, 4 - melds.Count);
        bool pairs = melds.Count == 0 && hidden.All(n => n % 2 == 0);
        if (!standard && !pairs) return null;

        int common = Enumerable.Range(11, 29).Where(t => physical[t] > 0).Select(t => t / 10).Distinct().Count() == 1 ? 13 : 0;
        common += ready == "hard_ready" ? 26 : ready == "soft_ready" ? 13 : 0;
        int pattern = 0;
        if (standard) {
            // 四副露单吊已经包含大对子，不能再叠加 8 分。
            if (melds.Count == 4) pattern = 13;
            else if (AllPungs(hidden)) pattern = 8;
        }
        if (pairs) pattern = Math.Max(pattern, hidden.Contains(4) ? 26 : 13);
        int points = common + pattern;
        bool plain = points == 0;
        return new BasicScore { Points = Math.Min(39, plain ? 3 : points), CanRon = !plain || passport };
    }

    public static WaitTileHint Describe(WaitHintQuery query) {
        string ready = query.Record != null ? query.Record.ReadyQualification : GuizhouGameState.Active?.SelfReadyQualification;
        BasicScore score = Evaluate(query.HandWithWin, query.Melds, ready);
        if (score == null) return WaitTileHint.None("未满足");
        return score.CanRon ? WaitTileHint.Ron($"{score.Points}分") : WaitTileHint.TsumoOnly($"仅自摸 {score.Points}分");
    }

    private static bool Valid(int tile) => tile >= 11 && tile <= 39 && tile % 10 >= 1 && tile % 10 <= 9;
    private static bool AllPungs(int[] hidden) {
        for (int tile = 11; tile <= 39; tile++) {
            if (hidden[tile] < 2) continue;
            hidden[tile] -= 2;
            bool complete = hidden.All(n => n % 3 == 0);
            hidden[tile] += 2;
            if (complete) return true;
        }
        return false;
    }
}
