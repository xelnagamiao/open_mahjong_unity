#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

/// <summary>同花计分回归；纯逻辑，可通过后台 Pipeline 运行，不切换场景或 Play Mode。</summary>
public static class HongqueColourLayoutValidation {
    public static string Run() {
        int checks = 0;
        void Check(bool condition, string label) {
            if (!condition) throw new InvalidOperationException("HongqueColourLayout: " + label);
            checks++;
        }
        List<int> Ids(string codes) => codes.Split(' ').Select(HongqueTileVisual.FromCode).ToList();
        List<int> Tiles(int[] colours, int[] numbers) => colours.Select((c, i) => 1000 + c * 10 + numbers[i]).ToList();
        int[] Mask(List<int> tiles) => tiles.SelectMany(tile => new[] { 0, tile }).ToArray();
        for (int colour = 0; colour < 14; colour++) foreach (bool exposed in new[] { false, true })
            foreach (bool selfDraw in new[] { false, true }) {
                var tiles = Enumerable.Range(1, 3).Select(n => 1000 + colour * 10 + n).ToList();
                var result = HongqueScoring.BestWinResult(exposed ? new List<int>() : tiles,
                    exposed ? new List<int[]> { Mask(tiles) } : new List<int[]>(), selfDraw, false, false);
                int expectedFan = (colour % 2 == 0 ? 37 : 31) - (exposed ? 1 : 0);
                int expectedBase = exposed ? 3 : 5;
                Check(result != null && result.FanTotal == expectedFan && result.Base == expectedBase
                    && result.Points == expectedFan * expectedBase, "pure/half colours are 2 fan: " + colour);
            }
        foreach (string fanName in new[] { "全纯色", "全半色" }) {
            var entry = MahjongFanCatalog.Get("hongque").entries.Single(e => e.name == fanName);
            Check(entry.fan == 2 && entry.category == "2" && entry.valueText == "2 番", "reading catalog agrees: " + fanName);
        }
        var method = typeof(HongqueScoring).GetMethod("SameColourLayoutFans", BindingFlags.NonPublic | BindingFlags.Static);
        void Flowers(List<List<int>> groups, string name, int value, int count = 1) {
            var shapes = groups.Select(HongqueScoring.ClassifyMeld).ToList();
            Check(shapes.All(shape => shape != null), "all supplied groups are legal");
            var fans = ((IEnumerable)method.Invoke(null, new object[] { shapes })).Cast<object>().ToList();
            if (name == null) { Check(fans.Count == 0, "different flower layouts must not match"); return; }
            Check(fans.Count == 1, "only one tier per group bucket");
            var type = fans[0].GetType();
            Check((string)type.GetField("Name").GetValue(fans[0]) == name, name);
            Check((int)type.GetField("Value").GetValue(fans[0]) == value, "fan value");
            Check((int)type.GetField("Count").GetValue(fans[0]) == count, "repeat count");
        }
        var hand = Ids("AY1 AY9 GY2 GY9 FY3 FY9");
        var melds = new List<int[]> { Mask(Ids("CY1 DX1 DY1")), Mask(Ids("DY8 EY8 FY8")) };
        string original = JsonConvert.SerializeObject(new { hand, melds });
        var score = HongqueScoring.BestWinResult(hand, melds, true, false, false);
        Check(score != null && score.Base == 3 && score.FanTotal == 2 && score.Points == 6, "reported hand scores 6 points");
        Check(JsonConvert.SerializeObject(new { hand, melds }) == original, "scoring must not reorder inputs");
        foreach (bool selfDraw in new[] { false, true }) {
            var hintHand = new List<int>(hand);
            if (!selfDraw) hintHand.Remove(HongqueTileVisual.FromCode("AY1"));
            var hint = HongqueScoring.BuildWinHint(hintHand, melds, new HongqueWinHintContext { tile = "AY1", self_draw = selfDraw });
            Check(hint != null && hint.fan_total == 2 && hint.points == 6, "ron/self-draw local score hint");
        }
        for (int start = 0; start < 14; start++) foreach (int step in new[] { 1, 2 })
            foreach (bool reverse in new[] { false, true }) for (int count = 2; count <= 4; count++) {
                int[] colours = Enumerable.Range(0, 3).Select(i => (start + step * i) % 14).ToArray();
                var groups = new List<List<int>> { Tiles(colours, new[] { 9, 9, 9 }) };
                foreach (var numbers in new[] { new[] { 1, 2, 3 }, new[] { 4, 5, 6 }, new[] { 3, 4, 5 } }.Take(count - 1))
                    groups.Add(Tiles(colours, reverse ? numbers.Reverse().ToArray() : numbers));
                Check(groups.SelectMany(group => group).Distinct().Count() == count * 3, "no duplicate physical tile");
                string name = new[] { "双同花", "三同花", "四同花" }[count - 2];
                int value = new[] { 2, 6, 12 }[count - 2];
                Flowers(groups, name, value);
                Flowers(groups.AsEnumerable().Reverse().Select(group => group.AsEnumerable().Reverse().ToList()).ToList(), name, value);
            }
        for (int start = 0; start < 14; start++) for (int shift = 1; shift < 7; shift++) {
            int[] colours = Enumerable.Range(0, 7).Select(i => (start + 2 * i) % 14).ToArray();
            int[] rotated = colours.Skip(shift).Concat(colours.Take(shift)).ToArray();
            var groups = new List<List<int>> { Tiles(colours, Enumerable.Range(1, 7).ToArray()), Tiles(rotated, Enumerable.Range(1, 7).ToArray()) };
            Check(groups.SelectMany(group => group).Distinct().Count() == 14, "distinct full-cycle tiles");
            Flowers(groups, "双同花", 2);
        }
        Flowers(new List<List<int>> {
            Tiles(Enumerable.Range(0, 8).Select(i => i * 2 % 14).ToArray(), Enumerable.Range(1, 8).ToArray()),
            Tiles(Enumerable.Range(0, 8).Select(i => (2 + i * 2) % 14).ToArray(), Enumerable.Range(1, 8).ToArray())
        }, null, 0);
        Flowers(new List<List<int>> { Ids("AX1 AX2 AX3"), Ids("AX4 AX5 AX6 AX7") }, null, 0);
        Flowers(new List<List<int>> { Ids("AX1 AY2 BX3"), Ids("BX4 BY5 CX6") }, null, 0);
        Flowers(new List<List<int>> { Ids("FY1 GY2 AY3"), Ids("AY9 FY9 GY9"), Ids("GX2 GY3 AX4"), Ids("AX8 GX8 GY8") }, "双同花", 2, 2);
        return JsonConvert.SerializeObject(new { checks, reportedPoints = score.Points, reportedFans = score.FanTotal });
    }
}
#endif
