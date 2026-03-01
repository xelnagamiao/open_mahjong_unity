using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three mirrored pairs - seven pairs with three pairs in two suits mirroring each other.
    /// C++ ref: inline res_t three_mirrored_pairs(const hand& h)
    /// </summary>
    public class ThreeMirroredPairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeMirroredPairs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            
            // Check for three distinct numbers with pairs in two suits
            for (byte i = 1; i <= 7; i++)
            {
                for (byte j = (byte)(i + 1); j <= 8; j++)
                {
                    for (byte k = (byte)(j + 1); k <= 9; k++)
                    {
                        // Check M and P
                        if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, i)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.M, j)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.M, k)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.P, i)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.P, j)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.P, k)) >= 2)
                            return true;
                        
                        // Check S and P
                        if (counter.Count(new QingqueTile(QingqueTile.SuitType.S, i)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.S, j)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.S, k)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.P, i)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.P, j)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.P, k)) >= 2)
                            return true;
                        
                        // Check M and S
                        if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, i)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.M, j)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.M, k)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.S, i)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.S, j)) >= 2 &&
                            counter.Count(new QingqueTile(QingqueTile.SuitType.S, k)) >= 2)
                            return true;
                    }
                }
            }
            
            // Check for one pair + one double pair (4 tiles) pattern
            for (byte i = 1; i <= 8; i++)
            {
                for (byte j = (byte)(i + 1); j <= 9; j++)
                {
                    // M and P: i has pair in both, j has 4 tiles in both
                    if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, i)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.P, i)) >= 2)
                    {
                        byte mCount = counter.Count(new QingqueTile(QingqueTile.SuitType.M, j));
                        byte pCount = counter.Count(new QingqueTile(QingqueTile.SuitType.P, j));
                        byte pairCount = 0;
                        if (mCount == 2) pairCount++;
                        if (mCount == 4) pairCount += 2;
                        if (pCount == 2) pairCount++;
                        if (pCount == 4) pairCount += 2;
                        if (pairCount == 4) return true;
                    }
                    
                    // S and P
                    if (counter.Count(new QingqueTile(QingqueTile.SuitType.S, i)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.P, i)) >= 2)
                    {
                        byte sCount = counter.Count(new QingqueTile(QingqueTile.SuitType.S, j));
                        byte pCount = counter.Count(new QingqueTile(QingqueTile.SuitType.P, j));
                        byte pairCount = 0;
                        if (sCount == 2) pairCount++;
                        if (sCount == 4) pairCount += 2;
                        if (pCount == 2) pairCount++;
                        if (pCount == 4) pairCount += 2;
                        if (pairCount == 4) return true;
                    }
                    
                    // M and S
                    if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, i)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.S, i)) >= 2)
                    {
                        byte mCount = counter.Count(new QingqueTile(QingqueTile.SuitType.M, j));
                        byte sCount = counter.Count(new QingqueTile(QingqueTile.SuitType.S, j));
                        byte pairCount = 0;
                        if (mCount == 2) pairCount++;
                        if (mCount == 4) pairCount += 2;
                        if (sCount == 2) pairCount++;
                        if (sCount == 4) pairCount += 2;
                        if (pairCount == 4) return true;
                    }
                }
            }
            
            return false;
        }
    }
}
