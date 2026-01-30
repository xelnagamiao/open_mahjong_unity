using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Seven pairs - hand consists of exactly 7 pairs.
    /// C++ ref: inline res_t seven_pairs(const hand& h)
    /// </summary>
    public class SevenPairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.SevenPairs;
        public bool Check(QingqueDecomposition decomposition) => decomposition.IsSevenPairs;
    }
}
