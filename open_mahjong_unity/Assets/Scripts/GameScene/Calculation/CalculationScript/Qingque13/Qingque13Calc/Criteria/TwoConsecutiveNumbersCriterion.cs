using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Two consecutive numbers - only two consecutive numbers appear.
    /// C++ ref: inline res_t two_consecutive_numbers(const hand& h)
    /// </summary>
    public class TwoConsecutiveNumbersCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TwoConsecutiveNumbers;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            var numbers = new bool[9];

            for (int n = 1; n <= 7; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n)) > 0) return false;
            }
            
            foreach (var tile in QingqueTile.NumberedTiles)
            {
                if (counter.Count(tile) > 0)
                {
                    numbers[tile.Num() - 1] = true;
                }
            }
            
            int first = -1, last = -1;
            for (int i = 0; i < 9; i++)
            {
                if (numbers[i])
                {
                    if (first == -1) first = i;
                    last = i;
                }
            }
            
            if (last - first != 1) return false;
            
            int count = 0;
            for (int i = 0; i < 9; i++)
            {
                if (numbers[i]) count++;
            }
            
            return count == 2;
        }
    }
}
