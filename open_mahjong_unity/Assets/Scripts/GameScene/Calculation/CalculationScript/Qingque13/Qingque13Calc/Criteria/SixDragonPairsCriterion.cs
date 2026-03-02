using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Six dragon pairs - seven pairs hand with 3 pairs of dragons appearing twice (4 tiles each).
    /// C++ ref: inline res_t six_dragon_pairs(const hand& h)
    /// </summary>
    public class SixDragonPairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.SixDragonPairs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            
            // Count pairs of dragons
            byte pairCount = 0;
            for (byte n = 5; n <= 7; n++)
            {
                byte count = counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n));
                if (count == 2) pairCount += 1;
                else if (count == 4) pairCount += 2;
            }
            
            return pairCount == 6;
        }
    }
}
