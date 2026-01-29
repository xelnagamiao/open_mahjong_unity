using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three dragon pairs - seven pairs hand containing all 3 dragon tiles as pairs.
    /// C++ ref: inline res_t three_dragon_pairs(const hand& h)
    /// </summary>
    public class ThreeDragonPairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeDragonPairs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            
            // All 3 dragons must be present as pairs (2 or 4 tiles each)
            for (byte n = 5; n <= 7; n++)
            {
                byte count = counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n));
                if (count != 2 && count != 4) return false;
            }
            
            return true;
        }
    }
}
