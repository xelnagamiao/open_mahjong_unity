using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Two triple pairs - two sets of same number from all three suits.
    /// C++ ref: inline res_t two_triple_pairs(const hand& h)
    /// </summary>
    public class TwoTriplePairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TwoTriplePairs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            
            for (byte i = 1; i <= 8; i++)
            {
                for (byte j = (byte)(i + 1); j <= 9; j++)
                {
                    bool iHasAll = counter.Count(new QingqueTile(QingqueTile.SuitType.M, i)) >= 2 &&
                                   counter.Count(new QingqueTile(QingqueTile.SuitType.P, i)) >= 2 &&
                                   counter.Count(new QingqueTile(QingqueTile.SuitType.S, i)) >= 2;
                                   
                    bool jHasAll = counter.Count(new QingqueTile(QingqueTile.SuitType.M, j)) >= 2 &&
                                   counter.Count(new QingqueTile(QingqueTile.SuitType.P, j)) >= 2 &&
                                   counter.Count(new QingqueTile(QingqueTile.SuitType.S, j)) >= 2;
                    
                    if (iHasAll && jHasAll) return true;
                }
            }
            
            // Also check for one number with 4 tiles in each suit (6 pairs total)
            for (byte i = 1; i <= 9; i++)
            {
                byte pairCount = 0;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, i)) == 2) pairCount++;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, i)) == 4) pairCount += 2;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.P, i)) == 2) pairCount++;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.P, i)) == 4) pairCount += 2;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.S, i)) == 2) pairCount++;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.S, i)) == 4) pairCount += 2;
                
                if (pairCount == 6) return true;
            }
            
            return false;
        }
    }
}
