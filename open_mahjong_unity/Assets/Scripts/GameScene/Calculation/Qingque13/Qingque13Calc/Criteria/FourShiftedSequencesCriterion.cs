using System.Collections.Generic;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Four shifted sequences - four consecutive sequences in same suit (e.g., 123m 234m 345m 456m).
    /// C++ ref: inline res_v four_shifted_sequences(const hand& h)
    /// </summary>
    public class FourShiftedSequencesCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FourShiftedSequences;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            var melds = decomposition.Melds;
            
            foreach (var suit in new[] { QingqueTile.SuitType.M, QingqueTile.SuitType.P, QingqueTile.SuitType.S })
            {
                for (byte num = 2; num <= 5; num++) // 2-5 (sequences start at 2)
                {
                    if (ContainsSequences(melds, suit, num, (byte)(num + 1), (byte)(num + 2), (byte)(num + 3)))
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
