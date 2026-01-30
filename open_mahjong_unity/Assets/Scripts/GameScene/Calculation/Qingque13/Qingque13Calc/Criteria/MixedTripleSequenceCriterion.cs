using System.Collections.Generic;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Mixed triple sequence - sequences of same number in all three suits (e.g., 123m 123p 123s).
    /// C++ ref: inline res_v mixed_triple_sequence(const hand& h)
    /// </summary>
    public class MixedTripleSequenceCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.MixedTripleSequence;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            var melds = decomposition.Melds;
            
            // Check each possible middle number (2-8 for sequences)
            for (byte num = 2; num <= 8; num++)
            {
                if (ContainsSequences(melds, num)) return true;
            }
            return false;
        }
        
        private bool ContainsSequences(List<QingqueMeld> melds, byte midTile)
        {
            bool hasM = false, hasP = false, hasS = false;
            foreach (var meld in melds)
            {
                if (meld.Type == QingqueMeldType.Sequence && meld.Tile.Num() == midTile)
                {
                    if (meld.Tile.GetSuitType() == QingqueTile.SuitType.M) hasM = true;
                    else if (meld.Tile.GetSuitType() == QingqueTile.SuitType.P) hasP = true;
                    else if (meld.Tile.GetSuitType() == QingqueTile.SuitType.S) hasS = true;
                }
            }
            return hasM && hasP && hasS;
        }
    }
}
