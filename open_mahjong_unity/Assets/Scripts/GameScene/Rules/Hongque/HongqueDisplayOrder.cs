using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 虹雀显示顺序。牌 ID / 协议 / 计分的规范顺序不变；色环必须在整组范围内决定方向和切口，
/// 不能用“两个颜色谁在前”的循环比较器（它不满足排序的传递性）。
/// </summary>
public static class HongqueDisplayOrder {
    private static int Colour(int id) => (id - HongqueTileVisual.BaseId) / 10;
    private static int Number(int id) => (id - HongqueTileVisual.BaseId) % 10;
    private static int Mod(int value) => (value % 14 + 14) % 14;

    private static bool Earlier(int[] candidate, int[] best) {
        if (best == null) return true;
        for (int i = 0; i < candidate.Length; i++) {
            if (candidate[i] != best[i]) return candidate[i] < best[i];
        }
        return false;
    }

    /// <summary>合法组沿连续花色/等差数字排列；正反方向取字典序较小者，完整色环也有固定起点。</summary>
    public static int[] Meld(IEnumerable<int> tiles) {
        int[] source = tiles?.ToArray() ?? Array.Empty<int>();
        if (source.Any(id => !HongqueTileVisual.IsHongqueId(id))) return source;
        HongqueMeldShape shape = HongqueScoring.ClassifyMeld(source.ToList());
        if (shape == null) return source;
        int[] best = null;
        if (shape.BaseKind == "sequence") {
            int[] ascending = source.OrderBy(Number).ToArray();
            int[] descending = ascending.Reverse().ToArray();
            return Earlier(ascending, descending) ? ascending : descending;
        }
        var present = new HashSet<int>(source);
        foreach (int start in source) {
            foreach (int direction in new[] { 1, -1 }) {
                int[] candidate = Enumerable.Range(0, source.Length)
                    .Select(i => HongqueTileVisual.BaseId + Mod(Colour(start) + direction * shape.ColourStep * i) * 10 + Number(start))
                    .ToArray();
                if (candidate.All(present.Contains) && Earlier(candidate, best)) best = candidate;
            }
        }
        return best ?? source;
    }

    /// <summary>
    /// 任意手牌在最大空缺处切开色环；原切口同样好时保留原顺序。
    /// 若整手恰好是一组，直接使用组内顺序。同色数字仍升序，不推断或拆分玩家的牌组。
    /// </summary>
    public static int[] Hand(IEnumerable<int> tiles) {
        int[] source = tiles?.ToArray() ?? Array.Empty<int>();
        if (source.Length == 0 || source.Any(id => !HongqueTileVisual.IsHongqueId(id))) return source;
        if (HongqueScoring.ClassifyMeld(source.ToList()) != null) return Meld(source);
        int[] colours = source.Select(Colour).Distinct().OrderBy(c => c).ToArray();
        int[] natural = source.OrderBy(id => id).ToArray();
        if (colours.Length < 2) return natural;
        int maxGap = 0;
        for (int i = 0; i < colours.Length; i++)
            maxGap = Math.Max(maxGap, Mod(colours[(i + 1) % colours.Length] - colours[i]));
        if (14 + colours[0] - colours[colours.Length - 1] == maxGap) return natural;
        int[] best = null;
        for (int i = 0; i < colours.Length; i++) {
            int left = colours[i], right = colours[(i + 1) % colours.Length];
            if (Mod(right - left) != maxGap) continue;
            int[] forward = source.OrderBy(id => Mod(Colour(id) - right)).ThenBy(Number).ToArray();
            int[] backward = source.OrderBy(id => Mod(left - Colour(id))).ThenBy(Number).ToArray();
            if (Earlier(forward, best)) best = forward;
            if (Earlier(backward, best)) best = backward;
        }
        return best ?? natural;
    }

    public static bool IsMeldMask(int[] mask) {
        if (mask == null || mask.Length < 6 || mask.Length % 2 != 0) return false;
        for (int i = 0; i < mask.Length; i += 2) {
            // 暗牌、空槽、备用叠牌和未知标记不推断可见顺序。
            if ((mask[i] != 0 && mask[i] != 1 && mask[i] != 3)
                || !HongqueTileVisual.IsHongqueId(mask[i + 1])) return false;
        }
        return true;
    }

    /// <summary>连同来源/追加标记一起移动。只在最终竖排展示时消除横置/叠放标记。</summary>
    public static int[] MeldMask(int[] mask, bool vertical = false) {
        if (!IsMeldMask(mask)) return mask;
        int[] source = Enumerable.Range(0, mask.Length / 2).Select(i => mask[i * 2 + 1]).ToArray();
        if (source.Distinct().Count() != source.Length) return mask;
        int[] ordered = Meld(source);
        int[] result = new int[mask.Length];
        for (int i = 0; i < ordered.Length; i++) {
            int original = Array.IndexOf(source, ordered[i]);
            result[i * 2] = vertical ? 0 : mask[original * 2];
            result[i * 2 + 1] = ordered[i];
        }
        return result;
    }

    /// <summary>当前虹雀牌谱显式指定目标组；扩展后全组重新排序，追加张不使用普通加杠叠牌标记。</summary>
    public static int[] ExtendRecordedMeld(List<int[]> masks, List<string> combinations, int index, int addedTile) {
        if (index < 0 || index >= masks.Count || index >= combinations.Count || !IsMeldMask(masks[index]))
            throw new InvalidOperationException("虹雀杠牌目标组无效: " + index);
        int[] mask = masks[index];
        var tiles = Enumerable.Range(0, mask.Length / 2).Select(i => mask[i * 2 + 1]).ToList();
        tiles.Add(addedTile);
        var shape = HongqueScoring.ClassifyMeld(tiles);
        if (!HongqueTileVisual.IsHongqueId(addedTile) || shape == null)
            throw new InvalidOperationException("虹雀杠牌扩展后不是合法牌组。");
        int[] ordered = Meld(tiles);
        int[] result = ordered.SelectMany(id => new[] { 0, id }).ToArray();
        masks[index] = result;
        combinations[index] = (shape.Kind == "rainbow" ? "h" : shape.BaseKind == "triplet" ? "k" : "s") + ordered[0];
        return result;
    }
}
