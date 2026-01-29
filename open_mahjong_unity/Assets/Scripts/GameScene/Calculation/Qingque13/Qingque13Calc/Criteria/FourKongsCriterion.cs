using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Four kongs - total tile count >= 18 (4 kongs = 16 tiles + pair).
    /// C++ ref: inline res_t four_kongs(const hand& h)
    /// </summary>
    public class FourKongsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FourKongs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return decomposition.Counter().Count() >= 18;
        }
    }
}
