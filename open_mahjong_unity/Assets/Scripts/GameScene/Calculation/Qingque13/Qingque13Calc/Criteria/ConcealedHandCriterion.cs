using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Concealed hand - all open melds are concealed (暗杠 only).
    /// C++ ref: inline res_t concealed_hand(const hand& h)
    /// Checks h.melds() which is open_melds only.
    /// </summary>
    public class ConcealedHandCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ConcealedHand;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            // Only check the open melds (declared melds), not all melds from decomposition
            foreach (var meld in decomposition.OriginalHand.OpenMelds)
            {
                if (!meld.Concealed) return false;
            }
            return true;
        }
    }
}
