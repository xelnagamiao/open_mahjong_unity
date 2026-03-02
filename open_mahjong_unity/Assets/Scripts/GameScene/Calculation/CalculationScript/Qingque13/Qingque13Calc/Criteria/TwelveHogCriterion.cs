using Qingque13.Core;
using System.Linq;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Twelve hog - exactly 3 tiles appearing 4 times (not counting declared kongs).
    /// C++ ref: inline res_t twelve_hog(const hand& h)
    /// </summary>
    public class TwelveHogCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TwelveHog;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            
            // Count tiles appearing 4 times
            byte count = 0;
            foreach (var tile in QingqueTile.AllTiles)
            {
                if (counter.Count(tile) == 4) count++;
            }
            
            // Subtract declared kongs from original hand (not decomposition)
            foreach (var meld in decomposition.OriginalHand.OpenMelds)
            {
                if (meld.Type == QingqueMeldType.Kong)
                {
                    count--;
                }
            }
            
            return count == 3;
        }
    }
}
