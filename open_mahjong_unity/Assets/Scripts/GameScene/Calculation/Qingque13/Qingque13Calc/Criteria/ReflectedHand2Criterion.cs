using System;
using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Reflected hand 2 (映数) - reflection with suit swapping.
    /// C++ ref: inline res_v reflected_hand_2(const hand& h)
    /// Uses is_reflection_2 for decompositions, reflected_pairs_2 for seven pairs.
    /// Key difference from ReflectedHand: uses pair's suit as reference for suit reflection.
    /// </summary>
    public class ReflectedHand2Criterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ReflectedHand2;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            // Cannot have honour tiles
            var counter = decomposition.Counter();
            for (byte n = 1; n <= 7; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n)) > 0) return false;
            }
            
            if (decomposition.IsSevenPairs)
            {
                // C++ reflected_pairs_2 logic
                return CheckReflectedPairs2(decomposition);
            }
            else
            {
                // C++ is_reflection_2(d, d, 2 * d.pair().num())
                // Uses pair's suit as reference suit
                return IsReflection2(decomposition, (byte)(2 * decomposition.Pair.Num()));
            }
        }
        
        /// <summary>
        /// C++ reflected_pairs_2: checks seven pairs with suit reflection.
        /// Two suits must have equal tile counts.
        /// Tiles are reflected both by number AND by suit.
        /// </summary>
        private bool CheckReflectedPairs2(QingqueDecomposition decomposition)
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
            
            // Count tiles in each suit
            byte mCount = CountSuit(counter, QingqueTile.SuitType.M);
            byte pCount = CountSuit(counter, QingqueTile.SuitType.P);
            byte sCount = CountSuit(counter, QingqueTile.SuitType.S);
            
            // Two suits must have equal counts
            if (mCount != pCount && mCount != sCount && pCount != sCount) return false;
            
            // Determine reference suit (s_ in C++)
            // s_ is the odd-one-out suit
            QingqueTile.SuitType refSuit = QingqueTile.SuitType.Z;
            if (mCount == pCount) refSuit = QingqueTile.SuitType.S;
            if (mCount == sCount) refSuit = QingqueTile.SuitType.P;
            if (pCount == sCount) refSuit = QingqueTile.SuitType.M;
            
            // C++: for each tile, count(t) must equal count(reflect_suit(reflect_by(t, ref), s_))
            foreach (var tile in QingqueTile.NumberedTiles)
            {
                byte count = counter.Count(tile);
                if (count == 0) continue;
                
                // First reflect by number
                byte reflectedNum = (byte)(refSum - tile.Num());
                if (reflectedNum < 1 || reflectedNum > 9) return false;
                
                // Then reflect suit
                var refSuit2 = ReflectSuit(tile.GetSuitType(), refSuit);
                var reflectedTile = new QingqueTile(refSuit2, reflectedNum);
                
                if (counter.Count(reflectedTile) != count) return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// C++ is_reflection_2: like is_reflection but also reflects suit.
        /// Uses pair's suit as the reference suit.
        /// </summary>
        private bool IsReflection2(QingqueDecomposition decomposition, byte refSum)
        {
            // Get pair's suit as reference suit
            QingqueTile.SuitType refSuit = decomposition.Pair.GetSuitType();
            
            // Check pair is self-symmetric (reflected by number equals itself)
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
                    
                    // Check if meld[i] is equivalent to reflect_suit(reflect_by(meld[j], ref), refSuit)
                    if (IsEquivalentToReflection2(meld, decomposition.Melds[j], refSum, refSuit))
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
        /// C++ is_equivalent(m1, reflect_suit(reflect_by(m2, ref), refSuit))
        /// </summary>
        private bool IsEquivalentToReflection2(QingqueMeld m1, QingqueMeld m2, byte refSum, QingqueTile.SuitType refSuit)
        {
            // Must be same meld type (triplet/kong vs sequence)
            if ((m1.Type == QingqueMeldType.Triplet || m1.Type == QingqueMeldType.Kong) !=
                (m2.Type == QingqueMeldType.Triplet || m2.Type == QingqueMeldType.Kong)) return false;
            
            // Reflect m2's number
            byte reflectedNum = (byte)(refSum - m2.Tile.Num());
            if (reflectedNum < 1 || reflectedNum > 9) return false;
            
            // Reflect m2's suit
            var reflectedSuit = ReflectSuit(m2.Tile.GetSuitType(), refSuit);
            
            // Check if m1 matches
            return m1.Tile.Num() == reflectedNum && m1.Tile.GetSuitType() == reflectedSuit;
        }
        
        /// <summary>
        /// C++ reflect_suit: if tile's suit == refSuit, keep it; otherwise swap to the third suit.
        /// </summary>
        private QingqueTile.SuitType ReflectSuit(QingqueTile.SuitType tileSuit, QingqueTile.SuitType refSuit)
        {
            if (tileSuit == refSuit) return tileSuit;
            if (refSuit == QingqueTile.SuitType.Z) return tileSuit;
            
            // Find the third suit (not tileSuit and not refSuit)
            foreach (var s in new[] { QingqueTile.SuitType.M, QingqueTile.SuitType.P, QingqueTile.SuitType.S })
            {
                if (s != refSuit && s != tileSuit) return s;
            }
            
            return tileSuit;
        }
        
        private byte CountSuit(QingqueTileCounter counter, QingqueTile.SuitType suit)
        {
            byte count = 0;
            for (byte num = 1; num <= 9; num++)
            {
                count += counter.Count(new QingqueTile(suit, num));
            }
            return count;
        }
    }
}
