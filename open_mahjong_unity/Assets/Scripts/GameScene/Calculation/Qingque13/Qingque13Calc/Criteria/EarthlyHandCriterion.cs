using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Earthly hand - non-dealer wins on first draw (has heavenly/earthly flag, NOT self-drawn).
    /// C++ ref: inline res_t earthly_hand(const hand& h)
    /// C++ logic: h.winning_type()(win_type::heavenly_or_earthly_hand, win_type::self_drawn)
    /// = has heavenly_or_earthly_hand AND does NOT have self_drawn
    /// </summary>
    public class EarthlyHandCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.EarthlyHand;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            // Has HeavenlyOrEarthlyHand flag AND does NOT have SelfDrawn flag
            return decomposition.WinningType.HasFlags(QingqueWinType.HeavenlyOrEarthlyHand, QingqueWinType.SelfDrawn);
        }
    }
}
