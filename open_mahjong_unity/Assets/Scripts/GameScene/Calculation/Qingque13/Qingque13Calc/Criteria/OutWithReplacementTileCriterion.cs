using Qingque13.Core;

namespace Qingque13.Criteria
{
    public class OutWithReplacementTileCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.OutWithReplacementTile;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var wt = decomposition.WinningType;
            return wt.HasFlags(QingqueWinType.KongRelated | QingqueWinType.SelfDrawn);
        }
    }
}
