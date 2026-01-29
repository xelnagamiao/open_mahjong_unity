using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Pure outside hand - all melds and pair contain terminals (1 or 9), no honours.
    /// C++ ref: inline res_v pure_outside_hand(const hand& h)
    /// </summary>
    public class PureOutsideHandCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.PureOutsideHand;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) {
                // check if all terminals in seven pairs
                foreach (var pair in decomposition.SevenPairTiles)
                {
                    if (!IsTerminal(pair)) return false;
                }
                return true;
            }
            
            // Check pair
            if (!IsTerminal(decomposition.Pair)) return false;
            
            // Check all melds
            foreach (var meld in decomposition.Melds)
            {
                if (!HasTerminal(meld)) return false;
            }
            
            return true;
        }
        
        private bool IsTerminal(QingqueTile tile)
        {
            return tile.GetSuitType() != QingqueTile.SuitType.Z && (tile.Num() == 1 || tile.Num() == 9);
        }
        
        private bool HasTerminal(QingqueMeld meld)
        {
            if (meld.Type == QingqueMeldType.Triplet || meld.Type == QingqueMeldType.Kong)
            {
                return IsTerminal(meld.Tile);
            }
            else // Sequence - base tile is MIDDLE tile
            {
                // For 123 sequence, middle tile = 2; for 789 sequence, middle tile = 8
                // Terminal tiles are: middle-1=1 or middle+1=9
                return meld.Tile.Num() == 2 || meld.Tile.Num() == 8;
            }
        }
    }
}
