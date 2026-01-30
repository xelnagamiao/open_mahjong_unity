using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Five wind pairs - five pairs from wind tiles in seven pairs hand.
    /// C++ ref: inline res_t five_wind_pairs(const hand& h)
    /// </summary>
    public class FiveWindPairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FiveWindPairs;
        
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
            
            return pairCount >= 5;
        }
    }
}
