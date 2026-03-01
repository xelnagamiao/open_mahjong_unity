using System.Linq;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// All triplets - all melds are triplets/kongs (no sequences).
    /// C++ ref: inline res_v all_triplets(const hand& h)
    /// </summary>
    public class AllTripletsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.AllTriplets;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return decomposition.Melds.All(m => m.Type != QingqueMeldType.Sequence);
        }
    }
}
