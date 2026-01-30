using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Two kongs - total tile count >= 16.
    /// C++ ref: inline res_t two_kongs(const hand& h)
    /// </summary>
    public class TwoKongsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TwoKongs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return decomposition.Counter().Count() >= 16;
        }
    }
}
