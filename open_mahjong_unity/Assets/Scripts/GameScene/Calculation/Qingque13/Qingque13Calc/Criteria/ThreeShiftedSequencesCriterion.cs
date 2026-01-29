using System.Collections.Generic;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three shifted sequences - three consecutive sequences in same suit (e.g., 234m 345m 456m).
    /// C++ ref: inline res_v three_shifted_sequences(const hand& h)
    /// </summary>
    public class ThreeShiftedSequencesCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeShiftedSequences;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            var melds = decomposition.Melds;
            
            foreach (var suit in new[] { QingqueTile.SuitType.M, QingqueTile.SuitType.P, QingqueTile.SuitType.S })
            {
                for (byte num = 2; num <= 6; num++) // 2-6 for sequences
                {
                    if (ContainsSequences(melds, suit, num, (byte)(num + 1), (byte)(num + 2))) 
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
