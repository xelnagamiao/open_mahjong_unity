using Qingque13.Core;

namespace Qingque13.Criteria
{
    public class RobbingTheKongCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.RobbingTheKong;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var wt = decomposition.WinningType;
            return !wt.IsSelfDrawn && wt.HasFlags(QingqueWinType.KongRelated);
        }
    }
}
