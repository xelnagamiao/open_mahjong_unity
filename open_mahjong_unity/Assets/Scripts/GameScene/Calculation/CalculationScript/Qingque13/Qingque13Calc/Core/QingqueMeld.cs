using System;
using System.Collections.Generic;

namespace Qingque13.Core
{
    /// <summary>
    /// Represents a meld type.
    /// Matches C++ enum meld_type.
    /// </summary>
    public enum QingqueMeldType : ushort
    {
        Sequence = 0b0000000000,  // Chi (顺子)
        Triplet = 0b0100000000,   // Pon/Pung (刻子)
        Kong = 0b1100000000        // Kong/Kan (杠)
    }

    /// <summary>
    /// Represents a meld (group of tiles).
    /// Matches C++ meld_t (uint16_t) and meld class.
    /// Format: (nothing)0000 (fixed)0 (concealed)0 (type)00 (tile)00000000
    /// </summary>
    public struct QingqueMeld : IEquatable<QingqueMeld>
    {
        public const ushort Invalid = 0;

        private readonly ushort value;

        public QingqueMeld(ushort value)
        {
            this.value = value;
        }

        public QingqueMeld(QingqueTile tile, QingqueMeldType type, bool concealed = true, bool fixedMeld = true)
        {
            // Platform-specific: explicit ushort conversion for bitwise operations to ensure correct behavior in WebGL/IL2CPP
            ushort tileValue = tile.Value;
            ushort typeValue = (ushort)type;
            ushort concealedBit = concealed ? (ushort)(1 << 10) : (ushort)0;
            ushort fixedBit = fixedMeld ? (ushort)(1 << 11) : (ushort)0;
            this.value = (ushort)(tileValue | typeValue | concealedBit | fixedBit);
        }

        public ushort Value => value;

        /// <summary>
        /// Gets the base tile of this meld.
        /// For sequences, this is the middle tile (e.g., 2 in 123).
        /// </summary>
        public QingqueTile Tile => new QingqueTile((byte)(value & 0b0000000011111111));

        /// <summary>
        /// Gets the meld type.
        /// </summary>
        public QingqueMeldType Type => (QingqueMeldType)(value & 0b0000001100000000);

        /// <summary>
        /// Returns true if the meld is concealed (not called from another player).
        /// </summary>
        public bool Concealed => (value & 0b0000010000000000) != 0;

        /// <summary>
        /// Returns true if the meld is fixed (open/called).
        /// </summary>
        public bool Fixed => (value & 0b0000100000000000) != 0;

        public bool IsValid => value != Invalid;

        public static implicit operator ushort(QingqueMeld meld) => meld.value;

        public static implicit operator QingqueMeld(ushort value) => new QingqueMeld(value);

        public bool Equals(QingqueMeld other) => value == other.value;

        public override bool Equals(object obj) => obj is QingqueMeld meld && Equals(meld);

        public override int GetHashCode() => value.GetHashCode();

        public static bool operator ==(QingqueMeld left, QingqueMeld right) => left.Equals(right);

        public static bool operator !=(QingqueMeld left, QingqueMeld right) => !left.Equals(right);

        /// <summary>
        /// Checks if this meld contains the specified tile.
        /// </summary>
        public bool Contains(QingqueTile tile)
        {
            if (Tile == tile)
                return true;

            if (Type == QingqueMeldType.Sequence)
            {
                return ((int)Tile.Value + 1 == (int)tile.Value || (int)Tile.Value - 1 == (int)tile.Value);
            }

            return false;
        }

        /// <summary>
        /// Checks if this meld contains any of the specified tiles.
        /// </summary>
        public bool Contains(IEnumerable<QingqueTile> tiles)
        {
            foreach (var tile in tiles)
            {
                if (Contains(tile))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets all tiles in this meld.
        /// </summary>
        public QingqueTile[] GetTiles()
        {
            switch (Type)
            {
                case QingqueMeldType.Sequence:
                    // Platform-specific: use int arithmetic then convert to byte to prevent underflow/overflow in WebGL
                    {
                        int baseValue = (int)Tile.Value;
                        return new QingqueTile[] 
                        { 
                            new QingqueTile((byte)(baseValue - 1)), 
                            Tile, 
                            new QingqueTile((byte)(baseValue + 1)) 
                        };
                    }
                
                case QingqueMeldType.Triplet:
                    return new QingqueTile[] { Tile, Tile, Tile };
                
                case QingqueMeldType.Kong:
                    return new QingqueTile[] { Tile, Tile, Tile, Tile };
                
                default:
                    return new QingqueTile[0];
            }
        }

        public override string ToString()
        {
            switch (Type)
            {
                case QingqueMeldType.Sequence:
                    {
                        // Platform-specific: use int arithmetic to prevent byte underflow in WebGL
                        int num = (int)Tile.Num();
                        int suitIndex = (int)Tile.Suit() >> 5;
                        char suit = "__mp__s"[suitIndex];
                        return $"{num - 1}{num}{num + 1}{suit}";
                    }

                case QingqueMeldType.Triplet:
                    if (Tile.IsHonor)
                        return $"{Tile}{Tile}{Tile}";
                    else
                        return $"{Tile.Num()}{Tile.Num()}{Tile.Num()}{Tile.ToString()[1]}";

                case QingqueMeldType.Kong:
                    if (Tile.IsHonor)
                        return $"{Tile}{Tile}{Tile}{Tile}";
                    else
                        return $"{Tile.Num()}{Tile.Num()}{Tile.Num()}{Tile.Num()}{Tile.ToString()[1]}";

                default:
                    return "Invalid";
            }
        }

        /// <summary>
        /// Helper to create a sequence meld.
        /// </summary>
        public static QingqueMeld Sequence(QingqueTile tile, bool concealed = false, bool fixedMeld = false)
        {
            return new QingqueMeld(tile, QingqueMeldType.Sequence, concealed, fixedMeld);
        }

        /// <summary>
        /// Helper to create a triplet meld.
        /// </summary>
        public static QingqueMeld Triplet(QingqueTile tile, bool concealed = true, bool fixedMeld = false)
        {
            return new QingqueMeld(tile, QingqueMeldType.Triplet, concealed, fixedMeld);
        }

        /// <summary>
        /// Helper to create a kong meld.
        /// </summary>
        public static QingqueMeld Kong(QingqueTile tile, bool concealed = true, bool fixedMeld = false)
        {
            return new QingqueMeld(tile, QingqueMeldType.Kong, concealed, fixedMeld);
        }
    }
}
