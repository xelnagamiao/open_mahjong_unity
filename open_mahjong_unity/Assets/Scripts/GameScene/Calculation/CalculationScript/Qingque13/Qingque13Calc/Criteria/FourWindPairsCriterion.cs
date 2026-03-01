using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Four wind pairs - seven pairs hand containing all 4 wind tiles as pairs.
    /// C++ ref: inline res_t four_wind_pairs(const hand& h)
    /// </summary>
    public class FourWindPairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FourWindPairs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            
            // All 4 winds must be present as pairs (can be 2 or 4 tiles each)
            for (byte n = 1; n <= 4; n++)
            {
                byte count = counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n));
                if (count != 2 && count != 4) return false;
            }
            
            return true;
        }
    }
}
