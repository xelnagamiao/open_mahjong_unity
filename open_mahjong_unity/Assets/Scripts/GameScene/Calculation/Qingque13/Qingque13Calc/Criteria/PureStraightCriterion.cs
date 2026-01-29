using System.Collections.Generic;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Pure straight - 123 456 789 in same suit.
    /// C++ ref: inline res_v pure_straight(const hand& h)
    /// Middle tiles: 2, 5, 8 (for sequences 123, 456, 789)
    /// </summary>
    public class PureStraightCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.PureStraight;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            var melds = decomposition.Melds;
            
            foreach (var suit in new[] { QingqueTile.SuitType.M, QingqueTile.SuitType.P, QingqueTile.SuitType.S })
            {
                if (ContainsSequences(melds, suit, 2, 5, 8)) return true;
            }
            return false;
        }
        
        private bool ContainsSequences(List<QingqueMeld> melds, QingqueTile.SuitType suit, params byte[] middleTiles)
        {
            foreach (var midTile in middleTiles)
            {
                bool found = false;
                foreach (var meld in melds)
                {
                    if (meld.Type == QingqueMeldType.Sequence && 
                        meld.Tile.GetSuitType() == suit && 
                        meld.Tile.Num() == midTile)
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
