using System.Linq;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Concealed kong - at least 1 meld is a concealed kong.
    /// C++ ref: inline res_v concealed_kong(const hand& h)
    /// </summary>
    public class ConcealedKongCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ConcealedKong;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return decomposition.Melds.Any(m => m.Concealed && m.Type == QingqueMeldType.Kong);
        }
    }
}
