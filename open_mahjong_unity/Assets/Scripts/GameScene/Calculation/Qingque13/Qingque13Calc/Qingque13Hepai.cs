using System;
using System.Collections.Generic;
using System.Linq;
using Qingque13.Core;

namespace Qingque13
{
    /// <summary>
    /// Main entry point for Qingque 13-tile mahjong calculation.
    /// Matches the interface pattern of GBhepai.cs.
    /// </summary>
    public static class Qingque13Hepai
    {
        private static void Log(string message)
        {
#if UNITY_2017_1_OR_NEWER
            UnityEngine.Debug.Log(message);
#else
            Console.WriteLine(message);
#endif
        }

        private static void LogError(string message)
        {
#if UNITY_2017_1_OR_NEWER
            UnityEngine.Debug.LogError(message);
#else
            Console.WriteLine($"ERROR: {message}");
#endif
        }

        public static int GetBasePoint(double fan)
        {
            return (int)Math.Round(fan * fan);
        }

        /// <summary>
        /// Checks if a hand wins and calculates the fan score.
        /// </summary>
        /// <param name="hand_list">Hand tiles (list of tile IDs)</param>
        /// <param name="tiles_combination">Open meld combinations (e.g., "k11", "s12", "g45")</param>
        /// <param name="way_to_hepai">Win conditions (e.g., "zimo", "gangshangkaihua")</param>
        /// <param name="get_tile">Winning tile ID</param>
        /// <param name="debug">Enable debug logging</param>
        /// <returns>Tuple of (fan score, list of fan names)</returns>
        public static Tuple<double, List<string>> HepaiCheck(
            List<int> hand_list,
            List<string> tiles_combination,
            List<string> way_to_hepai,
            int get_tile,
            bool debug = false)
        {
            try
            {
                // Convert input to Qingque types
                var tiles = ConvertTiles(hand_list);
                var melds = ConvertMelds(tiles_combination);
                var winType = ConvertWinType(way_to_hepai);
                var winTile = ConvertTile(get_tile);

                if (debug)
                {
                    Log($"[Qingque13] Hand: {string.Join(",", hand_list)}");
                    Log($"[Qingque13] Melds: {string.Join(",", tiles_combination)}");
                    Log($"[Qingque13] Win type: {winType}");
                    Log($"[Qingque13] Win tile: {winTile}");
                }

                // Create hand
                var hand = new QingqueHand(tiles, melds, winTile, winType, winningTileIncluded: false);

                if (debug)
                {
                    Log($"[Qingque13] Hand valid: {hand.IsValid()}");
                    Log($"[Qingque13] Decompositions: {hand.Decompositions.Count}");
                    for (int i = 0; i < hand.Decompositions.Count; i++)
                    {
                        Log($"[Qingque13]   Decomp {i}: {hand.Decompositions[i]}");
                    }
                }

                // Validate hand
                if (!hand.IsValid())
                {
                    return Tuple.Create(0.0, new List<string> { "Invalid hand" });
                }

                // Evaluate fans
                var evaluator = new QingqueFanEvaluator();
                var allFans = evaluator.EvaluateFans(hand, ignoreOccasional: false);

                if (debug)
                {
                    Log($"[Qingque13] Fan sets: {allFans.Count}");
                    for (int i = 0; i < allFans.Count; i++)
                    {
                        Log($"[Qingque13]   Set {i}: {string.Join(", ", allFans[i])}");
                    }
                }

                // Calculate max fan score using ORIGINAL fans (with Trivial) for cache lookup
                var (fanScore, bestFans) = QingqueScoring.GetMaxFan(allFans);

                if (debug)
                {
                    Log($"[Qingque13] Best fan score: {fanScore}");
                    Log($"[Qingque13] Best fans (raw): {string.Join(", ", bestFans)}");
                }

                // Derepellenise only for DISPLAY (after scoring)
                var displayFans = QingqueDerepellenise.Derepellenise(bestFans);

                if (debug)
                {
                    Log($"[Qingque13] Best fans (display): {string.Join(", ", displayFans)}");
                }

                // Convert fans to Chinese names
                var fanNames = displayFans
                    .Select(f => QingqueFanMetadata.ChineseNames.TryGetValue(f, out var name) ? name : f.ToString())
                    .ToList();

                return Tuple.Create(fanScore, fanNames);
            }
            catch (Exception ex)
            {
                if (debug)
                {
                    LogError($"[Qingque13] Error: {ex.Message}\n{ex.StackTrace}");
                }
                return Tuple.Create(0.0, new List<string> { $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Converts tile IDs (11-47) to QingqueTile objects.
        /// </summary>
        private static List<QingqueTile> ConvertTiles(List<int> tileIds)
        {
            var tiles = new List<QingqueTile>();
            foreach (var id in tileIds)
            {
                tiles.Add(ConvertTile(id));
            }
            return tiles;
        }

        /// <summary>
        /// Converts a single tile ID to QingqueTile.
        /// Format: 11-19 (万), 21-29 (饼), 31-39 (条), 41-47 (字)
        /// </summary>
        private static QingqueTile ConvertTile(int tileId)
        {
            int suit = tileId / 10;
            int num = tileId % 10;

            return suit switch
            {
                1 => QingqueTile.Literals.M((byte)num),
                2 => QingqueTile.Literals.P((byte)num),
                3 => QingqueTile.Literals.S((byte)num),
                4 => QingqueTile.Literals.Z((byte)num),
                _ => new QingqueTile(0)
            };
        }

        /// <summary>
        /// Converts meld strings to QingqueMeld objects.
        /// Format: "k11" (triplet), "s12" (sequence), "g45" (kong), etc.
        /// </summary>
        private static List<QingqueMeld> ConvertMelds(List<string> meldStrings)
        {
            var melds = new List<QingqueMeld>();
            
            foreach (var str in meldStrings)
            {
                if (str.Length < 2) continue;

                char type = str[0];
                int tileId = int.Parse(str.Substring(1));
                var tile = ConvertTile(tileId);

                bool isConcealed = char.IsUpper(type);

                switch (char.ToLower(type))
                {
                    case 's': // Sequence
                        melds.Add(QingqueMeld.Sequence(tile, isConcealed, !isConcealed));
                        break;
                    case 'k': // Triplet
                        melds.Add(QingqueMeld.Triplet(tile, isConcealed, !isConcealed));
                        break;
                    case 'g': // Kong
                        melds.Add(QingqueMeld.Kong(tile, isConcealed, !isConcealed));
                        break;
                }
            }

            return melds;
        }

        /// <summary>
        /// Converts win condition strings to QingqueWinType.
        /// Supports both Chinese and pinyin strings.
        /// </summary>
        private static QingqueWinType ConvertWinType(List<string> wayToHepai)
        {
            bool selfDrawn = wayToHepai.Any(w => 
                w == "自摸" || w == "海底捞月" ||
                w == "岭上开花" || w == "天和");
            bool finalTile = wayToHepai.Any(w => 
                w == "海底捞月" || w == "河底捞鱼");
                bool kongRelated = wayToHepai.Any(w => 
                w == "岭上开花" || w == "抢杠");
            bool heavenlyOrEarthly = wayToHepai.Any(w => 
                w == "天和" || w == "地和");

            // Parse seat wind (自风)
            QingqueTile seatWind = default;
            foreach (var w in wayToHepai)
            {
                if (w.Contains("自风东")) seatWind = QingqueTile.Honours.E;
                else if (w.Contains("自风南")) seatWind = QingqueTile.Honours.S;
                else if (w.Contains("自风西")) seatWind = QingqueTile.Honours.W;
                else if (w.Contains("自风北")) seatWind = QingqueTile.Honours.N;
            }

            // Parse prevalent wind (场风/圈风)
            QingqueTile prevalentWind = default;
            foreach (var w in wayToHepai)
            {
                if (w.Contains("场风东") || w.Contains("圈风东")) prevalentWind = QingqueTile.Honours.E;
                else if (w.Contains("场风南") || w.Contains("圈风南")) prevalentWind = QingqueTile.Honours.S;
                else if (w.Contains("场风西") || w.Contains("圈风西")) prevalentWind = QingqueTile.Honours.W;
                else if (w.Contains("场风北") || w.Contains("圈风北")) prevalentWind = QingqueTile.Honours.N;
            }

            return new QingqueWinType(
                selfDrawn: selfDrawn,
                finalTile: finalTile,
                kongRelated: kongRelated,
                heavenlyOrEarthlyHand: heavenlyOrEarthly,
                seatWind: seatWind,
                prevalentWind: prevalentWind
            );
        }
    }
}
