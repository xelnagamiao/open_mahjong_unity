using System;
using System.Collections.Generic;

namespace Qingque13.Core
{
    /// <summary>
    /// Represents a single decomposition of a hand.
    /// Can be either standard (4 melds + 1 pair) or seven pairs (7 pairs).
    /// Matches C++ hand::decomposition class.
    /// </summary>
    public class QingqueDecomposition
    {
        private readonly QingqueHand originalHand;
        private readonly QingqueTile pairTile;
        private readonly List<QingqueMeld> melds;
        private readonly QingqueTileCounter remainingCounter;
        private readonly bool isSevenPairs;
        private readonly List<QingqueTile> sevenPairTiles;

        public QingqueDecomposition(QingqueHand hand)
        {
            this.originalHand = hand;
            this.pairTile = new QingqueTile(0);
            this.melds = new List<QingqueMeld>(hand.OpenMelds);
            this.remainingCounter = hand.ClosedCounter;
            this.isSevenPairs = false;
            this.sevenPairTiles = null;
        }
        
        // Constructor for seven pairs decomposition
        public QingqueDecomposition(QingqueHand hand, List<QingqueTile> pairs)
        {
            this.originalHand = hand;
            this.pairTile = pairs.Count > 0 ? pairs[0] : new QingqueTile(0);
            this.melds = new List<QingqueMeld>(hand.OpenMelds);
            this.remainingCounter = new QingqueTileCounter(0, 0);
            this.isSevenPairs = true;
            this.sevenPairTiles = new List<QingqueTile>(pairs);
        }

        public QingqueDecomposition(QingqueDecomposition other, QingqueMeld meld)
        {
            this.originalHand = other.originalHand;
            this.pairTile = other.pairTile;
            this.melds = new List<QingqueMeld>(other.melds);
            this.melds.Add(meld);
            this.remainingCounter = other.remainingCounter - meld;
            this.isSevenPairs = false;
            this.sevenPairTiles = null;
        }

        public QingqueDecomposition(QingqueDecomposition other, QingqueTile pair)
        {
            this.originalHand = other.originalHand;
            this.pairTile = pair;
            this.melds = new List<QingqueMeld>(other.melds);
            this.remainingCounter = new QingqueTileCounter(0, 0);
            this.isSevenPairs = false;
            this.sevenPairTiles = null;
        }

        public QingqueDecomposition(QingqueHand hand, QingqueTile pair, List<QingqueMeld> allMelds)
        {
            this.originalHand = hand;
            this.pairTile = pair;
            this.melds = new List<QingqueMeld>(allMelds);
            this.remainingCounter = new QingqueTileCounter(0, 0);
            this.isSevenPairs = false;
            this.sevenPairTiles = null;
        }

        public bool IsSevenPairs => isSevenPairs;
        public List<QingqueTile> SevenPairTiles => sevenPairTiles;

        public QingqueHand OriginalHand => originalHand;
        public QingqueTile Pair => pairTile;
        public List<QingqueMeld> Melds => melds;
        public QingqueTileCounter RemainingCounter => remainingCounter;

        public QingqueTileCounter Counter(bool includeOpenMelds = true)
        {
            // Seven pairs decomposition: return counter of all 7 pairs
            if (isSevenPairs && sevenPairTiles != null)
            {
                var counter = new QingqueTileCounter(0, 0);
                foreach (var tile in sevenPairTiles)
                {
                    counter.Add(tile, 2);
                }
                return counter;
            }
            
            // If we're still building (have remaining tiles), return remaining
            if (!remainingCounter.IsEmpty)
                return remainingCounter;
            
            // If complete (pair is set), return ALL tiles used in this decomposition
            if (pairTile.IsValid)
            {
                var counter = new QingqueTileCounter(0, 0);
                
                // Add pair
                counter.Add(pairTile, 2);
                
                // Add all melds
                foreach (var meld in melds)
                {
                    if (includeOpenMelds || !meld.Fixed)
                        counter.Add(meld);
                }
                
                return counter;
            }
            
            // Fallback: return original hand's counter
            return originalHand.Counter(includeOpenMelds);
        }

        public QingqueWinType WinningType => originalHand.WinningType;
        public QingqueTile WinningTile => originalHand.WinningTile;

        public bool IsWonBy(ushort flags, bool inverse = false)
        {
            return originalHand.WinningType.HasFlags(flags) ^ inverse;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            foreach (var meld in melds)
            {
                parts.Add(meld.ToString());
            }
            if (pairTile.IsValid)
            {
                if (pairTile.IsHonor)
                    parts.Add($"{pairTile}{pairTile}");
                else
                    parts.Add($"{pairTile.Num()}{pairTile.Num()}{pairTile.ToString()[1]}");
            }
            return string.Join(" ", parts);
        }
    }
}
