using System;

namespace Qingque13.Core
{
    /// <summary>
    /// Represents a tile suit type.
    /// Matches C++ enum suit_type.
    /// </summary>
    public enum QingqueSuit : byte
    {
        M = 0b01000000,  // Characters (万)
        P = 0b01100000,  // Dots (饼)
        S = 0b11000000,  // Bamboo (条)
        Z = 0b10100000,  // Winds (风)
        D = 0b10110000   // Dragons (箭)
    }

    /// <summary>
    /// Represents a mahjong tile.
    /// Matches C++ tile_t (uint8_t) and tile class.
    /// </summary>  
    public struct QingqueTile : IEquatable<QingqueTile>
    {
        public const byte Invalid = 0;

        private readonly byte value;

        /// <summary>
        /// Nested suit type enum for compatibility with criterion code.
        /// </summary>
        public enum SuitType : byte
        {
            M = 0,  // Characters (万)
            P = 1,  // Dots (饼)
            S = 2,  // Bamboo (条)
            Z = 3   // Honors (字)
        }

        public QingqueTile(byte value)
        {
            this.value = value;
        }

        /// <summary>
        /// Creates a tile from suit type and number.
        /// For honors (Z), num: 1-4 = winds (E/S/W/N), 5-7 = dragons (C/F/P).
        /// </summary>
        public QingqueTile(SuitType suit, int num)
        {
            switch (suit)
            {
                case SuitType.M:
                    this.value = (byte)(num + (byte)QingqueSuit.M);
                    break;
                case SuitType.P:
                    this.value = (byte)(num + (byte)QingqueSuit.P);
                    break;
                case SuitType.S:
                    this.value = (byte)(num + (byte)QingqueSuit.S);
                    break;
                case SuitType.Z:
                    byte[] honours = { 0, Honours.E, Honours.S, Honours.W, Honours.N, Honours.C, Honours.F, Honours.P };
                    this.value = num >= 1 && num <= 7 ? honours[num] : Invalid;
                    break;
                default:
                    this.value = Invalid;
                    break;
            }
        }

        public byte Value => value;

        /// <summary>
        /// Gets the suit of the tile.
        /// </summary>
        public QingqueSuit Suit(bool distinctDragons = false)
        {
            return (QingqueSuit)(value & (byte)(0b11100000 + (distinctDragons ? 1 << 4 : 0)));
        }

        /// <summary>
        /// Gets the number of the tile (1-9 for numbered tiles, 0 for honors).
        /// </summary>
        public byte Num()
        {
            return (value & 0b01000000) != 0 ? (byte)(value & 0b00001111) : (byte)0;
        }

        public bool IsValid => value != Invalid;

        public bool IsHonor => (value & 0b01000000) == 0 && value != Invalid;

        public bool IsNumbered => (value & 0b01000000) != 0;

        public bool IsTerminal => IsNumbered && (Num() == 1 || Num() == 9);

        public bool IsSimple => IsNumbered && Num() >= 2 && Num() <= 8;

        /// <summary>
        /// Gets the suit type of this tile (for compatibility with criterion code).
        /// Converts QingqueSuit to SuitType.
        /// </summary>
        public SuitType GetSuitType()
        {
            var suit = Suit();
            if (suit == QingqueSuit.M) return SuitType.M;
            if (suit == QingqueSuit.P) return SuitType.P;
            if (suit == QingqueSuit.S) return SuitType.S;
            return SuitType.Z; // Z or D
        }

        public bool IsWind => value >= Honours.E && value <= Honours.N;

        public bool IsDragon => value >= Honours.C && value <= Honours.P;

        public static implicit operator byte(QingqueTile tile) => tile.value;

        public static implicit operator QingqueTile(byte value) => new QingqueTile(value);

        public bool Equals(QingqueTile other) => value == other.value;

        public override bool Equals(object obj) => obj is QingqueTile tile && Equals(tile);

        public override int GetHashCode() => value.GetHashCode();

        public static bool operator ==(QingqueTile left, QingqueTile right) => left.Equals(right);

        public static bool operator !=(QingqueTile left, QingqueTile right) => !left.Equals(right);

        public override string ToString()
        {
            if (!IsNumbered)
            {
                return "_ESWNCFP"[value & 0b00000111].ToString();
            }
            else
            {
                char suitChar = "__mp__s"[(int)Suit() >> 5];
                return $"{Num()}{suitChar}";
            }
        }

        /// <summary>
        /// Honor tile constants.
        /// </summary>
        public static class Honours
        {
            public const byte E = 0b10100001;  // East
            public const byte S = 0b10100010;  // South
            public const byte W = 0b10100011;  // West
            public const byte N = 0b10100100;  // North
            public const byte C = 0b10110101;  // Red (中)
            public const byte F = 0b10110110;  // Green (发)
            public const byte P = 0b10110111;  // White (白)
        }

        /// <summary>
        /// Tile literal constructors.
        /// </summary>
        public static class Literals
        {
            public static QingqueTile M(byte num) => new QingqueTile((byte)(num + (byte)QingqueSuit.M));
            public static QingqueTile P(byte num) => new QingqueTile((byte)(num + (byte)QingqueSuit.P));
            public static QingqueTile S(byte num) => new QingqueTile((byte)(num + (byte)QingqueSuit.S));
            
            public static QingqueTile Z(byte num)
            {
                byte[] honours = { 0, Honours.E, Honours.S, Honours.W, Honours.N, Honours.C, Honours.F, Honours.P };
                return new QingqueTile(honours[num]);
            }
        }

        /// <summary>
        /// All 34 unique tiles.
        /// </summary>
        public static readonly QingqueTile[] AllTiles = new QingqueTile[]
        {
            Literals.M(1), Literals.M(2), Literals.M(3), Literals.M(4), Literals.M(5), Literals.M(6), Literals.M(7), Literals.M(8), Literals.M(9),
            Literals.P(1), Literals.P(2), Literals.P(3), Literals.P(4), Literals.P(5), Literals.P(6), Literals.P(7), Literals.P(8), Literals.P(9),
            Literals.S(1), Literals.S(2), Literals.S(3), Literals.S(4), Literals.S(5), Literals.S(6), Literals.S(7), Literals.S(8), Literals.S(9),
            Honours.E, Honours.S, Honours.W, Honours.N, Honours.C, Honours.F, Honours.P
        };

        /// <summary>
        /// All 27 numbered tiles.
        /// </summary>
        public static readonly QingqueTile[] NumberedTiles = new QingqueTile[]
        {
            Literals.M(1), Literals.M(2), Literals.M(3), Literals.M(4), Literals.M(5), Literals.M(6), Literals.M(7), Literals.M(8), Literals.M(9),
            Literals.P(1), Literals.P(2), Literals.P(3), Literals.P(4), Literals.P(5), Literals.P(6), Literals.P(7), Literals.P(8), Literals.P(9),
            Literals.S(1), Literals.S(2), Literals.S(3), Literals.S(4), Literals.S(5), Literals.S(6), Literals.S(7), Literals.S(8), Literals.S(9)
        };

        /// <summary>
        /// Character tiles (1-9m).
        /// </summary>
        public static readonly QingqueTile[] CharacterTiles = new QingqueTile[]
        {
            Literals.M(1), Literals.M(2), Literals.M(3), Literals.M(4), Literals.M(5), Literals.M(6), Literals.M(7), Literals.M(8), Literals.M(9)
        };

        /// <summary>
        /// Dot tiles (1-9p).
        /// </summary>
        public static readonly QingqueTile[] DotTiles = new QingqueTile[]
        {
            Literals.P(1), Literals.P(2), Literals.P(3), Literals.P(4), Literals.P(5), Literals.P(6), Literals.P(7), Literals.P(8), Literals.P(9)
        };

        /// <summary>
        /// Bamboo tiles (1-9s).
        /// </summary>
        public static readonly QingqueTile[] BambooTiles = new QingqueTile[]
        {
            Literals.S(1), Literals.S(2), Literals.S(3), Literals.S(4), Literals.S(5), Literals.S(6), Literals.S(7), Literals.S(8), Literals.S(9)
        };

        /// <summary>
        /// Terminal and honor tiles.
        /// </summary>
        public static readonly QingqueTile[] TerminalHonourTiles = new QingqueTile[]
        {
            Literals.M(1), Literals.M(9), Literals.P(1), Literals.P(9), Literals.S(1), Literals.S(9),
            Honours.E, Honours.S, Honours.W, Honours.N, Honours.C, Honours.F, Honours.P
        };

        /// <summary>
        /// Honor tiles only.
        /// </summary>
        public static readonly QingqueTile[] HonourTiles = new QingqueTile[]
        {
            Honours.E, Honours.S, Honours.W, Honours.N, Honours.C, Honours.F, Honours.P
        };

        /// <summary>
        /// Wind tiles only.
        /// </summary>
        public static readonly QingqueTile[] WindTiles = new QingqueTile[]
        {
            Honours.E, Honours.S, Honours.W, Honours.N
        };

        /// <summary>
        /// Dragon tiles only.
        /// </summary>
        public static readonly QingqueTile[] DragonTiles = new QingqueTile[]
        {
            Honours.C, Honours.F, Honours.P
        };

        /// <summary>
        /// Terminal tiles only (1 and 9 of each suit).
        /// </summary>
        public static readonly QingqueTile[] TerminalTiles = new QingqueTile[]
        {
            Literals.M(1), Literals.M(9), Literals.P(1), Literals.P(9), Literals.S(1), Literals.S(9)
        };

        /// <summary>
        /// Simple tiles (2-8 of each suit).
        /// </summary>
        public static readonly QingqueTile[] SimpleTiles = new QingqueTile[]
        {
            Literals.M(2), Literals.M(3), Literals.M(4), Literals.M(5), Literals.M(6), Literals.M(7), Literals.M(8),
            Literals.P(2), Literals.P(3), Literals.P(4), Literals.P(5), Literals.P(6), Literals.P(7), Literals.P(8),
            Literals.S(2), Literals.S(3), Literals.S(4), Literals.S(5), Literals.S(6), Literals.S(7), Literals.S(8)
        };
    }
}
