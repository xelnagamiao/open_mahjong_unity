using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Eight hog - at least 2 tiles appearing 4 times (not counting declared kongs).
    /// C++ ref: inline res_t eight_hog(const hand& h)
    /// </summary>
    public class EightHogCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.EightHog;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            
            // Count tiles appearing 4 times
            byte count = 0;
            foreach (var tile in QingqueTile.AllTiles)
            {
                if (counter.Count(tile) == 4) count++;
            }
            
            // Subtract declared kongs (open melds with Kong type)
            foreach (var meld in decomposition.OriginalHand.OpenMelds)
            {
                if (meld.Type == QingqueMeldType.Kong)
                {
                    count--;
                }
            }
            
            return count >= 2;
        }
    }
}
