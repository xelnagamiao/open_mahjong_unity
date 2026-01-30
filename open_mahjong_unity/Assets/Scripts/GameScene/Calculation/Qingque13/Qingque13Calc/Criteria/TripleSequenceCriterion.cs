using System.Collections.Generic;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Triple sequence - at least 3 pairs of melds are identical (counts equivalent pairs).
    /// C++ ref: inline res_v triple_sequence(const hand& h)
    /// </summary>
    public class TripleSequenceCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.TripleSequence;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            return CountEquivalentPairs(decomposition.Melds) >= 3;
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
