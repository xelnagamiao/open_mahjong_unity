using System.Collections.Generic;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three shifted triplets - three consecutive triplets in same suit (e.g., 222m 333m 444m).
    /// C++ ref: inline res_v three_shifted_triplets(const hand& h)
    /// </summary>
    public class ThreeShiftedTripletsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeShiftedTriplets;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            var melds = decomposition.Melds;
            
            foreach (var suit in new[] { QingqueTile.SuitType.M, QingqueTile.SuitType.P, QingqueTile.SuitType.S })
            {
                for (byte num = 1; num <= 7; num++) // 1-7 to allow 3 consecutive
                {
                    if (ContainsTriplets(melds, suit, num, (byte)(num + 1), (byte)(num + 2))) 
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
