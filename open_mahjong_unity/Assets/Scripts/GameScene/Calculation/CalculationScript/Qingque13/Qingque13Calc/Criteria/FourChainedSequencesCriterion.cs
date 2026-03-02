using System.Collections.Generic;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Four chained sequences - sequences at intervals of 2 tiles in same suit.
    /// C++ ref: inline res_v four_chained_sequences(const hand& h)
    /// Pattern: 123 345 567 789 (middle tiles: 2, 4, 6, 8)
    /// </summary>
    public class FourChainedSequencesCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FourChainedSequences;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            var melds = decomposition.Melds;
            
            // Check for chained sequences in each suit (middle tiles: 2, 4, 6, 8)
            foreach (var suit in new[] { QingqueTile.SuitType.M, QingqueTile.SuitType.P, QingqueTile.SuitType.S })
            {
                if (ContainsSequences(melds, suit, 2, 4, 6, 8)) return true;
            }
            return false;
        }
        
        private bool ContainsSequences(List<QingqueMeld> melds, QingqueTile.SuitType suit, params byte[] middleTiles)
        {
            foreach (var midTile in middleTiles)
            {
                bool found = false;
                foreach (var meld in melds)
                {
                    if (meld.Type == QingqueMeldType.Sequence && 
                        meld.Tile.GetSuitType() == suit && 
                        meld.Tile.Num() == midTile)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }
    }
}
