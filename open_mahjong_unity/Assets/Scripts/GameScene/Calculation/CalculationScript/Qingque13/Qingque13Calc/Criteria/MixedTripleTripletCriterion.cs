using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Mixed triple triplet - exactly 3 pairs of mixed double triplets (same number, different suits).
    /// C++ ref: inline res_v mixed_triple_triplet(const hand& h)
    /// </summary>
    public class MixedTripleTripletCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.MixedTripleTriplet;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var melds = decomposition.Melds;
            byte count = 0;
            
            for (int i = 0; i < melds.Count; i++)
            {
                for (int j = i + 1; j < melds.Count; j++)
                {
                    if (IsMixedDoubleTriplet(melds[i], melds[j])) count++;
                }
            }
            return count == 3;
        }
        
        private bool IsMixedDoubleTriplet(QingqueMeld a, QingqueMeld b)
        {
            if (a.Type == QingqueMeldType.Sequence || b.Type == QingqueMeldType.Sequence) 
                return false;
            if (a.Tile.GetSuitType() == b.Tile.GetSuitType()) return false;
            return a.Tile.Num() == b.Tile.Num();
        }
    }
}
