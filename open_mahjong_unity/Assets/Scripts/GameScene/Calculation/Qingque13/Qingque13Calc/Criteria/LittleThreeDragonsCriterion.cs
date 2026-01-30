using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Little three dragons - 2 dragons have 3+ tiles, 1 dragon has 2+ tiles (total count = 5).
    /// C++ ref: inline res_t little_three_dragons(const hand& h)
    /// </summary>
    public class LittleThreeDragonsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.LittleThreeDragons;
        
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
            
            byte count = 0;
            foreach (var dragon in dragons)
            {
                byte dragonCount = counter.Count(dragon);
                if (dragonCount >= 3) count++;
                if (dragonCount >= 2) count++;
            }
            
            return count == 5;
        }
    }
}
