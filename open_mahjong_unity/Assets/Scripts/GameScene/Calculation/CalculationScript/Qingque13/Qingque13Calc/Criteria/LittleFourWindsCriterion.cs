using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Little four winds - 3 winds have 3+ tiles, 1 wind has 2+ tiles (total count = 7).
    /// C++ ref: inline res_t little_four_winds(const hand& h)
    /// </summary>
    public class LittleFourWindsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.LittleFourWinds;
        
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
            
            byte count = 0;
            foreach (var wind in winds)
            {
                byte windCount = counter.Count(wind);
                if (windCount >= 3) count++;
                if (windCount >= 2) count++;
            }
            
            return count == 7;
        }
    }
}
