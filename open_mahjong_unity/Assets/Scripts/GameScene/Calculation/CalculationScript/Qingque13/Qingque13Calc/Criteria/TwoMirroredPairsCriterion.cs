using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Two mirrored pairs - seven pairs with two pairs in two suits mirroring each other.
    /// C++ ref: inline res_t two_mirrored_pairs(const hand& h)
    /// </summary>
    public class TwoMirroredPairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TwoMirroredPairs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            
            // Check for two distinct numbers with pairs in two suits
            for (byte i = 1; i <= 8; i++)
            {
                for (byte j = (byte)(i + 1); j <= 9; j++)
                {
                    // M and P
                    if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, i)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.M, j)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.P, i)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.P, j)) >= 2)
                        return true;
                    
                    // S and P
                    if (counter.Count(new QingqueTile(QingqueTile.SuitType.S, i)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.S, j)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.P, i)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.P, j)) >= 2)
                        return true;
                    
                    // M and S
                    if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, i)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.M, j)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.S, i)) >= 2 &&
                        counter.Count(new QingqueTile(QingqueTile.SuitType.S, j)) >= 2)
                        return true;
                }
            }
            
            // Check for one number with 4 tiles in two suits (double pairs)
            for (byte i = 1; i <= 9; i++)
            {
                // M and P
                byte mCount = counter.Count(new QingqueTile(QingqueTile.SuitType.M, i));
                byte pCount = counter.Count(new QingqueTile(QingqueTile.SuitType.P, i));
                byte mpPairCount = 0;
                if (mCount == 2) mpPairCount++;
                if (mCount == 4) mpPairCount += 2;
                if (pCount == 2) mpPairCount++;
                if (pCount == 4) mpPairCount += 2;
                if (mpPairCount == 4) return true;
                
                // S and P
                byte sCount = counter.Count(new QingqueTile(QingqueTile.SuitType.S, i));
                byte spPairCount = 0;
                if (sCount == 2) spPairCount++;
                if (sCount == 4) spPairCount += 2;
                if (pCount == 2) spPairCount++;
                if (pCount == 4) spPairCount += 2;
                if (spPairCount == 4) return true;
                
                // M and S
                byte msPairCount = 0;
                if (mCount == 2) msPairCount++;
                if (mCount == 4) msPairCount += 2;
                if (sCount == 2) msPairCount++;
                if (sCount == 4) msPairCount += 2;
                if (msPairCount == 4) return true;
            }
            
            return false;
        }
    }
}
