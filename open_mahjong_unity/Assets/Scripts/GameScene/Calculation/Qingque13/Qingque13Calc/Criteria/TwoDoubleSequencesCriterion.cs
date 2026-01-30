using System.Collections.Generic;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Two double sequences - exactly 2 pairs of equivalent melds, or more than 3 pairs.
    /// C++ ref: inline res_v two_double_sequences(const hand& h)
    /// </summary>
    public class TwoDoubleSequencesCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TwoDoubleSequences;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            int count = CountEquivalentPairs(decomposition.Melds);
            return count == 2 || count > 3;
        }
        
        private int CountEquivalentPairs(List<QingqueMeld> melds)
        {
            int count = 0;
            for (int i = 0; i < melds.Count; i++)
            {
                for (int j = i + 1; j < melds.Count; j++)
                {
                    if (melds[i].Type == melds[j].Type && melds[i].Tile.Value == melds[j].Tile.Value)
                        count++;
                }
            }
            return count;
        }
    }
}
