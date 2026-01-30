using Qingque13.Core;
using System;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Reflected hand (镜数) - standard decomposition where all melds are symmetric around center number.
    /// C++ ref: inline res_v reflected_hand(const hand& h)
    /// Uses is_reflection for decompositions, reflected_pairs for seven pairs.
    /// </summary>
    public class ReflectedHandCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ReflectedHand;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            // No honour tiles allowed
            var counter = decomposition.Counter();
            for (byte n = 1; n <= 7; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n)) > 0) return false;
            }
            
            if (decomposition.IsSevenPairs)
            {
                // C++ reflected_pairs logic
                return CheckReflectedPairs(decomposition);
            }
            else
            {
                // C++ is_reflection(d, d, 2 * d.pair().num())
                return IsReflection(decomposition, (byte)(2 * decomposition.Pair.Num()));
            }
        }
        
        /// <summary>
        /// C++ reflected_pairs: checks if seven pairs hand has reflection symmetry.
        /// All tiles at position N must have matching count at position (ref - N).
        /// </summary>
        private bool CheckReflectedPairs(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            
            // Find min and max numbers
            byte minNum = 10, maxNum = 0;
            foreach (var tile in QingqueTile.NumberedTiles)
            {
                if (counter.Count(tile) > 0)
                {
                    minNum = Math.Min(minNum, tile.Num());
                    maxNum = Math.Max(maxNum, tile.Num());
                }
            }
            
            if (minNum == 10) return false;
            
            byte refSum = (byte)(minNum + maxNum);
            
            // C++: for each tile, count(t) must equal count(reflect_by(t, ref))
            foreach (var tile in QingqueTile.NumberedTiles)
            {
                byte count = counter.Count(tile);
                if (count == 0) continue;
                
                byte reflectedNum = (byte)(refSum - tile.Num());
                if (reflectedNum < 1 || reflectedNum > 9) return false;
                
                // Reflect within same suit
                var reflectedTile = new QingqueTile(tile.GetSuitType(), reflectedNum);
                if (counter.Count(reflectedTile) != count) return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// C++ is_reflection: checks if decomposition is symmetric.
        /// Each meld in d1 must match (by is_equivalent) the reflection of some meld in d2.
        /// Since d1 == d2, we're checking self-symmetry.
        /// </summary>
        private bool IsReflection(QingqueDecomposition decomposition, byte refSum)
        {
            // First check pair is self-symmetric
            byte pairReflected = (byte)(refSum - decomposition.Pair.Num());
            if (pairReflected < 1 || pairReflected > 9) return false;
            if (decomposition.Pair.Num() != pairReflected) return false;
            
            // Track which melds have been paired
            bool[] paired = new bool[decomposition.Melds.Count];
            
            // For each meld, find a matching reflected meld
            for (int i = 0; i < decomposition.Melds.Count; i++)
            {
                if (paired[i]) continue;
                
                var meld = decomposition.Melds[i];
                bool foundMatch = false;
                
                for (int j = 0; j < decomposition.Melds.Count; j++)
                {
                    if (paired[j]) continue;
                    
                    // Check if meld[i] is equivalent to reflect_by(meld[j], ref)
                    if (IsEquivalentToReflection(meld, decomposition.Melds[j], refSum))
                    {
                        paired[i] = true;
                        paired[j] = true;
                        foundMatch = true;
                        break;
                    }
                }
                
                if (!foundMatch) return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// C++ is_equivalent(m1, reflect_by(m2, ref))
        /// Checks if m1's base tile equals reflect_by(m2.tile, ref) and same meld type.
        /// </summary>
        private bool IsEquivalentToReflection(QingqueMeld m1, QingqueMeld m2, byte refSum)
        {
            // Must be same type
            if ((m1.Type == QingqueMeldType.Triplet || m1.Type == QingqueMeldType.Kong) !=
                (m2.Type == QingqueMeldType.Triplet || m2.Type == QingqueMeldType.Kong)) return false;
            
            // Same suit required
            if (m1.Tile.GetSuitType() != m2.Tile.GetSuitType()) return false;
            
            // Reflect m2's number
            byte reflectedNum = (byte)(refSum - m2.Tile.Num());
            if (reflectedNum < 1 || reflectedNum > 9) return false;
            
            // Check if m1's number equals the reflected number
            return m1.Tile.Num() == reflectedNum;
        }
    }
}
