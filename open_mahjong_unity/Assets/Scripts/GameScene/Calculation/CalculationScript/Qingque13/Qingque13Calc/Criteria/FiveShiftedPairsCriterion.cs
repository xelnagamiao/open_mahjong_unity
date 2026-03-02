using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Five shifted pairs - five consecutive numbered pairs in same suit.
    /// C++ ref: inline res_t five_shifted_pairs(const hand& h)
    /// </summary>
    public class FiveShiftedPairsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FiveShiftedPairs;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            var suits = new[] { QingqueTile.SuitType.M, QingqueTile.SuitType.P, QingqueTile.SuitType.S };
            
            foreach (var suit in suits)
            {
                for (byte start = 1; start <= 5; start++)
                {
                    bool valid = true;
                    for (byte offset = 0; offset < 5; offset++)
                    {
                        var tile = new QingqueTile(suit, (byte)(start + offset));
                        if (counter.Count(tile) < 2)
                        {
                            valid = false;
                            break;
                        }
                    }
                    if (valid) return true;
                }
            }
            
            return false;
        }
    }
}
