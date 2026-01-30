using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Two short straights - two pairs of sequences with gap of 3 (e.g., 123m + 456m, and 234p + 567p).
    /// C++ ref: inline res_v two_short_straights(const hand& h)
    /// Uses visited mask to avoid counting same melds twice.
    /// </summary>
    public class TwoShortStraightsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TwoShortStraights;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var melds = decomposition.Melds;
            byte count = 0;
            byte visited = 0;
            
            for (int i = 0; i < melds.Count; i++)
            {
                for (int j = i + 1; j < melds.Count; j++)
                {
                    if (IsShiftedSequences(melds[i], melds[j], 3))
                    {
                        byte mask = (byte)((1 << i) + (1 << j));
                        if ((visited & mask) == 0)
                        {
                            count++;
                            visited |= mask;
                        }
                    }
                }
            }
            return count == 2;
        }
        
        private bool IsShiftedSequences(QingqueMeld a, QingqueMeld b, int shift)
        {
            if (a.Type != QingqueMeldType.Sequence || b.Type != QingqueMeldType.Sequence) 
                return false;
            if (a.Tile.GetSuitType() != b.Tile.GetSuitType()) return false;
            return System.Math.Abs(a.Tile.Num() - b.Tile.Num()) == shift;
        }
    }
}
