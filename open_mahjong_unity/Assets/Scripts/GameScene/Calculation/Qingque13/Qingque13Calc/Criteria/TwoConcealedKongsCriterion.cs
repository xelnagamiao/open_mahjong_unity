using System.Linq;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Two concealed kongs - at least 2 melds are concealed kongs.
    /// C++ ref: inline res_v two_concealed_kongs(const hand& h)
    /// </summary>
    public class TwoConcealedKongsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TwoConcealedKongs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return decomposition.Melds.Count(m => m.Concealed && m.Type == QingqueMeldType.Kong) >= 2;
        }
    }
}
