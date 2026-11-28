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

        private static readonly int[] JiangTiles = { 12, 15, 18, 22, 25, 28, 32, 35, 38 };
        private static readonly ChangshaHepaiCheck SharedHepai = new ChangshaHepaiCheck();

        public HashSet<int> TingpaiCheck(List<int> handTileList, List<string> combinationList) {
            var waits = new HashSet<int>();
            var hand = handTileList ?? new List<int>();
            var melds = combinationList ?? new List<string>();
            int setsNeeded = 4 - melds.Count;
            if (setsNeeded < 0 || hand.Count != setsNeeded * 3 + 1) return waits;

            var physical = ExpandAll(hand, melds);
            var physicalCounts = new int[40];
            bool allJiang = physical.Count > 0;
            for (int i = 0; i < physical.Count; i++) {
                int t = physical[i];
                if (t < 11 || t > 39 || t % 10 == 0) return waits;
                physicalCounts[t]++;
                int r = t % 10;
                if (r != 2 && r != 5 && r != 8) allJiang = false;
            }

            var candidates = StandardMahjongSolver.StandardWaitingTiles(
                hand, allowHonors: false, allowFifthCopy: true);

            if (melds.Count == 0 && hand.Count == 13) {
                var handCounts = new int[40];
                for (int i = 0; i < hand.Count; i++) handCounts[hand[i]]++;
                int oddCount = 0, oddTile = -1;
                for (int i = 0; i < ValidTiles.Length; i++) {
                    int t = ValidTiles[i];
                    if ((handCounts[t] & 1) == 1) {
                        oddCount++;
                        oddTile = t;
                    }
                }
                if (oddCount == 1) candidates.Add(oddTile);
            }
            if (allJiang) {
                for (int i = 0; i < JiangTiles.Length; i++) candidates.Add(JiangTiles[i]);
            }

            var candHand = new List<int>(hand.Count + 1);
            candHand.AddRange(hand);
            candHand.Add(0);
            var emptyWay = new List<string>(0);

            foreach (int tile in candidates) {
                if (tile < 11 || tile > 39 || physicalCounts[tile] >= 4) continue;
                candHand[candHand.Count - 1] = tile;
                var result = SharedHepai.HepaiCheck(candHand, melds, emptyWay, tile, false);
                if (result.Item1 > 0 && result.Item2.Count > 0) {
                    waits.Add(tile);
                }
            }
            return waits;
        }
    }
}
