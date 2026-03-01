using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Double pair - one pair appearing twice (4 tiles) in seven pairs hand.
    /// C++ ref: inline res_t double_pair(const hand& h)
    /// </summary>
    public class DoublePairCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.DoublePair;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            byte doublePairCount = 0;
            
            foreach (var tile in QingqueTile.AllTiles)
            {
                if (counter.Count(tile) == 4) doublePairCount++;
            }
            
            return doublePairCount >= 1;
        }
    }
}
