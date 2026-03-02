using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Nine gates - specific pattern: 1112345678999 + 1 in same suit.
    /// C++ ref: inline res_t nine_gates(const hand& h)
    /// </summary>
    public class NineGatesCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.NineGates;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            if (counter.Count() != 14) return false;
            
            var suits = new[] { QingqueTile.SuitType.M, QingqueTile.SuitType.P, QingqueTile.SuitType.S };
            
            foreach (var suit in suits)
            {
                // Check if all tiles are in this suit
                bool allSameSuit = true;
                foreach (var tile in QingqueTile.AllTiles)
                {
                    if (counter.Count(tile) > 0 && tile.GetSuitType() != suit)
                    {
                        allSameSuit = false;
                        break;
                    }
                }
                
                if (!allSameSuit) continue;
                
                // Validate nine gates pattern: 1112345678999 + 1
                bool valid = true;
                for (byte n = 1; n <= 9; n++)
                {
                    var tile = new QingqueTile(suit, n);
                    int count = counter.Count(tile);
                    
                    // Subtract winning tile if it matches this number and suit
                    if (n == decomposition.WinningTile.Num() && decomposition.WinningTile.GetSuitType() == suit)
                    {
                        count--;
                    }
                    
                    if (n == 1 || n == 9)
                    {
                        if (count != 3) { valid = false; break; }
                    }
                    else
                    {
                        if (count != 1) { valid = false; break; }
                    }
                }
                
                if (valid) return true;
            }
            
            return false;
        }
    }
}
