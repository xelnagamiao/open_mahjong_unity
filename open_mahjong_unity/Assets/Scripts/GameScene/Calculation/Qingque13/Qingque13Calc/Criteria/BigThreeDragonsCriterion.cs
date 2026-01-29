using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Big three dragons - all 3 dragon tiles have 3+ occurrences.
    /// C++ ref: inline res_t big_three_dragons(const hand& h)
    /// </summary>
    public class BigThreeDragonsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.BigThreeDragons;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            var dragons = new[] 
            { 
                new QingqueTile(QingqueTile.SuitType.Z, 5), // Red
                new QingqueTile(QingqueTile.SuitType.Z, 6), // Green
                new QingqueTile(QingqueTile.SuitType.Z, 7)  // White
            };
            
            foreach (var dragon in dragons)
            {
                if (counter.Count(dragon) < 3) return false;
            }
            return true;
        }
    }
}
