using System.Collections.Generic;
using System.Linq;

namespace Changsha {
    public class ChangshaTingpaiCheck {
        private static readonly int[] ValidTiles = Enumerable.Range(11, 9)
            .Concat(Enumerable.Range(21, 9))
            .Concat(Enumerable.Range(31, 9))
            .ToArray();

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

        private static List<int> ExpandAll(List<int> handList, List<string> combinationList) {
            var tiles = new List<int>(handList ?? new List<int>());
            foreach (string comb in combinationList ?? new List<string>()) {
                tiles.AddRange(ExpandMeld(comb));
            }
            return tiles;
        }

        public HashSet<int> TingpaiCheck(List<int> handTileList, List<string> combinationList) {
            var waits = new HashSet<int>();
            var physical = ExpandAll(handTileList, combinationList);
            var checker = new ChangshaHepaiCheck();
            foreach (int tile in ValidTiles) {
                if (physical.Count(t => t == tile) >= 4) continue;
                var hand = new List<int>(handTileList ?? new List<int>()) { tile };
                var result = checker.HepaiCheck(hand, combinationList ?? new List<string>(), new List<string>(), tile, false);
                if (result.Item1 > 0 && result.Item2.Count > 0) {
                    waits.Add(tile);
                }
            }
            return waits;
        }
    }
}
