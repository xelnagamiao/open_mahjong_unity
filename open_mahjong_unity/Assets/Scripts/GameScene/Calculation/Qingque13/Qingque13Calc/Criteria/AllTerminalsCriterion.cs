using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// All terminals - hand contains only terminal tiles (1s and 9s).
    /// C++ ref: inline res_t all_terminals(const hand& h)
    /// </summary>
    public class AllTerminalsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.AllTerminals;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            var terminals = new[] 
            { 
                new QingqueTile(QingqueTile.SuitType.M, 1),
                new QingqueTile(QingqueTile.SuitType.M, 9),
                new QingqueTile(QingqueTile.SuitType.P, 1),
                new QingqueTile(QingqueTile.SuitType.P, 9),
                new QingqueTile(QingqueTile.SuitType.S, 1),
                new QingqueTile(QingqueTile.SuitType.S, 9)
            };
            
            foreach (var tile in QingqueTile.AllTiles)
            {
                if (counter.Count(tile) == 0) continue;
                
                bool isTerminal = false;
                foreach (var terminal in terminals)
                {
                    if (tile.Equals(terminal))
                    {
                        isTerminal = true;
                        break;
                    }
                }
                
                if (!isTerminal) return false;
            }
            
            return true;
        }
    }
}
