#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>Random preview melds must respect tile supply and the source of a chow.</summary>
public static class RandomTableValidation
{
    public static object Run()
    {
        var build = typeof(Game3DManager).GetMethod("BuildRandomMeldMask", BindingFlags.NonPublic | BindingFlags.Static);
        var assignFlowers = typeof(Game3DManager).GetMethod("AssignRandomFlowers", BindingFlags.NonPublic | BindingFlags.Static);
        int chows = 0, pungs = 0, kongs = 0;
        for (int seed = 0; seed < 1000; seed++)
        {
            var rng = new Random(seed);
            var counts = new Dictionary<int, int>();
            foreach (int suit in new[] {10, 20, 30, 40})
                for (int rank = 1; rank <= (suit == 40 ? 7 : 9); rank++) counts[suit + rank] = 4;
            var used = counts.Keys.ToDictionary(tile => tile, tile => 0);
            for (int group = 0; group < 12; group++)
            {
                var mask = (List<int>)build.Invoke(null, new object[] {counts, rng});
                if (mask == null) break;
                var tiles = mask.Where((value, index) => index % 2 == 1).ToArray();
                var flags = mask.Where((value, index) => index % 2 == 0).ToArray();
                bool chow = tiles.Distinct().Count() > 1;
                if (chow)
                {
                    var sorted = tiles.OrderBy(tile => tile).ToArray();
                    Check(sorted.Length == 3 && sorted[0] / 10 < 4 && sorted[0] / 10 == sorted[2] / 10
                        && sorted[1] == sorted[0] + 1 && sorted[2] == sorted[0] + 2, "valid suited sequence");
                    Check(flags.SequenceEqual(new[] {1, 0, 0}), "chow claimed tile belongs at the upper-seat end");
                    chows++;
                }
                else
                {
                    Check(tiles.Length == 3 || tiles.Length == 4, "pung or kong size");
                    if (tiles.Length == 3) pungs++; else kongs++;
                    Check(flags.Count(flag => flag == 1) == 1 || (tiles.Length == 4 && flags.All(flag => flag == 2)), "open or concealed meld");
                }
                foreach (int tile in tiles) used[tile]++;
                Check(counts.All(pair => pair.Value >= 0 && pair.Value + used[pair.Key] == 4), "tile conservation");
            }
            var flowers = (Dictionary<string, List<int>>)assignFlowers.Invoke(null, new object[] {rng});
            Check(flowers.Count == 4 && flowers.Values.SelectMany(tiles => tiles).OrderBy(tile => tile)
                .SequenceEqual(Enumerable.Range(51, 8)), "eight unique flowers across four seats");
        }
        Check(chows > 0 && pungs > 0 && kongs > 0, "all meld categories exercised");
        return new {passed = true, seeds = 1000, chows, pungs, kongs};
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Random table: " + label);
    }
}
#endif
