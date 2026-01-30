using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Big four winds - all 4 wind tiles have 3+ occurrences.
    /// C++ ref: inline res_t big_four_winds(const hand& h)
    /// </summary>
    public class BigFourWindsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.BigFourWinds;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            var winds = new[] 
            { 
                new QingqueTile(QingqueTile.SuitType.Z, 1), // East
                new QingqueTile(QingqueTile.SuitType.Z, 2), // South
                new QingqueTile(QingqueTile.SuitType.Z, 3), // West
                new QingqueTile(QingqueTile.SuitType.Z, 4)  // North
            };
            
            foreach (var wind in winds)
            {
                if (counter.Count(wind) < 3) return false;
            }
            return true;
        }
    }
}
