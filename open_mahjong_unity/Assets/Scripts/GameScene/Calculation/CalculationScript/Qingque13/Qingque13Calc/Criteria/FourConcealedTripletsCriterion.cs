using System.Linq;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Four concealed triplets - all melds are concealed triplets/kongs (not sequences).
    /// C++ ref: inline res_v four_concealed_triplets(const hand& h)
    /// </summary>
    public class FourConcealedTripletsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FourConcealedTriplets;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return decomposition.Melds.All(m => m.Concealed && m.Type != QingqueMeldType.Sequence);
        }
    }
}
