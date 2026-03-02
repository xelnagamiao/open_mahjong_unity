using System.Collections.Generic;
using Qingque13.Core;
using Qingque13.Patterns;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Mixed shifted triplets - checks against predefined patterns in QingquePatterns.
    /// C++ ref: inline res_v mixed_shifted_triplets(const hand& h)
    /// </summary>
    public class MixedShiftedTripletsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.MixedShiftedTriplets;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            foreach (var pattern in QingquePatterns.MixedShiftedTriplets)
            {
                if (ContainsMelds(decomposition.Melds, pattern)) return true;
            }
            return false;
        }
        
        private bool ContainsMelds(List<QingqueMeld> melds, List<QingqueMeld> required)
        {
            foreach (var req in required)
            {
                bool found = false;
                foreach (var meld in melds)
                {
                    // Triplets or Kongs both count
                    if ((meld.Type == QingqueMeldType.Triplet || meld.Type == QingqueMeldType.Kong) && 
                        (req.Type == QingqueMeldType.Triplet || req.Type == QingqueMeldType.Kong) &&
                        meld.Tile.Value == req.Tile.Value)
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
