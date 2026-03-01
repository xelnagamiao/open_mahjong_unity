using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Four wind pairs 2 - four pairs from wind tiles in seven pairs hand.
    /// C++ ref: inline res_t four_wind_pairs_2(const hand& h)
    /// </summary>
    public class FourWindPairs2Criterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FourWindPairs2;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            byte pairCount = 0;
            
            for (byte n = 1; n <= 4; n++)
            {
                byte count = counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n));
                pairCount += (byte)(count / 2); // Each 2 tiles = 1 pair
            }
            
            return pairCount >= 4;
        }
    }
}
