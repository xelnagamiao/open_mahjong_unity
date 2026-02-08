using System;
using System.Collections.Generic;
using System.Linq;

namespace Qingque13.Core
{
    /// <summary>
    /// Efficiently counts tiles using bitwise operations.
    /// Matches C++ tile_counter class.
    /// Uses two ulongs: one for m/p suits, one for s/z suits.
    /// 3 bits per tile (can count 0-7 of each tile).
    /// </summary>
    public struct QingqueTileCounter : IEquatable<QingqueTileCounter>
    {
        private ulong mpCount;  // m bits 3-29, p bits 35-61
        private ulong szCount;  // s bits 3-29, z bits 35-55

        public QingqueTileCounter(ulong mpCount, ulong szCount)
        {
            this.mpCount = mpCount;
            this.szCount = szCount;
        }

        public QingqueTileCounter(IEnumerable<QingqueTile> tiles)
        {
            mpCount = 0;
            szCount = 0;
            foreach (var tile in tiles)
            {
                Add(tile);
            }
        }

        public QingqueTileCounter(IEnumerable<QingqueTile> tiles, IEnumerable<QingqueMeld> melds)
        {
            mpCount = 0;
            szCount = 0;
            foreach (var tile in tiles)
            {
                Add(tile);
            }
            foreach (var meld in melds)
            {
                Add(meld);
            }
        }

        /// <summary>
        /// Adds a tile to the counter.
        /// </summary>
        public void Add(QingqueTile tile, int count = 1)
        {
            if (tile.Value == 0) return;

            if ((tile.Value & 0b10000000) != 0)
            {
                // s or z suit
                int shift = (int)(tile.Value & 0b00100000) + (int)(3 * (tile.Value & 0b00001111));
                szCount += (ulong)count << shift;
            }
            else
            {
                // m or p suit
                int shift = (int)(tile.Value & 0b00100000) + (int)(3 * (tile.Value & 0b00001111));
                mpCount += (ulong)count << shift;
            }
        }

        /// <summary>
        /// Adds a meld to the counter.
        /// </summary>
        public void Add(QingqueMeld meld, int count = 1)
        {
            switch (meld.Type)
            {
                case QingqueMeldType.Sequence:
                    // For sequence, the meld.Tile is the MIDDLE tile
                    // So for 123, meld.Tile is 2, and we need tiles 1, 2, 3
                    Add(new QingqueTile((byte)(meld.Tile.Value - 1)), count);
                    Add(meld.Tile, count);
                    Add(new QingqueTile((byte)(meld.Tile.Value + 1)), count);
                    break;
                case QingqueMeldType.Triplet:
                    Add(meld.Tile, count * 3);
                    break;
                case QingqueMeldType.Kong:
                    Add(meld.Tile, count * 4);
                    break;
            }
        }

        /// <summary>
        /// Counts how many of a specific tile are in the counter.
        /// </summary>
        public byte Count(QingqueTile tile)
        {
            if (tile.Value == 0) return 0;

            if ((tile.Value & 0b10000000) != 0)
            {
                // s or z suit
                int shift = (int)(tile.Value & 0b00100000) + (int)(3 * (tile.Value & 0b00001111));
                return (byte)((szCount >> shift) & 0b111);
            }
            else
            {
                // m or p suit
                int shift = (int)(tile.Value & 0b00100000) + (int)(3 * (tile.Value & 0b00001111));
                return (byte)((mpCount >> shift) & 0b111);
            }
        }

        /// <summary>
        /// Counts total number of tiles.
        /// </summary>
        public byte Count()
        {
            byte total = 0;
            foreach (var tile in QingqueTile.AllTiles)
            {
                total += Count(tile);
            }
            return total;
        }

        /// <summary>
        /// Counts tiles in a collection.
        /// </summary>
        public byte Count(IEnumerable<QingqueTile> tiles)
        {
            byte total = 0;
            foreach (var tile in tiles)
            {
                total += Count(tile);
            }
            return total;
        }

        /// <summary>
        /// Gets all tiles (with duplicates if duplicate=true).
        /// </summary>
        public List<QingqueTile> GetTiles(bool duplicate = true)
        {
            var result = new List<QingqueTile>();
            if (duplicate)
            {
                foreach (var tile in QingqueTile.AllTiles)
                {
                    byte count = Count(tile);
                    for (int i = 0; i < count; i++)
                    {
                        result.Add(tile);
                    }
                }
            }
            else
            {
                foreach (var tile in QingqueTile.AllTiles)
                {
                    if (Count(tile) > 0)
                    {
                        result.Add(tile);
                    }
                }
            }
            return result;
        }

        public ulong MpCount => mpCount;
        public ulong SzCount => szCount;

        public bool IsEmpty => mpCount == 0 && szCount == 0;

        public static QingqueTileCounter operator +(QingqueTileCounter a, QingqueTileCounter b)
        {
            return new QingqueTileCounter(a.mpCount + b.mpCount, a.szCount + b.szCount);
        }

        public static QingqueTileCounter operator +(QingqueTileCounter counter, QingqueTile tile)
        {
            var result = counter;
            result.Add(tile);
            return result;
        }

        public static QingqueTileCounter operator +(QingqueTileCounter counter, QingqueMeld meld)
        {
            var result = counter;
            result.Add(meld);
            return result;
        }

        public static QingqueTileCounter operator -(QingqueTileCounter a, QingqueTileCounter b)
        {
            // Ensure no underflow in ulong subtraction (platform-specific for WebGL/IL2CPP)
            ulong mpResult = a.mpCount >= b.mpCount ? a.mpCount - b.mpCount : 0;
            ulong szResult = a.szCount >= b.szCount ? a.szCount - b.szCount : 0;
            return new QingqueTileCounter(mpResult, szResult);
        }

        public static QingqueTileCounter operator -(QingqueTileCounter counter, QingqueTile tile)
        {
            var result = counter;
            result.Add(tile, -1);
            return result;
        }

        public static QingqueTileCounter operator -(QingqueTileCounter counter, QingqueMeld meld)
        {
            var result = counter;
            result.Add(meld, -1);
            return result;
        }

        public static bool operator <=(QingqueTileCounter a, QingqueTileCounter b)
        {
            foreach (var tile in QingqueTile.AllTiles)
            {
                if (a.Count(tile) > b.Count(tile))
                    return false;
            }
            return true;
        }

        public static bool operator >=(QingqueTileCounter a, QingqueTileCounter b)
        {
            foreach (var tile in QingqueTile.AllTiles)
            {
                if (a.Count(tile) < b.Count(tile))
                    return false;
            }
            return true;
        }

        public bool Equals(QingqueTileCounter other)
        {
            return mpCount == other.mpCount && szCount == other.szCount;
        }

        public override bool Equals(object obj)
        {
            return obj is QingqueTileCounter counter && Equals(counter);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(mpCount, szCount);
        }

        public static bool operator ==(QingqueTileCounter left, QingqueTileCounter right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(QingqueTileCounter left, QingqueTileCounter right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            var tiles = GetTiles(true);
            if (tiles.Count == 0) return "Empty";

            var groups = new System.Text.StringBuilder();
            var currentSuit = QingqueSuit.M;
            var nums = new List<byte>();

            foreach (var tile in tiles)
            {
                if (tile.IsHonor)
                {
                    if (nums.Count > 0)
                    {
                        groups.Append(string.Join("", nums));
                        groups.Append("__mp__s"[(int)currentSuit >> 5]);
                        nums.Clear();
                    }
                    groups.Append(tile);
                }
                else
                {
                    if (tile.Suit() != currentSuit && nums.Count > 0)
                    {
                        groups.Append(string.Join("", nums));
                        groups.Append("__mp__s"[(int)currentSuit >> 5]);
                        nums.Clear();
                    }
                    currentSuit = tile.Suit();
                    nums.Add(tile.Num());
                }
            }

            if (nums.Count > 0)
            {
                groups.Append(string.Join("", nums));
                groups.Append("__mp__s"[(int)currentSuit >> 5]);
            }

            return groups.ToString();
        }
    }
}
