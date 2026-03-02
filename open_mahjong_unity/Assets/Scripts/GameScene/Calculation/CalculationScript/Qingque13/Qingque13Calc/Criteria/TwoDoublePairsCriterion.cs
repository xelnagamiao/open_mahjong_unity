using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Two double pairs - two pairs each appearing twice (4 tiles each) in seven pairs hand.
    /// C++ ref: inline res_t two_double_pairs(const hand& h)
    /// </summary>
    public class TwoDoublePairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TwoDoublePairs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            byte doublePairCount = 0;
            
            foreach (var tile in QingqueTile.AllTiles)
            {
                if (counter.Count(tile) == 4) doublePairCount++;
            }
            
            return doublePairCount >= 2;
        }
    }
}
