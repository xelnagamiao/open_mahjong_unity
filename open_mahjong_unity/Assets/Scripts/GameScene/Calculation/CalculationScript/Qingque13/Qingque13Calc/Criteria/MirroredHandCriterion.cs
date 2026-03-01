using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Mirrored hand (镜同) - two pairs of mixed double sequences or mixed double triplets.
    /// C++ ref: inline res_v mirrored_hand(const hand& h)
    /// </summary>
    public class MirroredHandCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.MirroredHand;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            // Must have exactly 2 suits
            byte suitDistribution = 0;
            foreach (var meld in decomposition.Melds)
            {
                if (meld.Tile.GetSuitType() != QingqueTile.SuitType.Z)
                {
                    suitDistribution |= (byte)(1 << (byte)meld.Tile.GetSuitType());
                }
            }
            
            if (CountBits(suitDistribution) != 2) return false;
            
            // Count pairs of mixed double sequences or mixed double triplets
            byte matchCount = 0;
            byte visited = 0;
            
            for (byte i = 0; i < decomposition.Melds.Count; i++)
            {
                for (byte j = (byte)(i + 1); j < decomposition.Melds.Count; j++)
                {
                    if (IsMixedDoubleMeld(decomposition.Melds[i], decomposition.Melds[j]))
                    {
                        byte mask = (byte)((1 << i) | (1 << j));
                        if ((visited & mask) == 0)
                        {
                            matchCount++;
                            visited |= mask;
                        }
                    }
                }
            }
            
            return matchCount == 2;
        }
        
        private bool IsMixedDoubleMeld(QingqueMeld meld1, QingqueMeld meld2)
        {
            // Different suits required
            if (meld1.Tile.GetSuitType() == meld2.Tile.GetSuitType()) return false;
            if (meld1.Tile.GetSuitType() == QingqueTile.SuitType.Z || meld2.Tile.GetSuitType() == QingqueTile.SuitType.Z) return false;
            
            // Same type and same number
            if (meld1.Type != meld2.Type) return false;
            if (meld1.Tile.Num() != meld2.Tile.Num()) return false;
            
            return true;
        }
        
        private byte CountBits(byte value)
        {
            byte count = 0;
            while (value != 0)
            {
                count++;
                value &= (byte)(value - 1);
            }
            return count;
        }
    }
}
