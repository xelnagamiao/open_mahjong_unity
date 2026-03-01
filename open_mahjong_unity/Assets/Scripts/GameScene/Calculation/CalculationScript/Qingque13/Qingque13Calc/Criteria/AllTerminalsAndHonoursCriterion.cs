using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// All terminals and honours - hand contains only terminals (1s, 9s) and honours.
    /// C++ ref: inline res_t all_terminals_and_honours(const hand& h)
    /// </summary>
    public class AllTerminalsAndHonoursCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.AllTerminalsAndHonours;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            
            // Check each numbered suit - only 1 and 9 allowed
            for (byte n = 2; n <= 8; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, n)) > 0) return false;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.P, n)) > 0) return false;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.S, n)) > 0) return false;
            }
            
            return true;
        }
    }
}
