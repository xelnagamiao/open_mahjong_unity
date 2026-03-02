using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Mixed triple pair - one number with pairs from all three suits.
    /// C++ ref: inline res_t mixed_triple_pair(const hand& h)
    /// </summary>
    public class MixedTriplePairCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.MixedTriplePair;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            
            for (byte i = 1; i <= 9; i++)
            {
                bool hasAll = counter.Count(new QingqueTile(QingqueTile.SuitType.M, i)) >= 2 &&
                             counter.Count(new QingqueTile(QingqueTile.SuitType.P, i)) >= 2 &&
                             counter.Count(new QingqueTile(QingqueTile.SuitType.S, i)) >= 2;
                             
                if (hasAll) return true;
            }
            
            return false;
        }
    }
}
