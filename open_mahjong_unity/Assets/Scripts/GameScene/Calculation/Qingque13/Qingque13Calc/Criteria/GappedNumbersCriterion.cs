using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Gapped numbers - all numbers fit into a pattern with equal gaps.
    /// C++ ref: inline res_t gapped_numbers(const hand& h)
    /// Patterns: alternating (1,3,5,7,9 or 2,4,6,8) or every-third (1,4,7 or 2,5,8 or 3,6,9)
    /// </summary>
    public class GappedNumbersCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.GappedNumbers;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            
            // Build number bitmap (bit 1 = number 1 present, bit 2 = number 2 present, etc.)
            ushort numTable = 0;
            foreach (var tile in QingqueTile.AllTiles)
            {
                if (counter.Count(tile) > 0)
                {
                    byte num = tile.Num();
                    if (num >= 1 && num <= 9)
                    {
                        numTable |= (ushort)(1 << num);
                    }
                }
            }
            
            // Need at least 3 distinct numbers
            if (PopCount(numTable) <= 2) return false;
            
            // Pattern 1: Alternating numbers (every other)
            // 0b1010101010 = bits for 1,3,5,7,9 (bit positions: 1,3,5,7,9)
            for (int mask = 0b1010101010; mask <= 0b10101010100; mask <<= 1)
            {
                if ((numTable | mask) == mask) return true;
            }
            
            // Pattern 2: Every third number
            // 0b10010010 = bits for 1,4,7 (bit positions: 1,4,7)
            for (int mask = 0b10010010; mask <= 0b1001001000; mask <<= 1)
            {
                if ((numTable | mask) == mask) return true;
            }
            
            return false;
        }
        
        private static int PopCount(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }
            return count;
        }
    }
}
