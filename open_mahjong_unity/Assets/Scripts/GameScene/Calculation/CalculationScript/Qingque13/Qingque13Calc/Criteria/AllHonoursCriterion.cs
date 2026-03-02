using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// All honours - hand contains only honour tiles (winds and dragons), no numbered tiles.
    /// C++ ref: inline res_t all_honours(const hand& h)
    /// </summary>
    public class AllHonoursCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.AllHonours;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            
            // Check if any numbered tile exists
            foreach (var tile in QingqueTile.NumberedTiles)
            {
                if (counter.Count(tile) > 0) return false;
            }
            return true;
        }
    }
}
