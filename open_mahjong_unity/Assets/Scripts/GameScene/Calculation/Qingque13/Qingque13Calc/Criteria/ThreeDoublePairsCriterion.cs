using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three double pairs - three pairs each appearing twice (4 tiles each) in seven pairs hand.
    /// C++ ref: inline res_t three_double_pairs(const hand& h)
    /// </summary>
    public class ThreeDoublePairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeDoublePairs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            byte doublePairCount = 0;
            
            foreach (var tile in QingqueTile.AllTiles)
            {
                if (counter.Count(tile) == 4) doublePairCount++;
            }
            
            return doublePairCount >= 3;
        }
    }
}
