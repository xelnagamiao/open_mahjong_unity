using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Full flush - all tiles from same numbered suit (no honours).
    /// C++ ref: inline res_t full_flush(const hand& h)
    /// </summary>
    public class FullFlushCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FullFlush;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            
            // Check if any honour tile exists
            for (byte n = 1; n <= 7; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n)) > 0) return false;
            }
            
            // Count how many suits are present
            bool hasM = false, hasP = false, hasS = false;
            
            for (byte n = 1; n <= 9; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, n)) > 0) hasM = true;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.P, n)) > 0) hasP = true;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.S, n)) > 0) hasS = true;
            }
            
            int suitCount = (hasM ? 1 : 0) + (hasP ? 1 : 0) + (hasS ? 1 : 0);
            return suitCount == 1;
        }
    }
}
