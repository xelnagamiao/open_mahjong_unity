using System.Linq;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Connected numbers - tiles use consecutive numbers with no gaps (like 3,4,5,6).
    /// C++ ref: inline res_v connected_numbers(const hand& h)
    /// </summary>
    public class ConnectedNumbersCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ConnectedNumbers;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            
            // Must have no honour tiles
            for (byte n = 1; n <= 7; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n)) > 0) return false;
            }
            
            if (decomposition.IsSevenPairs)
            {
                // Seven pairs: count pairs per number
                uint numTable = 0u;
                foreach (var tile in QingqueTile.AllTiles)
                {
                    if (!tile.IsHonor)
                    {
                        byte count = (byte)(counter.Count(tile) / 2);
                        numTable += (uint)(count << (3 * tile.Num()));
                    }
                }
                return CheckConnected(numTable);
            }
            else
            {
                // Standard decomposition
                if (decomposition.Pair.IsHonor) return false;
                
                uint numTable = 1u << (3 * decomposition.Pair.Num());
                
                foreach (var meld in decomposition.Melds)
                {
                    if (meld.Type == QingqueMeldType.Sequence)
                    {
                        // Sequence: middle tile is base, so tiles are (num-1, num, num+1)
                        byte num = meld.Tile.Num();
                        numTable += (1u << (3 * (num - 1)));
                        numTable += (1u << (3 * num));
                        numTable += (1u << (3 * (num + 1)));
                    }
                    else
                    {
                        numTable += (1u << (3 * meld.Tile.Num()));
                    }
                }
                
                return CheckConnected(numTable);
            }
        }
        
        private static bool CheckConnected(uint numTable)
        {
            // Check for connected pattern: no gaps between used numbers
            byte state = 0; // 0=not started, 1=in sequence, 2=ended
            while (numTable > 0)
            {
                uint c = numTable & 0b111u;
                if (c > 1) return false; // More than one occurrence
                if (state == 2 && c == 1) return false; // Gap after sequence ended
                if (state == 1 && c == 0) state = 2; // Sequence ended
                if (state == 0 && c == 1) state = 1; // Sequence started
                numTable >>= 3;
            }
            
            return true;
        }
    }
}
