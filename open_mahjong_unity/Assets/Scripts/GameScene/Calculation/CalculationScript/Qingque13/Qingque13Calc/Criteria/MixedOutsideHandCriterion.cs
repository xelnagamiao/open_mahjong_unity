using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Mixed outside hand - all melds and pair contain terminals (1, 9) or honours.
    /// C++ ref: inline res_v mixed_outside_hand(const hand& h)
    /// </summary>
    public class MixedOutsideHandCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.MixedOutsideHand;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) {
                // check if all terminals or honours in seven pairs
                foreach (var pair in decomposition.SevenPairTiles)
                {
                    if (!IsTerminalOrHonour(pair)) return false;
                }
                return true;
            }
            
            // Check pair
            if (!IsTerminalOrHonour(decomposition.Pair)) return false;
            
            // Check all melds
            foreach (var meld in decomposition.Melds)
            {
                if (!HasTerminalOrHonour(meld)) return false;
            }
            
            return true;
        }
        
        private bool IsTerminalOrHonour(QingqueTile tile)
        {
            if (tile.GetSuitType() == QingqueTile.SuitType.Z) return true;
            return tile.Num() == 1 || tile.Num() == 9;
        }
        
        private bool HasTerminalOrHonour(QingqueMeld meld)
        {
            if (meld.Type == QingqueMeldType.Triplet || meld.Type == QingqueMeldType.Kong)
            {
                return IsTerminalOrHonour(meld.Tile);
            }
            else // Sequence - base tile is MIDDLE tile
            {
                // For 123 sequence, middle tile = 2; for 789 sequence, middle tile = 8
                return meld.Tile.Num() == 2 || meld.Tile.Num() == 8;
            }
        }
    }
}
