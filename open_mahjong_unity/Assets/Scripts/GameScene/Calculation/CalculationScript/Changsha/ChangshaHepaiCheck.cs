using System;
using System.Collections.Generic;
using System.Linq;

namespace Changsha {
    public class ChangshaHepaiCheck {
        private static readonly int[] ValidTiles = Enumerable.Range(11, 9)
            .Concat(Enumerable.Range(21, 9))
            .Concat(Enumerable.Range(31, 9))
            .ToArray();

        private static readonly HashSet<int> ValidTileSet = new HashSet<int>(ValidTiles);
        private static readonly HashSet<int> JiangRanks = new HashSet<int> { 2, 5, 8 };
        private static readonly HashSet<string> BigHuNames = new HashSet<string> {
            "碰碰胡", "将将胡", "清一色", "全求人", "七小对", "豪华七小对", "双豪华七小对", "三豪华七小对",
            "天胡", "地胡", "海底", "杠上开花", "杠上炮", "抢杠胡",
        };
        private static readonly Dictionary<string, int> BigHuWeights = new Dictionary<string, int> {
            { "豪华七小对", 2 },
            { "双豪华七小对", 3 },
            { "三豪华七小对", 4 },
        };

        private static int Rank(int tile) {
            return tile % 10;
        }

        private static int Suit(int tile) {
            return tile / 10;
        }

        private static bool IsJiang(int tile) {
            return JiangRanks.Contains(Rank(tile));
        }

        private static Dictionary<int, int> CountTiles(IEnumerable<int> tiles) {
            var counts = new Dictionary<int, int>();
            foreach (int tile in tiles) {
                counts.TryGetValue(tile, out int count);
                counts[tile] = count + 1;
            }
            return counts;
        }

        private static List<int> ExpandMeld(string comb) {
            if (string.IsNullOrEmpty(comb) || comb.Length < 2) return new List<int>();
            char sign = comb[0];
            if (!int.TryParse(comb.Substring(1), out int tile)) return new List<int>();
            switch (sign) {
                case 'g':
                case 'G':
                    return new List<int> { tile, tile, tile, tile };
                case 'k':
                case 'K':
                    return new List<int> { tile, tile, tile };
                case 's':
                case 'S':
                    return new List<int> { tile - 1, tile, tile + 1 };
                case 'q':
                    return new List<int> { tile, tile };
                default:
                    return new List<int>();
            }
        }

        private static List<int> ExpandAll(List<int> handList, List<string> tilesCombination) {
            var tiles = new List<int>(handList ?? new List<int>());
            foreach (string comb in tilesCombination ?? new List<string>()) {
                tiles.AddRange(ExpandMeld(comb));
            }
            return tiles;
        }

        private static int? FirstRemaining(Dictionary<int, int> counts) {
            foreach (int tile in counts.Keys.OrderBy(t => t)) {
                if (counts[tile] > 0) return tile;
            }
            return null;
        }

        private static void Decrement(Dictionary<int, int> counts, int tile, int amount) {
            counts.TryGetValue(tile, out int current);
            int next = current - amount;
            if (next <= 0) counts.Remove(tile);
            else counts[tile] = next;
        }

        private static bool CanFormSets(Dictionary<int, int> counts, int setsNeeded, bool tripletsOnly = false) {
            if (setsNeeded == 0) return counts.Values.All(v => v == 0);

            int? maybeTile = FirstRemaining(counts);
            if (!maybeTile.HasValue) return false;
            int tile = maybeTile.Value;

            if (counts.TryGetValue(tile, out int count) && count >= 3) {
                var tripletCounts = new Dictionary<int, int>(counts);
                Decrement(tripletCounts, tile, 3);
                if (CanFormSets(tripletCounts, setsNeeded - 1, tripletsOnly)) return true;
            }

            if (tripletsOnly) return false;
            if (!ValidTileSet.Contains(tile) || Rank(tile) > 7) return false;

            int tile1 = tile + 1;
            int tile2 = tile + 2;
            if (Suit(tile1) != Suit(tile) || Suit(tile2) != Suit(tile)) return false;
            if (!counts.ContainsKey(tile1) || !counts.ContainsKey(tile2)) return false;

            var sequenceCounts = new Dictionary<int, int>(counts);
            Decrement(sequenceCounts, tile, 1);
            Decrement(sequenceCounts, tile1, 1);
            Decrement(sequenceCounts, tile2, 1);
            return CanFormSets(sequenceCounts, setsNeeded - 1, false);
        }

        private static bool IsStandardShape(List<int> handList, List<string> tilesCombination, bool jiangPair = false) {
            var combs = tilesCombination ?? new List<string>();
            int setsNeeded = 4 - combs.Count;
            if (setsNeeded < 0 || handList == null || handList.Count != setsNeeded * 3 + 2) return false;

            var counts = CountTiles(handList);
            foreach (var kv in counts.ToList()) {
                int tile = kv.Key;
                if (kv.Value < 2) continue;
                if (jiangPair && !IsJiang(tile)) continue;

                var remaining = new Dictionary<int, int>(counts);
                Decrement(remaining, tile, 2);
                if (CanFormSets(remaining, setsNeeded)) return true;
            }
            return false;
        }

        private static bool IsAllTriplets(List<int> handList, List<string> tilesCombination) {
            var combs = tilesCombination ?? new List<string>();
            if (combs.Any(c => !string.IsNullOrEmpty(c) && (c[0] == 's' || c[0] == 'S'))) return false;

            int setsNeeded = 4 - combs.Count;
            if (setsNeeded < 0 || handList == null || handList.Count != setsNeeded * 3 + 2) return false;

            var counts = CountTiles(handList);
            foreach (var kv in counts.ToList()) {
                int tile = kv.Key;
                if (kv.Value < 2) continue;
                var remaining = new Dictionary<int, int>(counts);
                Decrement(remaining, tile, 2);
                if (CanFormSets(remaining, setsNeeded, true)) return true;
            }
            return false;
        }

        private static string SevenPairsType(List<int> handList, List<string> tilesCombination) {
            if ((tilesCombination != null && tilesCombination.Count > 0) || handList == null || handList.Count != 14) {
                return "none";
            }

            var counts = CountTiles(handList).Values.ToList();
            if (counts.Count == 0 || counts.Any(c => c != 2 && c != 4)) return "none";
            if (counts.Sum(c => c / 2) != 7) return "none";
            int quadCount = counts.Count(c => c == 4);
            if (quadCount >= 3) return "triple_luxury";
            if (quadCount == 2) return "double_luxury";
            if (quadCount == 1) return "luxury";
            return "normal";
        }

        private static bool HasWinningShape(List<int> handList, List<string> tilesCombination) {
            return IsStandardShape(handList, tilesCombination, false)
                || SevenPairsType(handList, tilesCombination) != "none";
        }

        private static bool AllJiangTiles(List<int> allTiles) {
            return allTiles.Count > 0 && allTiles.All(IsJiang);
        }

        private static bool IsFlush(List<int> allTiles) {
            return allTiles.Select(Suit).Distinct().Count() == 1;
        }

        private static bool IsQuanqiuren(List<int> handList, List<string> tilesCombination) {
            return handList != null
                && tilesCombination != null
                && tilesCombination.Count == 4
                && IsStandardShape(handList, tilesCombination, false);
        }

        private static void AppendContextFans(List<string> names, bool hasShape, List<string> wayToHepai) {
            if (!hasShape || wayToHepai == null) return;
            var tokenToName = new List<Tuple<string[], string>> {
                Tuple.Create(new[] { "天胡", "天和" }, "天胡"),
                Tuple.Create(new[] { "地胡", "地和" }, "地胡"),
                Tuple.Create(new[] { "海底", "海底捞月", "海底漫游" }, "海底"),
                Tuple.Create(new[] { "杠上开花", "杠上花" }, "杠上开花"),
                Tuple.Create(new[] { "杠上炮" }, "杠上炮"),
                Tuple.Create(new[] { "抢杠胡", "抢杠和", "抢杠" }, "抢杠胡"),
            };

            foreach (var pair in tokenToName) {
                string fanName = pair.Item2;
                if (!names.Contains(fanName) && pair.Item1.Any(wayToHepai.Contains)) {
                    names.Add(fanName);
                }
            }
        }

        public static int BaseFromFans(
            List<string> fanList,
            bool dealerRelated = false,
            int smallHuScore = 2,
            int bigHuScore = 8,
            bool baseScoreNoDealer = false) {
            if (fanList == null || fanList.Count == 0) return 0;
            int bigCount = fanList
                .Where(BigHuNames.Contains)
                .Sum(name => BigHuWeights.TryGetValue(name, out int weight) ? weight : 1);
            if (bigCount > 0) {
                return (baseScoreNoDealer ? Math.Max(1, bigHuScore) : (dealerRelated ? 7 : 6)) * bigCount;
            }
            if (fanList.Contains("小胡")) {
                return baseScoreNoDealer ? Math.Max(1, smallHuScore) : (dealerRelated ? 2 : 1);
            }
            return 0;
        }

        public Tuple<int, List<string>> HepaiCheck(
            List<int> handList,
            List<string> tilesCombination,
            List<string> wayToHepai,
            int getTile,
            bool includeSituational = true) {
            var concealed = new List<int>(handList ?? new List<int>());
            var melds = new List<string>(tilesCombination ?? new List<string>());
            var ways = new List<string>(wayToHepai ?? new List<string>());
            var allTiles = ExpandAll(concealed, melds);

            if (allTiles.Any(tile => !ValidTileSet.Contains(tile))) {
                return Tuple.Create(0, new List<string>());
            }

            bool smallHu = IsStandardShape(concealed, melds, true);
            bool hasShape = HasWinningShape(concealed, melds);
            var bigNames = new List<string>();

            if (hasShape && IsAllTriplets(concealed, melds)) bigNames.Add("碰碰胡");
            if (hasShape && AllJiangTiles(allTiles)) bigNames.Add("将将胡");
            if (hasShape && IsFlush(allTiles)) bigNames.Add("清一色");
            if (hasShape && IsQuanqiuren(concealed, melds)) bigNames.Add("全求人");

            string sevenPairs = SevenPairsType(concealed, melds);
            if (sevenPairs == "normal") bigNames.Add("七小对");
            else if (sevenPairs == "luxury") bigNames.Add("豪华七小对");
            else if (sevenPairs == "double_luxury") bigNames.Add("双豪华七小对");
            else if (sevenPairs == "triple_luxury") bigNames.Add("三豪华七小对");

            if (includeSituational) {
                AppendContextFans(bigNames, hasShape, ways);
            }

            if (bigNames.Count > 0) return Tuple.Create(BaseFromFans(bigNames), bigNames);
            if (smallHu) return Tuple.Create(BaseFromFans(new List<string> { "小胡" }), new List<string> { "小胡" });
            return Tuple.Create(0, new List<string>());
        }
    }
}
