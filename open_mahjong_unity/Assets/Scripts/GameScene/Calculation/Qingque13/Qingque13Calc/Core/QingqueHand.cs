using System;
using System.Collections.Generic;
using System.Linq;

namespace Qingque13.Core
{
    /// <summary>
    /// Represents a complete mahjong hand with automatic decomposition.
    /// Matches C++ hand class.
    /// </summary>
    public class QingqueHand
    {
        private QingqueWinType winType;
        private QingqueTile winTile;
        private List<QingqueMeld> openMelds;
        private QingqueTileCounter closedCounter;
        private QingqueTileCounter totalCounter;
        private List<QingqueDecomposition> decompositions;

        public QingqueHand(
            IEnumerable<QingqueTile> tiles,
            IEnumerable<QingqueMeld> melds,
            QingqueTile winningTile,
            QingqueWinType winningType = default,
            bool winningTileIncluded = false)
        {
            this.openMelds = new List<QingqueMeld>(melds);
            this.closedCounter = new QingqueTileCounter(tiles);
            this.totalCounter = new QingqueTileCounter(tiles, melds);
            this.winType = winningType;
            this.winTile = winningTile;

            if (!winningTileIncluded)
            {
                closedCounter.Add(winTile);
                totalCounter.Add(winTile);
            }

            DecomposeInit();
        }

        public QingqueWinType WinningType => winType;
        public QingqueTile WinningTile => winTile;
        public List<QingqueMeld> OpenMelds => openMelds;
        public QingqueTileCounter ClosedCounter => closedCounter;
        public QingqueTileCounter TotalCounter => totalCounter;
        public List<QingqueDecomposition> Decompositions => decompositions;

        /// <summary>
        /// Returns true if any decomposition is a seven pairs hand.
        /// </summary>
        public bool IsSevenPairs => decompositions?.Any(d => d.IsSevenPairs) ?? false;

        public QingqueTileCounter Counter(bool includeOpenMelds = true)
        {
            return includeOpenMelds ? totalCounter : closedCounter;
        }

        public bool IsValid(bool checkFifthTile = true)
        {
            // Must have 14 tiles total (closed + open melds)
            if (closedCounter.Count() + openMelds.Count * 3 != 14)
                return false;

            // Winning tile must be in hand
            if (closedCounter.Count(winTile) == 0)
                return false;

            // Check for kongs
            byte kongCount = 0;
            foreach (var meld in openMelds)
            {
                if (meld.Type == QingqueMeldType.Kong)
                    kongCount++;
            }

            // Total tiles minus kongs should be 14
            if (totalCounter.Count() - kongCount != 14)
                return false;

            // Check for invalid tile counts (max 4 per tile)
            if (checkFifthTile)
            {
                foreach (var tile in QingqueTile.AllTiles)
                {
                    if (totalCounter.Count(tile) > 4)
                        return false;
                }
            }

            return true;
        }

        public void SetWinningType(QingqueWinType type)
        {
            winType = type;
            
            // Update concealed flags on triplets in decompositions
            foreach (var decomp in decompositions)
            {
                for (int i = 0; i < decomp.Melds.Count; i++)
                {
                    var meld = decomp.Melds[i];
                    if (meld.Type != QingqueMeldType.Triplet) continue;
                    if (meld.Fixed) continue;

                    bool concealed = winType.IsSelfDrawn || 
                                   meld.Tile != winTile || 
                                   closedCounter.Count(meld.Tile) == 4;

                    decomp.Melds[i] = new QingqueMeld(meld.Tile, QingqueMeldType.Triplet, concealed, false);
                }
            }
        }

        /// <summary>
        /// Decomposes the hand into all possible 4 melds + 1 pair combinations.
        /// Also checks for seven pairs decomposition.
        /// Implements the C++ decompose_init algorithm.
        /// </summary>
        private void DecomposeInit()
        {
            decompositions = new List<QingqueDecomposition>();
            
            // Check for seven pairs first
            CheckSevenPairs();
            
            var queue = new Queue<(QingqueDecomposition decomp, byte index)>();
            queue.Enqueue((new QingqueDecomposition(this), 0));

            while (queue.Count > 0)
            {
                var (front, index) = queue.Dequeue();
                var counter = front.Counter(false);

                // If we have 4 melds, find the pair
                if (front.Melds.Count == 4)
                {
                    // Must have exactly 2 tiles left (the pair)
                    if (counter.Count() == 2)
                    {
                        foreach (var tile in QingqueTile.AllTiles)
                        {
                            if (counter.Count(tile) == 2)
                            {
                                decompositions.Add(new QingqueDecomposition(front, tile));
                                break;
                            }
                        }
                    }
                    continue;
                }

                // Try to form triplets
                for (byte i = (byte)((index + 1) / 2); i < 34; i++)
                {
                    var tile = QingqueTile.AllTiles[i];
                    if (counter.Count(tile) >= 3)
                    {
                        bool concealed = winType.IsSelfDrawn || 
                                       tile != winTile || 
                                       closedCounter.Count(tile) == 4;
                        var triplet = new QingqueMeld(tile, QingqueMeldType.Triplet, concealed, false);
                        queue.Enqueue((new QingqueDecomposition(front, triplet), (byte)(i * 2)));
                    }
                }

                // Try to form sequences (only for numbered tiles)
                for (byte i = (byte)(index / 2); i < 27; i++)
                {
                    var tile = QingqueTile.NumberedTiles[i];
                    var tile1 = new QingqueTile((byte)(tile.Value - 1));
                    var tile2 = new QingqueTile((byte)(tile.Value + 1));
                    
                    if (counter.Count(tile) > 0 && 
                        counter.Count(tile1) > 0 && 
                        counter.Count(tile2) > 0)
                    {
                        var sequence = new QingqueMeld(tile, QingqueMeldType.Sequence, false, false);
                        queue.Enqueue((new QingqueDecomposition(front, sequence), (byte)(i * 2 + 1)));
                    }
                }
            }
        }
        
        /// <summary>
        /// Checks if the hand is seven pairs (all closed, 7 different pairs).
        /// C++ ref: verifier is_seven_pairs
        /// </summary>
        private void CheckSevenPairs()
        {
            // Seven pairs must have all closed melds (no chows/pons/kongs)
            if (openMelds.Count > 0) return;
            
            // closedCounter already includes winning tile (added in constructor)
            // Must have exactly 14 tiles
            if (closedCounter.Count() != 14) return;
            
            // All tiles must appear an even number of times (pairs)
            // C++: for (tile_t ti : tile_set::all_tiles) if (h.counter().count(ti) & 1) return false;
            var pairs = new List<QingqueTile>();
            foreach (var tile in QingqueTile.AllTiles)
            {
                byte count = closedCounter.Count(tile);
                if ((count & 1) != 0) return; // Must be even (0, 2, or 4)
                
                if (count == 2) pairs.Add(tile);
                else if (count == 4)
                {
                    pairs.Add(tile);
                    pairs.Add(tile); // Double pair
                }
            }
            
            // Must have exactly 7 pairs
            if (pairs.Count == 7)
            {
                decompositions.Add(new QingqueDecomposition(this, pairs));
            }
        }

        public override string ToString()
        {
            var parts = new List<string>();
            
            // Open melds
            foreach (var meld in openMelds)
            {
                parts.Add(meld.ToString());
            }

            // Closed tiles (excluding winning tile)
            var closedTiles = closedCounter.GetTiles(true);
            var tilesExceptWinning = closedTiles.Where(t => t != winTile || closedTiles.Count(x => x == t) > 1).ToList();
            
            if (tilesExceptWinning.Count > 0)
            {
                parts.Add(new QingqueTileCounter(tilesExceptWinning).ToString());
            }

            // Winning tile
            parts.Add($"[{winTile}]");

            return string.Join(" ", parts);
        }
    }
}
