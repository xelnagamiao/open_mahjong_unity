using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three consecutive numbers - only three consecutive numbers appear.
    /// C++ ref: inline res_t three_consecutive_numbers(const hand& h)
    /// </summary>
    public class ThreeConsecutiveNumbersCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeConsecutiveNumbers;
        
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
            
            if (last - first > 2) return false;
            
            int count = 0;
            for (int i = 0; i < 9; i++)
            {
                if (numbers[i]) count++;
            }
            
            return count <= 3;
        }
    }
}
