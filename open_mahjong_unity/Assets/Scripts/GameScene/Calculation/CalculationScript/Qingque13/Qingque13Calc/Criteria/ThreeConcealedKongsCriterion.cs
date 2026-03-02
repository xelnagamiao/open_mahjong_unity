using System.Linq;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three concealed kongs - at least 3 melds are concealed kongs.
    /// C++ ref: inline res_v three_concealed_kongs(const hand& h)
    /// </summary>
    public class ThreeConcealedKongsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeConcealedKongs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return decomposition.Melds.Count(m => m.Concealed && m.Type == QingqueMeldType.Kong) >= 3;
        }
    }
}
