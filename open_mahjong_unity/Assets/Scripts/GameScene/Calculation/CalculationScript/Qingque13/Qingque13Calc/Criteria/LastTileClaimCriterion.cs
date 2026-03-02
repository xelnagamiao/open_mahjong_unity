using Qingque13.Core;

namespace Qingque13.Criteria
{
    public class LastTileClaimCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.LastTileClaim;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var wt = decomposition.WinningType;
            return !wt.IsSelfDrawn && wt.IsFinalTile;
        }
    }
}
