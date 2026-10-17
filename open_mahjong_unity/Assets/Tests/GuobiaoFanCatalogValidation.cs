#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>以正式计番器交叉检查教学牌例，防止内容改动引入无效牌型或错误番值。</summary>
public static class GuobiaoFanCatalogValidation {
    public static string Run() {
        var errors = new List<string>();
        var entries = GuobiaoFanCatalog.Entries;
        Require(entries.Count == 82, "82 个番种", errors);
        Require(entries.Select(x => x.number).Distinct().Count() == 82, "番种编号不重复", errors);
        Require(entries.Select(x => x.name).Distinct().Count() == 82, "番种名称不重复", errors);
        foreach (var entry in entries) {
            string name = entry.name.Replace("单吊将", "单钓将");
            string value;
            bool known = GuobiaoFanText.FanToDisplayGuobiao.TryGetValue(name, out value)
                || GuobiaoFanText.FanToDisplayGuobiao.TryGetValue(name + "*1", out value);
            Require(known && value == entry.fan + "番", entry.name + " 番值与计番器一致", errors);
            var groups = entry.ExampleGroups();
            int[] tiles = groups.SelectMany(x => x).ToArray();
            Require(tiles.GroupBy(x => x).All(x => x.Count() <= (x.Key >= 51 ? 1 : 4)), entry.name + " 不存在第五张牌", errors);
            foreach (int tile in tiles)
                Require(UnityEngine.Resources.Load<UnityEngine.Texture2D>("image/Cards/Faces/official/table/" + tile), entry.name + " 牌面 " + tile, errors);
            if (entry.number == 81) {
                Require(tiles.Length == 8 && tiles.All(x => x >= 51 && x <= 58), "花牌八张", errors);
                continue;
            }
            try {
                var hand = new List<int>();
                var melds = new List<string>();
                for (int i = 0; i < groups.Length; i++) {
                    int[] group = groups[i];
                    bool kong = group.Length == 4 && group.All(x => x == group[0]);
                    bool open = IsOpenGroup(entry.number, i);
                    if (kong) melds.Add((open ? "g" : "G") + group[0]);
                    else if (open) melds.Add((group.All(x => x == group[0]) ? "k" + group[0] : "s" + group[1]));
                    else hand.AddRange(group);
                }
                Require(hand.Count + melds.Count * 3 == 14, entry.name + " 和牌有效张数", errors);
                int win = entry.number == 43 ? 38 : tiles[tiles.Length - 1];
                var ways = new List<string>();
                if (!new[] { 43, 45, 47, 52, 62 }.Contains(entry.number)) ways.Add("自摸");
                else ways.Add("点和");
                if (entry.number >= 77 && entry.number <= 79) ways.Add("和单张");
                if (entry.number == 44) ways.Add("妙手回春");
                if (entry.number == 45) ways.Add("海底捞月");
                if (entry.number == 46) ways.Add("杠上开花");
                if (entry.number == 47) ways.Add("抢杠和");
                if (entry.number == 58) ways.Add("和绝张");
                if (entry.number == 60) ways.Add("场风东");
                if (entry.number == 61) ways.Add("自风南");
                var result = GBhepai.HepaiCheck(hand, melds, ways, win);
                Require(result.Item2.Any(x => x.Split('*')[0] == name),
                    entry.name + " 牌例应成立；实际：" + string.Join("、", result.Item2), errors);
            } catch (Exception error) { errors.Add(entry.name + ": " + error.Message); }
        }
        // 抢杠和与和绝张的和牌张已有三张在他处，暗手内不能再含同种牌。
        foreach (var entry in entries.Where(x => x.number == 47 || x.number == 58)) {
            var tiles = entry.ExampleGroups().SelectMany(x => x).ToArray();
            Require(tiles.Count(x => x == tiles.Last()) == 1, entry.name + " 公开三张的数量约束", errors);
        }
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        return "82 个番值、81 副和牌牌例、8 张花牌及牌面资源检查通过。";
    }

    private static bool IsOpenGroup(int number, int index) {
        if (number == 52) return index < 4;
        if (number == 57) return index < 2;
        if (number == 33) return index == 3;
        return index == 0 && new[] { 14, 23, 43, 45, 46, 47, 48, 74, 80, 82 }.Contains(number);
    }
    private static void Require(bool condition, string message, List<string> errors) { if (!condition) errors.Add(message); }
}
#endif
