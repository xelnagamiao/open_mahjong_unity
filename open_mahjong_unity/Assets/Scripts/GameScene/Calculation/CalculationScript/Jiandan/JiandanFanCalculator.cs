using System;
using System.Collections.Generic;
using System.Linq;

namespace Jiandan {
    /// <summary>
    /// Client-side static Jiandan fan calculator used by tingpai tips.
    /// It mirrors server/game_calculation/jiandan and intentionally receives
    /// no haitei, houtei, rinshan, chankan, heavenly, or earthly-win context.
    /// The winning tile is therefore evaluated as an ordinary discard win.
    /// </summary>
    public sealed class JiandanFanCalculator {
        private static readonly int[] ValidTiles = Enumerable.Range(11, 9)
            .Concat(Enumerable.Range(21, 9))
            .Concat(Enumerable.Range(31, 9))
            .Concat(Enumerable.Range(41, 7))
            .ToArray();

        private static readonly HashSet<int> Orphans = new HashSet<int> {
            11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47
        };
        private static readonly HashSet<int> Terminals = new HashSet<int> {
            11, 19, 21, 29, 31, 39
        };

        private enum MeldKind { Sequence, Triplet, Kong, Pair }

        private sealed class Meld {
            public MeldKind Kind;
            public List<int> Tiles;
            public bool Concealed;
            public bool Declared;

            public int Head => Tiles[0];
            public bool IsSet => Kind == MeldKind.Sequence || Kind == MeldKind.Triplet || Kind == MeldKind.Kong;
            public bool IsTripletLike => Kind == MeldKind.Triplet || Kind == MeldKind.Kong;
        }

        private sealed class Decomposition {
            public List<Meld> Melds;
            public Meld Pair;
            public IEnumerable<Meld> AllUnits => Melds.Concat(new[] { Pair });
        }

        private sealed class FanHit {
            public string Name;
            public int Value;
            public string Row;
            public bool Repeatable;

            public FanHit(string name, int value, string row, bool repeatable = false) {
                Name = name;
                Value = value;
                Row = row;
                Repeatable = repeatable;
            }
        }

        /// <summary>
        /// handList contains the hypothetical winning tile. combinationList
        /// uses the common s/k/g/q meld encoding. Returns capped fan points and
        /// the kept fan names after same-row exclusion.
        /// </summary>
        public Tuple<int, List<string>> HepaiCheck(
            List<int> handList,
            List<string> combinationList,
            int winningTile) {
            var concealed = new List<int>(handList ?? new List<int>());
            concealed.Sort();
            var declared = ParseMelds(combinationList ?? new List<string>());
            var finalTiles = new List<int>(concealed);
            foreach (Meld meld in declared) finalTiles.AddRange(meld.Tiles);
            finalTiles.Sort();

            var preWinTiles = new List<int>(concealed);
            preWinTiles.Remove(winningTile);
            var candidates = new List<List<FanHit>>();

            List<FanHit> special = DetectFans(
                finalTiles, concealed, null, declared, winningTile, preWinTiles, includeSpecialShapes: true);
            if (special.Any(hit => hit.Name == "七对子" || hit.Name == "十三幺九" || hit.Name == "九莲宝灯")) {
                candidates.Add(special);
            }

            foreach (Decomposition decomposition in FindStandardDecompositions(concealed, declared)) {
                candidates.Add(DetectFans(
                    finalTiles,
                    concealed,
                    decomposition,
                    decomposition.Melds,
                    winningTile,
                    preWinTiles,
                    includeSpecialShapes: false));
            }

            if (candidates.Count == 0) {
                return Tuple.Create(0, new List<string>());
            }

            List<FanHit> kept = candidates
                .Select(ApplyRowRules)
                .OrderByDescending(hits => hits.Sum(hit => hit.Value))
                .First();
            int rawPoints = kept.Sum(hit => hit.Value);
            List<string> names = kept
                .OrderByDescending(hit => hit.Value)
                .ThenBy(hit => hit.Row)
                .ThenBy(hit => hit.Name)
                .Select(hit => hit.Name)
                .ToList();
            return Tuple.Create(Math.Min(rawPoints, 20), names);
        }

        private static List<FanHit> DetectFans(
            List<int> finalTiles,
            List<int> concealedTiles,
            Decomposition decomposition,
            List<Meld> melds,
            int winningTile,
            List<int> preWinTiles,
            bool includeSpecialShapes) {
            var hits = new List<FanHit>();

            if (includeSpecialShapes) {
                if (IsSevenPairs(finalTiles)) hits.Add(Hit("七对子", 3, "shape"));
                if (IsThirteenOrphans(finalTiles)) hits.Add(Hit("十三幺九", 20, "terminals_honors"));
                if (IsNineGates(preWinTiles, winningTile, melds)) hits.Add(Hit("九莲宝灯", 20, "color"));
            }

            DetectComposition(finalTiles, hits);
            if (IsClosed(melds)) hits.Add(Hit("门前清", 1, "closed_opening"));

            if (decomposition != null) {
                DetectStandardShape(decomposition, hits);
                DetectWinds(decomposition, hits);
                DetectDragons(decomposition, hits);
                DetectTerminalHonorStandard(decomposition, finalTiles, hits);
                DetectThreeSuitNumber(decomposition, hits);
                DetectConcealedTriplets(decomposition, winningTile, hits);
                DetectConsecutiveTriplets(decomposition, hits);
                DetectIdenticalSequences(decomposition, hits);
            }

            DetectKongCount(melds, hits);
            return hits;
        }

        private static FanHit Hit(string name, int value, string row, bool repeatable = false) {
            return new FanHit(name, value, row, repeatable);
        }

        private static List<FanHit> ApplyRowRules(List<FanHit> hits) {
            var kept = new List<FanHit>();
            foreach (IGrouping<string, FanHit> group in hits.GroupBy(hit => hit.Row)) {
                if (group.Key == "three_suit_number") {
                    kept.AddRange(group);
                    continue;
                }
                List<FanHit> nonRepeatable = group.Where(hit => !hit.Repeatable).ToList();
                if (nonRepeatable.Count > 0) {
                    kept.Add(nonRepeatable
                        .OrderByDescending(hit => hit.Value)
                        .ThenByDescending(hit => hit.Name)
                        .First());
                } else {
                    kept.AddRange(group);
                }
            }
            return kept;
        }

        private static void DetectComposition(List<int> tiles, List<FanHit> hits) {
            bool hasTerminal = tiles.Any(IsTerminal);
            bool hasHonor = tiles.Any(IsHonor);
            if (tiles.All(IsSimple)) hits.Add(Hit("断幺九", 1, "terminals_honors"));

            bool allTerminalOrHonor = tiles.All(tile => IsTerminal(tile) || IsHonor(tile));
            if (allTerminalOrHonor && hasTerminal && hasHonor && !IsThirteenOrphans(tiles)) {
                hits.Add(Hit("混幺九", 8, "terminals_honors"));
            }
            if (tiles.All(tile => Terminals.Contains(tile))) {
                hits.Add(Hit("清幺九", 20, "terminals_honors"));
            }

            var suitedSuits = new HashSet<int>(tiles.Where(IsSuited).Select(Suit));
            if (suitedSuits.Count == 1 && hasHonor) hits.Add(Hit("混一色", 3, "color"));
            if (suitedSuits.Count == 1 && !hasHonor) hits.Add(Hit("清一色", 8, "color"));
            if (tiles.Count > 0 && tiles.All(IsHonor)) hits.Add(Hit("字一色", 20, "color"));
        }

        private static void DetectStandardShape(Decomposition decomposition, List<FanHit> hits) {
            if (decomposition.Melds.All(meld => meld.Kind == MeldKind.Sequence)) {
                hits.Add(Hit("平和", 1, "shape"));
            }
            if (decomposition.Melds.All(meld => meld.IsTripletLike)) {
                hits.Add(Hit("对对和", 3, "shape"));
            }
        }

        private static void DetectWinds(Decomposition decomposition, List<FanHit> hits) {
            var windSets = new HashSet<int>(decomposition.Melds
                .Where(meld => meld.IsTripletLike && IsWind(meld.Head))
                .Select(meld => meld.Head));
            int? windPair = IsWind(decomposition.Pair.Head) ? decomposition.Pair.Head : (int?)null;
            if (windSets.Count == 4) hits.Add(Hit("大四喜", 20, "winds"));
            else if (windSets.Count == 3 && windPair.HasValue && !windSets.Contains(windPair.Value)) hits.Add(Hit("小四喜", 20, "winds"));
            else if (windSets.Count == 3) hits.Add(Hit("大三风", 8, "winds"));
            else if (windSets.Count == 2 && windPair.HasValue && !windSets.Contains(windPair.Value)) hits.Add(Hit("小三风", 3, "winds"));
            else if (windSets.Count == 2) hits.Add(Hit("二风刻", 1, "winds"));
        }

        private static void DetectDragons(Decomposition decomposition, List<FanHit> hits) {
            var dragonSets = new HashSet<int>(decomposition.Melds
                .Where(meld => meld.IsTripletLike && IsDragon(meld.Head))
                .Select(meld => meld.Head));
            int? dragonPair = IsDragon(decomposition.Pair.Head) ? decomposition.Pair.Head : (int?)null;
            if (dragonSets.Count == 3) hits.Add(Hit("大三元", 20, "dragons"));
            else if (dragonSets.Count == 2 && dragonPair.HasValue && !dragonSets.Contains(dragonPair.Value)) hits.Add(Hit("小三元", 8, "dragons"));
            else if (dragonSets.Count == 2) hits.Add(Hit("二元刻", 3, "dragons"));
            else if (dragonSets.Count == 1) {
                int tile = dragonSets.First();
                hits.Add(Hit(tile == 45 ? "中" : tile == 46 ? "白" : "发", 1, "dragons"));
            }
        }

        private static void DetectTerminalHonorStandard(
            Decomposition decomposition,
            List<int> finalTiles,
            List<FanHit> hits) {
            for (int suit = 1; suit <= 3; suit++) {
                var starts = new HashSet<int>(decomposition.Melds
                    .Where(meld => meld.Kind == MeldKind.Sequence && Suit(meld.Head) == suit)
                    .Select(meld => Number(meld.Head)));
                if (starts.Contains(1) && starts.Contains(4) && starts.Contains(7)) {
                    hits.Add(Hit("一气通贯", 3, "terminals_honors"));
                }
            }

            bool allUnitsOutside = decomposition.AllUnits.All(unit => unit.Tiles.Any(tile => IsTerminal(tile) || IsHonor(tile)));
            if (!allUnitsOutside) return;
            bool hasSequence = decomposition.Melds.Any(meld => meld.Kind == MeldKind.Sequence);
            bool hasHonor = finalTiles.Any(IsHonor);
            if (hasSequence && hasHonor) hits.Add(Hit("混全带幺", 3, "terminals_honors"));
            if (hasSequence && !hasHonor) hits.Add(Hit("纯全带幺", 8, "terminals_honors"));
        }

        private static void DetectThreeSuitNumber(Decomposition decomposition, List<FanHit> hits) {
            var tripletSuits = new Dictionary<int, HashSet<int>>();
            var sequenceSuits = new Dictionary<int, HashSet<int>>();
            foreach (Meld meld in decomposition.Melds.Where(meld => IsSuited(meld.Head))) {
                Dictionary<int, HashSet<int>> target = meld.IsTripletLike ? tripletSuits
                    : meld.Kind == MeldKind.Sequence ? sequenceSuits : null;
                if (target == null) continue;
                int number = Number(meld.Head);
                if (!target.ContainsKey(number)) target[number] = new HashSet<int>();
                target[number].Add(Suit(meld.Head));
            }

            foreach (KeyValuePair<int, HashSet<int>> entry in tripletSuits) {
                if (entry.Value.Count == 3) {
                    hits.Add(Hit("三色同刻", 8, "three_suit_number"));
                } else if (entry.Value.Count == 2) {
                    int missingSuit = new[] { 1, 2, 3 }.First(suit => !entry.Value.Contains(suit));
                    bool missingIsPair = IsSuited(decomposition.Pair.Head)
                        && Number(decomposition.Pair.Head) == entry.Key
                        && Suit(decomposition.Pair.Head) == missingSuit;
                    hits.Add(missingIsPair
                        ? Hit("小三色同刻", 3, "three_suit_number")
                        : Hit("二色同刻", 1, "three_suit_number", true));
                }
            }

            foreach (HashSet<int> suits in sequenceSuits.Values) {
                if (suits.Count == 3) hits.Add(Hit("三色同顺", 3, "three_suit_number"));
            }
        }

        private static void DetectConcealedTriplets(
            Decomposition decomposition,
            int winningTile,
            List<FanHit> hits) {
            int count = 0;
            foreach (Meld meld in decomposition.Melds) {
                if (!meld.IsTripletLike || !meld.Concealed) continue;
                // Tips always use the ordinary discard-win interpretation.
                if (meld.Tiles.Contains(winningTile)) continue;
                count++;
            }
            if (count >= 4) hits.Add(Hit("四暗刻", 20, "concealed_triplets"));
            else if (count == 3) hits.Add(Hit("三暗刻", 8, "concealed_triplets"));
            else if (count == 2) hits.Add(Hit("二暗刻", 1, "concealed_triplets"));
        }

        private static void DetectConsecutiveTriplets(Decomposition decomposition, List<FanHit> hits) {
            foreach (IGrouping<int, int> group in decomposition.Melds
                .Where(meld => meld.IsTripletLike && IsSuited(meld.Head))
                .Select(meld => meld.Head)
                .GroupBy(Suit, Number)) {
                List<int> numbers = group.Distinct().OrderBy(number => number).ToList();
                var chains = new List<List<int>>();
                var current = new List<int>();
                foreach (int number in numbers) {
                    if (current.Count == 0 || number == current[current.Count - 1] + 1) current.Add(number);
                    else { chains.Add(current); current = new List<int> { number }; }
                }
                if (current.Count > 0) chains.Add(current);
                foreach (List<int> chain in chains) {
                    if (chain.Count >= 4) hits.Add(Hit("四连刻", 20, "consecutive_triplets"));
                    else if (chain.Count == 3) hits.Add(Hit("三连刻", 8, "consecutive_triplets"));
                    else if (chain.Count == 2) hits.Add(Hit("二连刻", 1, "consecutive_triplets", true));
                }
            }
        }

        private static void DetectIdenticalSequences(Decomposition decomposition, List<FanHit> hits) {
            List<int> counts = decomposition.Melds
                .Where(meld => meld.Kind == MeldKind.Sequence)
                .GroupBy(meld => Tuple.Create(Suit(meld.Head), Number(meld.Head)))
                .Select(group => group.Count())
                .ToList();
            if (counts.Any(count => count >= 4)) hits.Add(Hit("一色四同顺", 20, "identical_sequences"));
            else if (counts.Any(count => count == 3)) hits.Add(Hit("一色三同顺", 8, "identical_sequences"));
            else if (counts.Count(count => count >= 2) >= 2) hits.Add(Hit("二般高", 8, "identical_sequences"));
            else if (counts.Count(count => count >= 2) == 1) hits.Add(Hit("一般高", 1, "identical_sequences"));
        }

        private static void DetectKongCount(List<Meld> melds, List<FanHit> hits) {
            int count = melds.Count(meld => meld.Kind == MeldKind.Kong && meld.Declared);
            if (count >= 4) hits.Add(Hit("四杠", 20, "kong_count"));
            else if (count == 3) hits.Add(Hit("三杠", 8, "kong_count"));
            else if (count == 2) hits.Add(Hit("二杠", 3, "kong_count"));
            else if (count == 1) hits.Add(Hit("一杠", 1, "kong_count"));
        }

        private static List<Decomposition> FindStandardDecompositions(
            List<int> concealedTiles,
            List<Meld> declaredMelds) {
            List<Meld> declaredSets = declaredMelds.Where(meld => meld.IsSet).ToList();
            int neededMelds = 4 - declaredSets.Count;
            if (neededMelds < 0 || concealedTiles.Count != neededMelds * 3 + 2) {
                return new List<Decomposition>();
            }

            Dictionary<int, int> counts = BuildCounts(concealedTiles);
            var results = new List<Decomposition>();
            foreach (int pairTile in ValidTiles) {
                if (counts[pairTile] < 2 || counts[pairTile] == 4) continue;
                counts[pairTile] -= 2;
                var concealedMeldLists = new List<List<Meld>>();
                FindMelds(counts, neededMelds, new List<Meld>(), concealedMeldLists);
                foreach (List<Meld> concealedMelds in concealedMeldLists) {
                    results.Add(new Decomposition {
                        Melds = declaredSets.Concat(concealedMelds).ToList(),
                        Pair = NewMeld(MeldKind.Pair, pairTile, 2, true, false),
                    });
                }
                counts[pairTile] += 2;
            }
            return results;
        }

        private static void FindMelds(
            Dictionary<int, int> counts,
            int remaining,
            List<Meld> current,
            List<List<Meld>> results) {
            if (remaining == 0) {
                if (counts.Values.All(count => count == 0)) results.Add(new List<Meld>(current));
                return;
            }
            int first = ValidTiles.FirstOrDefault(tile => counts[tile] > 0);
            if (first == 0) return;

            if (counts[first] >= 3 && counts[first] != 4) {
                counts[first] -= 3;
                current.Add(NewMeld(MeldKind.Triplet, first, 3, true, false));
                FindMelds(counts, remaining - 1, current, results);
                current.RemoveAt(current.Count - 1);
                counts[first] += 3;
            }

            if (IsSuited(first) && first % 10 <= 7
                && counts[first + 1] > 0 && counts[first + 2] > 0) {
                counts[first]--;
                counts[first + 1]--;
                counts[first + 2]--;
                current.Add(new Meld {
                    Kind = MeldKind.Sequence,
                    Tiles = new List<int> { first, first + 1, first + 2 },
                    Concealed = true,
                    Declared = false,
                });
                FindMelds(counts, remaining - 1, current, results);
                current.RemoveAt(current.Count - 1);
                counts[first]++;
                counts[first + 1]++;
                counts[first + 2]++;
            }
        }

        private static List<Meld> ParseMelds(IEnumerable<string> codes) {
            var melds = new List<Meld>();
            foreach (string code in codes) {
                if (string.IsNullOrEmpty(code) || code.Length < 2
                    || !int.TryParse(code.Substring(1), out int tile)) continue;
                char marker = code[0];
                bool concealed = char.IsUpper(marker);
                switch (char.ToLowerInvariant(marker)) {
                    case 's':
                        melds.Add(new Meld {
                            Kind = MeldKind.Sequence,
                            Tiles = new List<int> { tile, tile + 1, tile + 2 },
                            Concealed = concealed,
                            Declared = true,
                        });
                        break;
                    case 'k': melds.Add(NewMeld(MeldKind.Triplet, tile, 3, concealed, true)); break;
                    case 'g': melds.Add(NewMeld(MeldKind.Kong, tile, 4, concealed, true)); break;
                    case 'q': melds.Add(NewMeld(MeldKind.Pair, tile, 2, true, false)); break;
                }
            }
            return melds;
        }

        private static Meld NewMeld(
            MeldKind kind,
            int tile,
            int count,
            bool concealed,
            bool declared) {
            return new Meld {
                Kind = kind,
                Tiles = Enumerable.Repeat(tile, count).ToList(),
                Concealed = concealed,
                Declared = declared,
            };
        }

        private static Dictionary<int, int> BuildCounts(IEnumerable<int> tiles) {
            var counts = ValidTiles.ToDictionary(tile => tile, _ => 0);
            foreach (int tile in tiles) {
                if (counts.ContainsKey(tile)) counts[tile]++;
            }
            return counts;
        }

        private static bool IsSevenPairs(List<int> tiles) {
            return tiles.Count == 14 && BuildCounts(tiles).Values.Sum(count => count / 2) == 7;
        }

        private static bool IsThirteenOrphans(List<int> tiles) {
            if (tiles.Count != 14) return false;
            Dictionary<int, int> counts = BuildCounts(tiles);
            return Orphans.All(tile => counts[tile] >= 1)
                && Orphans.Count(tile => counts[tile] == 2) == 1
                && counts.Where(entry => !Orphans.Contains(entry.Key)).All(entry => entry.Value == 0);
        }

        private static bool IsNineGates(List<int> preWinTiles, int winningTile, List<Meld> melds) {
            if (preWinTiles == null || preWinTiles.Count != 13 || !IsClosed(melds) || !IsSuited(winningTile)) return false;
            int suit = Suit(winningTile);
            if (preWinTiles.Any(tile => !IsSuited(tile) || Suit(tile) != suit)) return false;
            Dictionary<int, int> counts = Enumerable.Range(1, 9).ToDictionary(number => number, _ => 0);
            foreach (int tile in preWinTiles) counts[Number(tile)]++;
            int[] expected = { 3, 1, 1, 1, 1, 1, 1, 1, 3 };
            return Enumerable.Range(1, 9).All(number => counts[number] == expected[number - 1]);
        }

        private static bool IsClosed(IEnumerable<Meld> melds) {
            return melds.All(meld => !meld.Declared || (meld.Kind == MeldKind.Kong && meld.Concealed));
        }

        private static bool IsSuited(int tile) => tile >= 11 && tile <= 39 && tile % 10 != 0;
        private static bool IsHonor(int tile) => tile >= 41 && tile <= 47;
        private static bool IsWind(int tile) => tile >= 41 && tile <= 44;
        private static bool IsDragon(int tile) => tile >= 45 && tile <= 47;
        private static bool IsTerminal(int tile) => Terminals.Contains(tile);
        private static bool IsSimple(int tile) => IsSuited(tile) && Number(tile) >= 2 && Number(tile) <= 8;
        private static int Suit(int tile) => tile / 10;
        private static int Number(int tile) => tile % 10;
    }
}
