using System.Collections.Generic;
using Qingque13.Core;
using Qingque13.Patterns;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Mixed straight - checks against predefined patterns in QingquePatterns.
    /// C++ ref: inline res_v mixed_straight(const hand& h)
    /// </summary>
    public class MixedStraightCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.MixedStraight;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            foreach (var pattern in QingquePatterns.MixedStraight)
            {
                if (ContainsMelds(decomposition.Melds, pattern)) return true;
            }
            return false;
        }
        
        private bool ContainsMelds(List<QingqueMeld> melds, List<QingqueMeld> required)
        {
            foreach (var req in required)
            {
                bool found = false;
                foreach (var meld in melds)
                {
                    if (meld.Type == req.Type && meld.Tile.Value == req.Tile.Value)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }
    }
}
