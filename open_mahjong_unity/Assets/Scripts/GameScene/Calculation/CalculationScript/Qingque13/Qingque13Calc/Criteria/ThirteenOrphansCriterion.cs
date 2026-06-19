using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Thirteen Orphans (十三幺) - hand contains one of each terminal (1,9 of each suit)
    /// and one of each honor tile (E,S,W,N,C,F,P), plus a pair of any one of them.
    /// This is a special hand that does NOT follow the normal 4-melds-1-pair structure.
    /// C++ ref: GS_check in GBhepai.cs
    /// </summary>
    public class ThirteenOrphansCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThirteenOrphans;

        public bool Check(QingqueDecomposition decomposition)
        {
            // Thirteen orphans is a hand-level check. Access the full hand
            // via the decomposition's OriginalHand reference.
            return IsThirteenOrphans(decomposition.OriginalHand);
        }

        /// <summary>
        /// Checks if a hand matches the thirteen orphans pattern:
        /// One of each terminal/honour tile (13 distinct tiles), plus one duplicate (the pair).
        /// </summary>
        public static bool IsThirteenOrphans(QingqueHand hand)
        {
            var counter = hand.TotalCounter;

            // Reject any non-terminal, non-honour tiles
            foreach (var tile in QingqueTile.AllTiles)
            {
                byte count = counter.Count(tile);
                if (count == 0) continue;

                // Only terminals (1 or 9) and honours are allowed
                if (!tile.IsHonor && !tile.IsTerminal)
                    return false;
            }

            // Count singles and pairs among the 13 terminal/honour tiles
            int pairCount = 0;
            int singleCount = 0;

            foreach (var tile in QingqueTile.TerminalHonourTiles)
            {
                byte count = counter.Count(tile);
                if (count == 1)
                    singleCount++;
                else if (count == 2)
                    pairCount++;
                else if (count != 0)
                    return false; // count 3+ is invalid for thirteen orphans
            }

            // Must have exactly 12 singles and 1 pair
            return singleCount == 12 && pairCount == 1;
        }
    }
}
