using System.Linq;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three concealed triplets - at least 3 melds are concealed triplets/kongs.
    /// C++ ref: inline res_v three_concealed_triplets(const hand& h)
    /// </summary>
    public class ThreeConcealedTripletsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeConcealedTriplets;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return decomposition.Melds.Count(m => m.Concealed && m.Type != QingqueMeldType.Sequence) >= 3;
        }
    }
}
