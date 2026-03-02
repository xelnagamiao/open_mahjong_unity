using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Quadruple sequence - all 4 melds are identical sequences.
    /// C++ ref: inline res_v quadruple_sequence(const hand& h)
    /// </summary>
    public class QuadrupleSequenceCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.QuadrupleSequence;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            var melds = decomposition.Melds;
            if (melds.Count < 4) return false;
            
            // All melds must be equivalent to the first
            for (int i = 1; i < melds.Count; i++)
            {
                if (!IsEquivalent(melds[0], melds[i])) return false;
            }
            return true;
        }
        
        private bool IsEquivalent(QingqueMeld a, QingqueMeld b)
        {
            return a.Type == b.Type && a.Tile.Value == b.Tile.Value;
        }
    }
}
