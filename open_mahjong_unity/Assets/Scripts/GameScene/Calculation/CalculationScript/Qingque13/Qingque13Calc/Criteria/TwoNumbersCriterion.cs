using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Two numbers - only two different numbers appear.
    /// C++ ref: inline res_t two_numbers(const hand& h)
    /// </summary>
    public class TwoNumbersCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TwoNumbers;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            var numbers = new bool[9];

            foreach (var tile in QingqueTile.HonourTiles)
            {
                if (counter.Count(tile) > 0)
                {
                    return false;
                }
            }
            
            foreach (var tile in QingqueTile.NumberedTiles)
            {
                if (counter.Count(tile) > 0)
                {
                    numbers[tile.Num() - 1] = true;
                }
            }
            
            int distinctNumbers = 0;
            foreach (var hasNumber in numbers)
            {
                if (hasNumber) distinctNumbers++;
            }
            
            return distinctNumbers == 2;
        }
    }
}
