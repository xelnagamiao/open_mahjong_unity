using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Half flush - tiles from at most one numbered suit (may include honours).
    /// C++ ref: inline res_t half_flush(const hand& h)
    /// Note: Full flush is a subset of half flush (derepellenise handles overlap).
    /// </summary>
    public class HalfFlushCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.HalfFlush;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            
            // Count how many numbered suits are present
            bool hasM = false, hasP = false, hasS = false;
            
            for (byte n = 1; n <= 9; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, n)) > 0) hasM = true;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.P, n)) > 0) hasP = true;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.S, n)) > 0) hasS = true;
            }
            
            int suitCount = (hasM ? 1 : 0) + (hasP ? 1 : 0) + (hasS ? 1 : 0);
            return suitCount <= 1;
        }
    }
}
