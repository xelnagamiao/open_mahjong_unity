using Qingque13.Core;
using System.Collections.Generic;

namespace Qingque13.Patterns
{
    /// <summary>
    /// Static pattern definitions used in criteria checking.
    /// Matches C++ namespace patterns in qingque.cpp.
    /// </summary>
    public static class QingquePatterns
    {
        // Nine gates patterns (九莲宝灯)
        public static readonly ulong NineGatesMS = 0b011001001001001001001001011000ul;
        public static readonly ulong NineGatesP = 0b011001001001001001001001011000ul << 32;

        // Knitted tiles pattern
        public static readonly ulong KnittedTiles = 0b001000000001000000001ul;
        
        // Honours pattern
        public static readonly ulong Honours = 0b001001001001001001001000ul << 32;

        // Mixed shifted triplets patterns (三色连刻)
        public static readonly List<List<QingqueMeld>> MixedShiftedTriplets = new List<List<QingqueMeld>>
        {
            // Pattern 1-7: m->p->s
            new List<QingqueMeld> { Triplet(1, 'm'), Triplet(2, 'p'), Triplet(3, 's') },
            new List<QingqueMeld> { Triplet(2, 'm'), Triplet(3, 'p'), Triplet(4, 's') },
            new List<QingqueMeld> { Triplet(3, 'm'), Triplet(4, 'p'), Triplet(5, 's') },
            new List<QingqueMeld> { Triplet(4, 'm'), Triplet(5, 'p'), Triplet(6, 's') },
            new List<QingqueMeld> { Triplet(5, 'm'), Triplet(6, 'p'), Triplet(7, 's') },
            new List<QingqueMeld> { Triplet(6, 'm'), Triplet(7, 'p'), Triplet(8, 's') },
            new List<QingqueMeld> { Triplet(7, 'm'), Triplet(8, 'p'), Triplet(9, 's') },
            
            // Pattern 8-14: p->s->m
            new List<QingqueMeld> { Triplet(1, 'p'), Triplet(2, 's'), Triplet(3, 'm') },
            new List<QingqueMeld> { Triplet(2, 'p'), Triplet(3, 's'), Triplet(4, 'm') },
            new List<QingqueMeld> { Triplet(3, 'p'), Triplet(4, 's'), Triplet(5, 'm') },
            new List<QingqueMeld> { Triplet(4, 'p'), Triplet(5, 's'), Triplet(6, 'm') },
            new List<QingqueMeld> { Triplet(5, 'p'), Triplet(6, 's'), Triplet(7, 'm') },
            new List<QingqueMeld> { Triplet(6, 'p'), Triplet(7, 's'), Triplet(8, 'm') },
            new List<QingqueMeld> { Triplet(7, 'p'), Triplet(8, 's'), Triplet(9, 'm') },
            
            // Pattern 15-21: s->m->p
            new List<QingqueMeld> { Triplet(1, 's'), Triplet(2, 'm'), Triplet(3, 'p') },
            new List<QingqueMeld> { Triplet(2, 's'), Triplet(3, 'm'), Triplet(4, 'p') },
            new List<QingqueMeld> { Triplet(3, 's'), Triplet(4, 'm'), Triplet(5, 'p') },
            new List<QingqueMeld> { Triplet(4, 's'), Triplet(5, 'm'), Triplet(6, 'p') },
            new List<QingqueMeld> { Triplet(5, 's'), Triplet(6, 'm'), Triplet(7, 'p') },
            new List<QingqueMeld> { Triplet(6, 's'), Triplet(7, 'm'), Triplet(8, 'p') },
            new List<QingqueMeld> { Triplet(7, 's'), Triplet(8, 'm'), Triplet(9, 'p') },
            
            // Pattern 22-28: m->s->p
            new List<QingqueMeld> { Triplet(1, 'm'), Triplet(2, 's'), Triplet(3, 'p') },
            new List<QingqueMeld> { Triplet(2, 'm'), Triplet(3, 's'), Triplet(4, 'p') },
            new List<QingqueMeld> { Triplet(3, 'm'), Triplet(4, 's'), Triplet(5, 'p') },
            new List<QingqueMeld> { Triplet(4, 'm'), Triplet(5, 's'), Triplet(6, 'p') },
            new List<QingqueMeld> { Triplet(5, 'm'), Triplet(6, 's'), Triplet(7, 'p') },
            new List<QingqueMeld> { Triplet(6, 'm'), Triplet(7, 's'), Triplet(8, 'p') },
            new List<QingqueMeld> { Triplet(7, 'm'), Triplet(8, 's'), Triplet(9, 'p') },
            
            // Pattern 29-35: p->m->s
            new List<QingqueMeld> { Triplet(1, 'p'), Triplet(2, 'm'), Triplet(3, 's') },
            new List<QingqueMeld> { Triplet(2, 'p'), Triplet(3, 'm'), Triplet(4, 's') },
            new List<QingqueMeld> { Triplet(3, 'p'), Triplet(4, 'm'), Triplet(5, 's') },
            new List<QingqueMeld> { Triplet(4, 'p'), Triplet(5, 'm'), Triplet(6, 's') },
            new List<QingqueMeld> { Triplet(5, 'p'), Triplet(6, 'm'), Triplet(7, 's') },
            new List<QingqueMeld> { Triplet(6, 'p'), Triplet(7, 'm'), Triplet(8, 's') },
            new List<QingqueMeld> { Triplet(7, 'p'), Triplet(8, 'm'), Triplet(9, 's') },
            
            // Pattern 36-42: s->p->m
            new List<QingqueMeld> { Triplet(1, 's'), Triplet(2, 'p'), Triplet(3, 'm') },
            new List<QingqueMeld> { Triplet(2, 's'), Triplet(3, 'p'), Triplet(4, 'm') },
            new List<QingqueMeld> { Triplet(3, 's'), Triplet(4, 'p'), Triplet(5, 'm') },
            new List<QingqueMeld> { Triplet(4, 's'), Triplet(5, 'p'), Triplet(6, 'm') },
            new List<QingqueMeld> { Triplet(5, 's'), Triplet(6, 'p'), Triplet(7, 'm') },
            new List<QingqueMeld> { Triplet(6, 's'), Triplet(7, 'p'), Triplet(8, 'm') },
            new List<QingqueMeld> { Triplet(7, 's'), Triplet(8, 'p'), Triplet(9, 'm') }
        };

        // Mixed shifted sequences patterns (三色顺)
        public static readonly List<List<QingqueMeld>> MixedShiftedSequences = new List<List<QingqueMeld>>
        {
            new List<QingqueMeld> { Sequence(2, 'm'), Sequence(3, 'p'), Sequence(4, 's') },
            new List<QingqueMeld> { Sequence(3, 'm'), Sequence(4, 'p'), Sequence(5, 's') },
            new List<QingqueMeld> { Sequence(4, 'm'), Sequence(5, 'p'), Sequence(6, 's') },
            new List<QingqueMeld> { Sequence(5, 'm'), Sequence(6, 'p'), Sequence(7, 's') },
            new List<QingqueMeld> { Sequence(6, 'm'), Sequence(7, 'p'), Sequence(8, 's') },
            
            new List<QingqueMeld> { Sequence(2, 'p'), Sequence(3, 's'), Sequence(4, 'm') },
            new List<QingqueMeld> { Sequence(3, 'p'), Sequence(4, 's'), Sequence(5, 'm') },
            new List<QingqueMeld> { Sequence(4, 'p'), Sequence(5, 's'), Sequence(6, 'm') },
            new List<QingqueMeld> { Sequence(5, 'p'), Sequence(6, 's'), Sequence(7, 'm') },
            new List<QingqueMeld> { Sequence(6, 'p'), Sequence(7, 's'), Sequence(8, 'm') },
            
            new List<QingqueMeld> { Sequence(2, 's'), Sequence(3, 'm'), Sequence(4, 'p') },
            new List<QingqueMeld> { Sequence(3, 's'), Sequence(4, 'm'), Sequence(5, 'p') },
            new List<QingqueMeld> { Sequence(4, 's'), Sequence(5, 'm'), Sequence(6, 'p') },
            new List<QingqueMeld> { Sequence(5, 's'), Sequence(6, 'm'), Sequence(7, 'p') },
            new List<QingqueMeld> { Sequence(6, 's'), Sequence(7, 'm'), Sequence(8, 'p') },
            
            new List<QingqueMeld> { Sequence(2, 'm'), Sequence(3, 's'), Sequence(4, 'p') },
            new List<QingqueMeld> { Sequence(3, 'm'), Sequence(4, 's'), Sequence(5, 'p') },
            new List<QingqueMeld> { Sequence(4, 'm'), Sequence(5, 's'), Sequence(6, 'p') },
            new List<QingqueMeld> { Sequence(5, 'm'), Sequence(6, 's'), Sequence(7, 'p') },
            new List<QingqueMeld> { Sequence(6, 'm'), Sequence(7, 's'), Sequence(8, 'p') },
            
            new List<QingqueMeld> { Sequence(2, 'p'), Sequence(3, 'm'), Sequence(4, 's') },
            new List<QingqueMeld> { Sequence(3, 'p'), Sequence(4, 'm'), Sequence(5, 's') },
            new List<QingqueMeld> { Sequence(4, 'p'), Sequence(5, 'm'), Sequence(6, 's') },
            new List<QingqueMeld> { Sequence(5, 'p'), Sequence(6, 'm'), Sequence(7, 's') },
            new List<QingqueMeld> { Sequence(6, 'p'), Sequence(7, 'm'), Sequence(8, 's') },
            
            new List<QingqueMeld> { Sequence(2, 's'), Sequence(3, 'p'), Sequence(4, 'm') },
            new List<QingqueMeld> { Sequence(3, 's'), Sequence(4, 'p'), Sequence(5, 'm') },
            new List<QingqueMeld> { Sequence(4, 's'), Sequence(5, 'p'), Sequence(6, 'm') },
            new List<QingqueMeld> { Sequence(5, 's'), Sequence(6, 'p'), Sequence(7, 'm') },
            new List<QingqueMeld> { Sequence(6, 's'), Sequence(7, 'p'), Sequence(8, 'm') }
        };

        // Mixed chained sequences patterns (三色连环)
        public static readonly List<List<QingqueMeld>> MixedChainedSequences = new List<List<QingqueMeld>>
        {
            new List<QingqueMeld> { Sequence(2, 'm'), Sequence(4, 'p'), Sequence(6, 's') },
            new List<QingqueMeld> { Sequence(3, 'm'), Sequence(5, 'p'), Sequence(7, 's') },
            new List<QingqueMeld> { Sequence(4, 'm'), Sequence(6, 'p'), Sequence(8, 's') },
            
            new List<QingqueMeld> { Sequence(2, 'p'), Sequence(4, 's'), Sequence(6, 'm') },
            new List<QingqueMeld> { Sequence(3, 'p'), Sequence(5, 's'), Sequence(7, 'm') },
            new List<QingqueMeld> { Sequence(4, 'p'), Sequence(6, 's'), Sequence(8, 'm') },
            
            new List<QingqueMeld> { Sequence(2, 's'), Sequence(4, 'm'), Sequence(6, 'p') },
            new List<QingqueMeld> { Sequence(3, 's'), Sequence(5, 'm'), Sequence(7, 'p') },
            new List<QingqueMeld> { Sequence(4, 's'), Sequence(6, 'm'), Sequence(8, 'p') },
            
            new List<QingqueMeld> { Sequence(2, 'm'), Sequence(4, 's'), Sequence(6, 'p') },
            new List<QingqueMeld> { Sequence(3, 'm'), Sequence(5, 's'), Sequence(7, 'p') },
            new List<QingqueMeld> { Sequence(4, 'm'), Sequence(6, 's'), Sequence(8, 'p') },
            
            new List<QingqueMeld> { Sequence(2, 'p'), Sequence(4, 'm'), Sequence(6, 's') },
            new List<QingqueMeld> { Sequence(3, 'p'), Sequence(5, 'm'), Sequence(7, 's') },
            new List<QingqueMeld> { Sequence(4, 'p'), Sequence(6, 'm'), Sequence(8, 's') },
            
            new List<QingqueMeld> { Sequence(2, 's'), Sequence(4, 'p'), Sequence(6, 'm') },
            new List<QingqueMeld> { Sequence(3, 's'), Sequence(5, 'p'), Sequence(7, 'm') },
            new List<QingqueMeld> { Sequence(4, 's'), Sequence(6, 'p'), Sequence(8, 'm') }
        };

        // Mixed straight patterns (三色贯通)
        public static readonly List<List<QingqueMeld>> MixedStraight = new List<List<QingqueMeld>>
        {
            new List<QingqueMeld> { Sequence(2, 'm'), Sequence(5, 'p'), Sequence(8, 's') },
            new List<QingqueMeld> { Sequence(2, 'p'), Sequence(5, 's'), Sequence(8, 'm') },
            new List<QingqueMeld> { Sequence(2, 's'), Sequence(5, 'm'), Sequence(8, 'p') },
            new List<QingqueMeld> { Sequence(2, 'm'), Sequence(5, 's'), Sequence(8, 'p') },
            new List<QingqueMeld> { Sequence(2, 'p'), Sequence(5, 'm'), Sequence(8, 's') },
            new List<QingqueMeld> { Sequence(2, 's'), Sequence(5, 'p'), Sequence(8, 'm') }
        };

        // Mirrored short straights patterns (双龙会 - 镜同对)
        public static readonly List<List<QingqueMeld>> MirroredShortStraights = new List<List<QingqueMeld>>
        {
            new List<QingqueMeld> { Sequence(2, 'm'), Sequence(5, 'm'), Sequence(2, 'p'), Sequence(5, 'p') },
            new List<QingqueMeld> { Sequence(3, 'm'), Sequence(6, 'm'), Sequence(3, 'p'), Sequence(6, 'p') },
            new List<QingqueMeld> { Sequence(4, 'm'), Sequence(7, 'm'), Sequence(4, 'p'), Sequence(7, 'p') },
            new List<QingqueMeld> { Sequence(5, 'm'), Sequence(8, 'm'), Sequence(5, 'p'), Sequence(8, 'p') },
            new List<QingqueMeld> { Sequence(2, 'm'), Sequence(8, 'm'), Sequence(2, 'p'), Sequence(8, 'p') },
            
            new List<QingqueMeld> { Sequence(2, 'p'), Sequence(5, 'p'), Sequence(2, 's'), Sequence(5, 's') },
            new List<QingqueMeld> { Sequence(3, 'p'), Sequence(6, 'p'), Sequence(3, 's'), Sequence(6, 's') },
            new List<QingqueMeld> { Sequence(4, 'p'), Sequence(7, 'p'), Sequence(4, 's'), Sequence(7, 's') },
            new List<QingqueMeld> { Sequence(5, 'p'), Sequence(8, 'p'), Sequence(5, 's'), Sequence(8, 's') },
            new List<QingqueMeld> { Sequence(2, 'p'), Sequence(8, 'p'), Sequence(2, 's'), Sequence(8, 's') },
            
            new List<QingqueMeld> { Sequence(2, 's'), Sequence(5, 's'), Sequence(2, 'm'), Sequence(5, 'm') },
            new List<QingqueMeld> { Sequence(3, 's'), Sequence(6, 's'), Sequence(3, 'm'), Sequence(6, 'm') },
            new List<QingqueMeld> { Sequence(4, 's'), Sequence(7, 's'), Sequence(4, 'm'), Sequence(7, 'm') },
            new List<QingqueMeld> { Sequence(5, 's'), Sequence(8, 's'), Sequence(5, 'm'), Sequence(8, 'm') },
            new List<QingqueMeld> { Sequence(2, 's'), Sequence(8, 's'), Sequence(2, 'm'), Sequence(8, 'm') }
        };

        // Honours and knitted tiles patterns
        public static readonly List<QingqueTileCounter> HonoursAndKnittedTiles = new List<QingqueTileCounter>
        {
            new QingqueTileCounter((KnittedTiles << 3) + (KnittedTiles << (32 + 6)), Honours + (KnittedTiles << 9)),
            new QingqueTileCounter((KnittedTiles << 6) + (KnittedTiles << (32 + 9)), Honours + (KnittedTiles << 3)),
            new QingqueTileCounter((KnittedTiles << 9) + (KnittedTiles << (32 + 3)), Honours + (KnittedTiles << 6)),
            new QingqueTileCounter((KnittedTiles << 3) + (KnittedTiles << (32 + 9)), Honours + (KnittedTiles << 6)),
            new QingqueTileCounter((KnittedTiles << 6) + (KnittedTiles << (32 + 3)), Honours + (KnittedTiles << 9)),
            new QingqueTileCounter((KnittedTiles << 9) + (KnittedTiles << (32 + 6)), Honours + (KnittedTiles << 3))
        };

        // Helper methods
        private static QingqueMeld Triplet(byte num, char suit)
        {
            QingqueTile tile = suit switch
            {
                'm' => QingqueTile.Literals.M(num),
                'p' => QingqueTile.Literals.P(num),
                's' => QingqueTile.Literals.S(num),
                _ => new QingqueTile(0)
            };
            return QingqueMeld.Triplet(tile);
        }

        private static QingqueMeld Sequence(byte num, char suit)
        {
            QingqueTile tile = suit switch
            {
                'm' => QingqueTile.Literals.M(num),
                'p' => QingqueTile.Literals.P(num),
                's' => QingqueTile.Literals.S(num),
                _ => new QingqueTile(0)
            };
            return QingqueMeld.Sequence(tile);
        }
    }
}
