using System.Collections.Generic;
using System.Linq;

namespace Jiandan {
    /// <summary>
    /// Jiandan waiting-tile calculator.
    ///
    /// The shape rules mirror the authoritative Python calculator: standard
    /// four-meld-and-pair hands, seven pairs, and thirteen orphans. Declared
    /// meld codes use the same s/k/g/q encoding as the server.
    /// </summary>
    public sealed class JiandanTingpaiCheck {
        private static readonly int[] ValidTiles = Enumerable.Range(11, 9)
            .Concat(Enumerable.Range(21, 9))
            .Concat(Enumerable.Range(31, 9))
            .Concat(Enumerable.Range(41, 7))
            .ToArray();

        private static readonly HashSet<int> Orphans = new HashSet<int> {
            11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47
        };

        public HashSet<int> TingpaiCheck(
            List<int> handTileList,
            List<string> combinationList) {
            var concealed = new List<int>(handTileList ?? new List<int>());
            var combinations = combinationList ?? new List<string>();
            var physicalCounts = BuildPhysicalCounts(concealed, combinations);
            var waits = new HashSet<int>();

            foreach (int tile in ValidTiles) {
                if (physicalCounts.TryGetValue(tile, out int count) && count >= 4) {
                    continue;
                }

                var candidate = new List<int>(concealed) { tile };
                if (IsWinningShape(candidate, combinations)) {
                    waits.Add(tile);
                }
            }
            return waits;
        }

        private static bool IsWinningShape(List<int> concealed, List<string> combinations) {
            int declaredSetCount = combinations.Count(IsSetCode);
            if (declaredSetCount == 0
                && (IsSevenPairs(concealed) || IsThirteenOrphans(concealed))) {
                return true;
            }

            int neededMelds = 4 - declaredSetCount;
            if (neededMelds < 0 || concealed.Count != neededMelds * 3 + 2) {
                return false;
            }

            var counts = BuildCounts(concealed);
            foreach (int pairTile in ValidTiles) {
                int count = counts[pairTile];
                if (count < 2 || count == 4) {
                    continue;
                }

                counts[pairTile] -= 2;
                bool complete = CanFormMelds(counts, neededMelds);
                counts[pairTile] += 2;
                if (complete) {
                    return true;
                }
            }
            return false;
        }

        private static bool CanFormMelds(Dictionary<int, int> counts, int remaining) {
            if (remaining == 0) {
                return counts.Values.All(count => count == 0);
            }

            int first = ValidTiles.FirstOrDefault(tile => counts[tile] > 0);
            if (first == 0) {
                return false;
            }

            int firstCount = counts[first];
            if (firstCount >= 3 && firstCount != 4) {
                counts[first] -= 3;
                if (CanFormMelds(counts, remaining - 1)) {
                    counts[first] += 3;
                    return true;
                }
                counts[first] += 3;
            }

            if (IsSuited(first) && first % 10 <= 7
                && counts[first + 1] > 0
                && counts[first + 2] > 0) {
                counts[first]--;
                counts[first + 1]--;
                counts[first + 2]--;
                if (CanFormMelds(counts, remaining - 1)) {
                    counts[first]++;
                    counts[first + 1]++;
                    counts[first + 2]++;
                    return true;
                }
                counts[first]++;
                counts[first + 1]++;
                counts[first + 2]++;
            }
            return false;
        }

        private static bool IsSevenPairs(List<int> tiles) {
            if (tiles.Count != 14) {
                return false;
            }
            return BuildCounts(tiles).Values.Sum(count => count / 2) == 7;
        }

        private static bool IsThirteenOrphans(List<int> tiles) {
            if (tiles.Count != 14) {
                return false;
            }
            var counts = BuildCounts(tiles);
            return Orphans.All(tile => counts[tile] >= 1)
                && Orphans.Count(tile => counts[tile] == 2) == 1
                && counts.Where(entry => !Orphans.Contains(entry.Key))
                    .All(entry => entry.Value == 0);
        }

        private static Dictionary<int, int> BuildPhysicalCounts(
            List<int> concealed,
            List<string> combinations) {
            var counts = BuildCounts(concealed);
            foreach (string code in combinations) {
                foreach (int tile in ExpandMeld(code)) {
                    if (counts.ContainsKey(tile)) {
                        counts[tile]++;
                    }
                }
            }
            return counts;
        }

        private static Dictionary<int, int> BuildCounts(IEnumerable<int> tiles) {
            var counts = ValidTiles.ToDictionary(tile => tile, _ => 0);
            foreach (int tile in tiles) {
                if (counts.ContainsKey(tile)) {
                    counts[tile]++;
                }
            }
            return counts;
        }

        private static bool IsSetCode(string code) {
            if (string.IsNullOrEmpty(code)) {
                return false;
            }
            char marker = char.ToLowerInvariant(code[0]);
            return marker == 's' || marker == 'k' || marker == 'g';
        }

        private static IEnumerable<int> ExpandMeld(string code) {
            if (string.IsNullOrEmpty(code) || code.Length < 2
                || !int.TryParse(code.Substring(1), out int tile)) {
                yield break;
            }

            switch (char.ToLowerInvariant(code[0])) {
                case 's':
                    yield return tile;
                    yield return tile + 1;
                    yield return tile + 2;
                    break;
                case 'k':
                    yield return tile;
                    yield return tile;
                    yield return tile;
                    break;
                case 'g':
                    yield return tile;
                    yield return tile;
                    yield return tile;
                    yield return tile;
                    break;
                case 'q':
                    yield return tile;
                    yield return tile;
                    break;
            }
        }

        private static bool IsSuited(int tile) {
            return tile >= 11 && tile <= 39 && tile % 10 != 0;
        }
    }
}
