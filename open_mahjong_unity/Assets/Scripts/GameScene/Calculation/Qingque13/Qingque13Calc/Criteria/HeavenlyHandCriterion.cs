using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Heavenly hand - dealer wins on initial deal.
    /// C++ ref: inline res_t heavenly_hand(const hand& h)
    /// </summary>
    public class HeavenlyHandCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.HeavenlyHand;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            return decomposition.IsWonBy(QingqueWinType.HeavenlyOrEarthlyHand | QingqueWinType.SelfDrawn);
        }
    }
}
