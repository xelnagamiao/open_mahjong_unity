using System.Linq;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Common number - pair has a number, and all melds contain that same number.
    /// C++ ref: inline res_v common_number(const hand& h)
    /// Note: If any honour tiles exist, immediately return false.
    /// </summary>
    public class CommonNumberCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.CommonNumber;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            
            // If any honour tiles are present, return false
            for (byte n = 1; n <= 7; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n)) > 0) return false;
            }
            
            byte num = decomposition.Pair.Num();
            if (num == 0) return false; // Honor tile has no number (should already be caught above)
            
            foreach (var meld in decomposition.Melds)
            {
                var tiles = meld.GetTiles();
                if (!tiles.Any(t => t.Num() == num)) return false;
            }
            
            return true;
        }
    }
}
