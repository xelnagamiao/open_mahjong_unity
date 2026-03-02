using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Seven wind pairs - seven pairs hand with all 7 pairs from wind tiles (4 winds appearing 7 times total).
    /// C++ ref: inline res_t seven_wind_pairs(const hand& h)
    /// </summary>
    public class SevenWindPairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.SevenWindPairs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            
            // Count pairs from wind tiles (E, S, W, N)
            byte pairCount = 0;
            for (byte n = 1; n <= 4; n++)
            {
                byte count = counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n));
                if (count == 2) pairCount++;
                if (count == 4) pairCount += 2;
            }
            
            return pairCount == 7;
        }
    }
}
