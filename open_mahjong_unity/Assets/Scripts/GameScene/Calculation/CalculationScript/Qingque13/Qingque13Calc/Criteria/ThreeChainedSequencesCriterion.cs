using System.Collections.Generic;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three chained sequences - three sequences with gaps of 2 in same suit.
    /// C++ ref: inline res_v three_chained_sequences(const hand& h)
    /// Pattern: e.g., 123 345 567 (middle tiles: 2, 4, 6) or 234 456 678 (middle tiles: 3, 5, 7) etc.
    /// </summary>
    public class ThreeChainedSequencesCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeChainedSequences;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            var melds = decomposition.Melds;
            
            foreach (var suit in new[] { QingqueTile.SuitType.M, QingqueTile.SuitType.P, QingqueTile.SuitType.S })
            {
                // Starting middle tile: 2, 3, or 4
                for (byte start = 2; start <= 4; start++)
                {
                    if (ContainsSequences(melds, suit, start, (byte)(start + 2), (byte)(start + 4)))
                        return true;
                }
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
