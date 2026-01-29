using System;
using System.Collections.Generic;
using System.Linq;

namespace Qingque13
{
    /// <summary>
    /// Removes redundant fans based on coverage rules.
    /// Matches C++ derepellenise function.
    /// </summary>
    public static class QingqueDerepellenise
    {
        /// <summary>
        /// Simplifies a fan set by removing covered/redundant fans.
        /// Matches C++ derepellenise function.
        /// </summary>
        public static HashSet<QingqueFan> Derepellenise(HashSet<QingqueFan> fans)
        {
            var result = new HashSet<QingqueFan>(fans);

            // Remove trivial
            result.Remove(QingqueFan.Trivial);

            // Apply coverage rules from C++ lines 1350-1442

            // Heavenly/Earthly/Seven Pairs cover Concealed Hand
            Cover(fans, result, QingqueFan.HeavenlyHand, QingqueFan.ConcealedHand);
            Cover(fans, result, QingqueFan.EarthlyHand, QingqueFan.ConcealedHand);
            Cover(fans, result, QingqueFan.SevenPairs, QingqueFan.ConcealedHand);

            // Concealed Kongs coverage
            Cover(fans, result, QingqueFan.FourConcealedKongs,
                QingqueFan.ThreeConcealedKongs, QingqueFan.TwoConcealedKongs, QingqueFan.ConcealedKong,
                QingqueFan.FourKongs, QingqueFan.ThreeKongs, QingqueFan.TwoKongs,
                QingqueFan.FourConcealedTriplets, QingqueFan.ThreeConcealedTriplets, QingqueFan.AllTriplets,
                QingqueFan.ConcealedHand);
            Cover(fans, result, QingqueFan.ThreeConcealedKongs,
                QingqueFan.TwoConcealedKongs, QingqueFan.ConcealedKong,
                QingqueFan.ThreeKongs, QingqueFan.TwoKongs, QingqueFan.ThreeConcealedTriplets);
            Cover(fans, result, QingqueFan.TwoConcealedKongs, QingqueFan.ConcealedKong, QingqueFan.TwoKongs);
            
            // Kongs coverage
            Cover(fans, result, QingqueFan.FourKongs, QingqueFan.ThreeKongs, QingqueFan.TwoKongs, QingqueFan.AllTriplets);
            Cover(fans, result, QingqueFan.ThreeKongs, QingqueFan.TwoKongs);
            
            // Concealed Triplets coverage
            Cover(fans, result, QingqueFan.FourConcealedTriplets, 
                QingqueFan.ThreeConcealedTriplets, QingqueFan.AllTriplets, QingqueFan.ConcealedHand);

            // Hog coverage
            Cover(fans, result, QingqueFan.TwelveHog, QingqueFan.EightHog);

            // All Honours coverage
            Cover(fans, result, QingqueFan.AllHonours, 
                QingqueFan.HalfFlush, QingqueFan.MixedOneNumber, QingqueFan.AllTerminalsAndHonours, 
                QingqueFan.MixedOutsideHand, QingqueFan.FanTile1P);

            // Wind coverage
            Cover(fans, result, QingqueFan.BigFourWinds, 
                QingqueFan.ThreeWindTriplets, QingqueFan.AllTriplets, QingqueFan.MixedOneNumber, 
                QingqueFan.HalfFlush, QingqueFan.FanTile1T);
            Cover(fans, result, QingqueFan.LittleFourWinds, 
                QingqueFan.ThreeWindTriplets, QingqueFan.HalfFlush, QingqueFan.FanTile1P);
            Cover(fans, result, QingqueFan.FourWindPairs, QingqueFan.FourWindPairs2, QingqueFan.FanTile1P);
            Cover(fans, result, QingqueFan.SevenWindPairs, 
                QingqueFan.SixWindPairs, QingqueFan.FiveWindPairs, QingqueFan.FourWindPairs2, QingqueFan.FourWindPairs,
                QingqueFan.FanTile1P, QingqueFan.AllHonours, QingqueFan.TwelveHog, QingqueFan.ThreeDoublePairs);
            Cover(fans, result, QingqueFan.SixWindPairs, 
                QingqueFan.FiveWindPairs, QingqueFan.FourWindPairs2,
                QingqueFan.HalfFlush, QingqueFan.MixedOneNumber, QingqueFan.EightHog, QingqueFan.TwoDoublePairs);
            Cover(fans, result, QingqueFan.FiveWindPairs, QingqueFan.FourWindPairs2, QingqueFan.DoublePair);

            // Dragon coverage
            Cover(fans, result, QingqueFan.BigThreeDragons, QingqueFan.FanTile3T);
            Cover(fans, result, QingqueFan.LittleThreeDragons, QingqueFan.FanTile2T, QingqueFan.FanTile3P);
            Cover(fans, result, QingqueFan.SixDragonPairs, 
                QingqueFan.ThreeDragonPairs, QingqueFan.FanTile6P,
                QingqueFan.HalfFlush, QingqueFan.MixedOneNumber, QingqueFan.TwelveHog, QingqueFan.ThreeDoublePairs);
            Cover(fans, result, QingqueFan.ThreeDragonPairs, QingqueFan.FanTile3P);

            // Fan tile coverage (triplets)
            Cover(fans, result, QingqueFan.FanTile4T, 
                QingqueFan.FanTile3T, QingqueFan.FanTile2T, QingqueFan.FanTile1T,
                QingqueFan.FanTile4P, QingqueFan.FanTile3P, QingqueFan.FanTile2P, QingqueFan.FanTile1P,
                QingqueFan.HalfFlush, QingqueFan.MixedOneNumber, QingqueFan.AllTriplets);
            Cover(fans, result, QingqueFan.FanTile3T, 
                QingqueFan.FanTile2T, QingqueFan.FanTile1T,
                QingqueFan.FanTile3P, QingqueFan.FanTile2P, QingqueFan.FanTile1P);
            Cover(fans, result, QingqueFan.FanTile2T, QingqueFan.FanTile1T, QingqueFan.FanTile2P, QingqueFan.FanTile1P);
            Cover(fans, result, QingqueFan.FanTile1T, QingqueFan.FanTile1P);

            // Fan tile coverage (pairs)
            Cover(fans, result, QingqueFan.FanTile7P, 
                QingqueFan.FanTile6P, QingqueFan.FanTile5P, QingqueFan.FanTile4P, 
                QingqueFan.FanTile3P, QingqueFan.FanTile2P, QingqueFan.FanTile1P,
                QingqueFan.AllHonours, QingqueFan.TwelveHog, QingqueFan.ThreeDoublePairs, QingqueFan.ThreeDragonPairs);
            Cover(fans, result, QingqueFan.FanTile6P, 
                QingqueFan.FanTile5P, QingqueFan.FanTile4P, QingqueFan.FanTile3P, QingqueFan.FanTile2P, QingqueFan.FanTile1P,
                QingqueFan.HalfFlush, QingqueFan.MixedOneNumber, QingqueFan.EightHog, QingqueFan.TwoDoublePairs);
            Cover(fans, result, QingqueFan.FanTile5P, 
                QingqueFan.FanTile4P, QingqueFan.FanTile3P, QingqueFan.FanTile2P, QingqueFan.FanTile1P, QingqueFan.DoublePair);
            Cover(fans, result, QingqueFan.FanTile4P, 
                QingqueFan.FanTile3P, QingqueFan.FanTile2P, QingqueFan.FanTile1P);
            Cover(fans, result, QingqueFan.FanTile3P, QingqueFan.FanTile2P, QingqueFan.FanTile1P);
            Cover(fans, result, QingqueFan.FanTile2P, QingqueFan.FanTile1P);

            // Terminal coverage
            Cover(fans, result, QingqueFan.AllTerminals, 
                QingqueFan.AllTerminalsAndHonours, QingqueFan.PureOutsideHand, QingqueFan.MixedOutsideHand, QingqueFan.TwoNumbers);
            Cover(fans, result, QingqueFan.AllTerminalsAndHonours, QingqueFan.MixedOutsideHand);
            Cover(fans, result, QingqueFan.PureOutsideHand, QingqueFan.MixedOutsideHand);

            // Flush coverage
            Cover(fans, result, QingqueFan.NineGates, QingqueFan.FullFlush, QingqueFan.ConcealedHand);
            Cover(fans, result, QingqueFan.FullFlush, QingqueFan.HalfFlush);
            Cover(fans, result, QingqueFan.AllTypes, QingqueFan.FanTile1P);

            // Consecutive numbers coverage
            Cover(fans, result, QingqueFan.TwoConsecutiveNumbers, QingqueFan.TwoNumbers);

            // Reflected hand special case
            if (fans.Contains(QingqueFan.ReflectedHand) && 
                fans.Contains(QingqueFan.ReflectedHand2) && 
                fans.Contains(QingqueFan.FullFlush))
            {
                result.Remove(QingqueFan.ReflectedHand2);
            }

            // Sequence coverage
            Cover(fans, result, QingqueFan.QuadrupleSequence, 
                QingqueFan.TripleSequence, QingqueFan.TwoDoubleSequences, QingqueFan.DoubleSequence, QingqueFan.TwelveHog);
            Cover(fans, result, QingqueFan.TripleSequence, QingqueFan.DoubleSequence);
            Cover(fans, result, QingqueFan.TwoDoubleSequences, QingqueFan.DoubleSequence);

            // Shifted patterns coverage
            Cover(fans, result, QingqueFan.FourShiftedTriplets, QingqueFan.ThreeShiftedTriplets, QingqueFan.AllTriplets);
            Cover(fans, result, QingqueFan.FourShiftedSequences, QingqueFan.ThreeShiftedSequences);
            Cover(fans, result, QingqueFan.FourChainedSequences, QingqueFan.ThreeChainedSequences);

            // Shifted pairs coverage
            Cover(fans, result, QingqueFan.SevenShiftedPairs, 
                QingqueFan.SixShiftedPairs, QingqueFan.FiveShiftedPairs, QingqueFan.FourShiftedPairs,
                QingqueFan.FullFlush, QingqueFan.ReflectedHand, QingqueFan.ConnectedNumbers);
            Cover(fans, result, QingqueFan.SixShiftedPairs, 
                QingqueFan.FiveShiftedPairs, QingqueFan.FourShiftedPairs);
            Cover(fans, result, QingqueFan.FiveShiftedPairs, QingqueFan.FourShiftedPairs);

            // Triple pairs coverage
            Cover(fans, result, QingqueFan.TwoTriplePairs, QingqueFan.MixedTriplePair, QingqueFan.TwoMirroredPairs);

            // Mirrored pairs coverage
            Cover(fans, result, QingqueFan.ThreeMirroredPairs, QingqueFan.TwoMirroredPairs);

            // Consecutive numbers coverage
            Cover(fans, result, QingqueFan.TwoConsecutiveNumbers, 
                QingqueFan.ThreeConsecutiveNumbers, QingqueFan.FourConsecutiveNumbers);
            Cover(fans, result, QingqueFan.ThreeConsecutiveNumbers, QingqueFan.FourConsecutiveNumbers);

            // Double pairs coverage
            Cover(fans, result, QingqueFan.ThreeDoublePairs, 
                QingqueFan.TwoDoublePairs, QingqueFan.DoublePair, QingqueFan.TwelveHog, QingqueFan.EightHog);
            Cover(fans, result, QingqueFan.TwoDoublePairs, QingqueFan.DoublePair, QingqueFan.EightHog);

            return result;
        }

        /// <summary>
        /// If the covering fan is present, remove all covered fans.
        /// </summary>
        private static void Cover(HashSet<QingqueFan> fansReference, HashSet<QingqueFan> fans, QingqueFan coveringFan, params QingqueFan[] coveredFans)
        {
            if (fansReference.Contains(coveringFan))
            {
                foreach (var covered in coveredFans)
                {
                    fans.Remove(covered);
                }
            }
        }

        /// <summary>
        /// Derepellenises a list of fan sets.
        /// </summary>
        public static List<HashSet<QingqueFan>> Derepellenise(List<HashSet<QingqueFan>> fansList)
        {
            return fansList.Select(Derepellenise).ToList();
        }
    }
}
