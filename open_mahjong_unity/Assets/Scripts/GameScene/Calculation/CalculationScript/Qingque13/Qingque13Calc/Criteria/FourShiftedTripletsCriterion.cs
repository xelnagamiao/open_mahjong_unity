using System.Collections.Generic;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Four shifted triplets - four consecutive triplets in same suit (e.g., 111m 222m 333m 444m).
    /// C++ ref: inline res_v four_shifted_triplets(const hand& h)
    /// </summary>
    public class FourShiftedTripletsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FourShiftedTriplets;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            var melds = decomposition.Melds;
            
            foreach (var suit in new[] { QingqueTile.SuitType.M, QingqueTile.SuitType.P, QingqueTile.SuitType.S })
            {
                for (byte num = 1; num <= 6; num++) // 1-6 to allow 4 consecutive
                {
                    if (ContainsTriplets(melds, suit, num, (byte)(num + 1), (byte)(num + 2), (byte)(num + 3)))
                        return true;
                }
            }
            return false;
        }
        
        private bool ContainsTriplets(List<QingqueMeld> melds, QingqueTile.SuitType suit, params byte[] nums)
        {
            foreach (var n in nums)
            {
                bool found = false;
                foreach (var meld in melds)
                {
                    if ((meld.Type == QingqueMeldType.Triplet || meld.Type == QingqueMeldType.Kong) && 
                        meld.Tile.GetSuitType() == suit && 
                        meld.Tile.Num() == n)
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
