using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three kongs - total tile count >= 17.
    /// C++ ref: inline res_t three_kongs(const hand& h)
    /// </summary>
    public class ThreeKongsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeKongs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return decomposition.Counter().Count() >= 17;
        }
    }
}
