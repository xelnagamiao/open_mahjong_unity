using System.Linq;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Four concealed kongs - all four melds must be concealed kongs.
    /// C++ ref: inline res_v four_concealed_kongs(const hand& h)
    /// </summary>
    public class FourConcealedKongsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FourConcealedKongs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return decomposition.Melds.All(m => m.Concealed && m.Type == QingqueMeldType.Kong);
        }
    }
}
