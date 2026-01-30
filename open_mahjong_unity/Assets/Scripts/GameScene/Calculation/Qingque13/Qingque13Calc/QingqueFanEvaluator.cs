using System;
using System.Collections.Generic;
using System.Linq;
using Qingque13.Core;
using Qingque13.Criteria;

namespace Qingque13
{
    /// <summary>
    /// Evaluates all fan criteria for a hand.
    /// Matches C++ evaluate_fans function.
    /// </summary>
    public class QingqueFanEvaluator
    {
        private readonly List<IQingqueCriterion> criteria;

        public QingqueFanEvaluator()
        {
            // Register all criteria
            criteria = new List<IQingqueCriterion>
            {
                // Basic
                new TrivialCriterion(),
                
                // Special hands
                new HeavenlyHandCriterion(),
                new EarthlyHandCriterion(),
                new SevenPairsCriterion(),
                new NineGatesCriterion(),
                
                // Concealed patterns
                new ConcealedHandCriterion(),
                new FourConcealedTripletsCriterion(),
                new ThreeConcealedTripletsCriterion(),
                new ConcealedKongCriterion(),
                new TwoConcealedKongsCriterion(),
                new ThreeConcealedKongsCriterion(),
                new FourConcealedKongsCriterion(),
                
                // Kongs
                new TwoKongsCriterion(),
                new ThreeKongsCriterion(),
                new FourKongsCriterion(),
                
                // Triplets and sequences
                new AllTripletsCriterion(),
                new DoubleSequenceCriterion(),
                new TwoDoubleSequencesCriterion(),
                new TripleSequenceCriterion(),
                new QuadrupleSequenceCriterion(),
                
                // Shifted patterns
                new ThreeShiftedSequencesCriterion(),
                new FourShiftedSequencesCriterion(),
                new ThreeShiftedTripletsCriterion(),
                new FourShiftedTripletsCriterion(),
                new MixedShiftedTripletsCriterion(),
                
                // Chained patterns
                new ThreeChainedSequencesCriterion(),
                new FourChainedSequencesCriterion(),
                new PureStraightCriterion(),
                new MixedStraightCriterion(),
                
                // Mixed patterns
                new MixedTripleSequenceCriterion(),
                new MixedTripleTripletCriterion(),
                
                // Flush patterns
                new FullFlushCriterion(),
                new HalfFlushCriterion(),
                
                // Terminal patterns
                new AllTerminalsCriterion(),
                new AllTerminalsAndHonoursCriterion(),
                new PureOutsideHandCriterion(),
                new MixedOutsideHandCriterion(),
                
                // Honour patterns
                new AllHonoursCriterion(),
                new AllTypesCriterion(),
                new BigThreeDragonsCriterion(),
                new LittleThreeDragonsCriterion(),
                new BigFourWindsCriterion(),
                new LittleFourWindsCriterion(),
                new ThreeWindTripletsCriterion(),
                
                // Fan tiles (triplets)
                new FanTile1TCriterion(),
                new FanTile2TCriterion(),
                new FanTile3TCriterion(),
                new FanTile4TCriterion(),
                
                // Fan tiles (pairs)
                new FanTile1PCriterion(),
                new FanTile2PCriterion(),
                new FanTile3PCriterion(),
                new FanTile4PCriterion(),
                new FanTile5PCriterion(),
                new FanTile6PCriterion(),
                new FanTile7PCriterion(),
                
                // Wind pairs (seven pairs)
                new FourWindPairsCriterion(),
                new FourWindPairs2Criterion(),
                new FiveWindPairsCriterion(),
                new SixWindPairsCriterion(),
                new SevenWindPairsCriterion(),
                
                // Dragon pairs (seven pairs)
                new ThreeDragonPairsCriterion(),
                new SixDragonPairsCriterion(),
                
                // Shifted pairs (seven pairs)
                new FourShiftedPairsCriterion(),
                new FiveShiftedPairsCriterion(),
                new SixShiftedPairsCriterion(),
                new SevenShiftedPairsCriterion(),
                
                // Mirrored patterns
                new MirroredHandCriterion(),
                new ReflectedHandCriterion(),
                new ReflectedHand2Criterion(),
                new TwoMirroredPairsCriterion(),
                new ThreeMirroredPairsCriterion(),
                
                // Triple pairs
                new MixedTriplePairCriterion(),
                new TwoTriplePairsCriterion(),
                
                // Double pairs
                new DoublePairCriterion(),
                new TwoDoublePairsCriterion(),
                new ThreeDoublePairsCriterion(),
                
                // Hog patterns
                new EightHogCriterion(),
                new TwelveHogCriterion(),
                
                // Number patterns
                new CommonNumberCriterion(),
                new MixedOneNumberCriterion(),
                new TwoNumbersCriterion(),
                new TwoConsecutiveNumbersCriterion(),
                new ThreeConsecutiveNumbersCriterion(),
                new FourConsecutiveNumbersCriterion(),
                new GappedNumbersCriterion(),
                new ConnectedNumbersCriterion(),
                
                // Win condition fans
                new LastTileDrawCriterion(),
                new LastTileClaimCriterion(),
                new OutWithReplacementTileCriterion(),
                new RobbingTheKongCriterion(),
            };
        }

        /// <summary>
        /// Evaluates all criteria for all decompositions of a hand.
        /// Returns a list of fan sets, one for each decomposition.
        /// </summary>
        public List<HashSet<QingqueFan>> EvaluateFans(QingqueHand hand, bool ignoreOccasional = false)
        {
            var results = new List<HashSet<QingqueFan>>();

            // Evaluate for each decomposition
            foreach (var decomposition in hand.Decompositions)
            {
                var fans = new HashSet<QingqueFan>();

                foreach (var criterion in criteria)
                {
                    // Skip occasional fans if requested
                    if (ignoreOccasional && QingqueFanMetadata.Tags.TryGetValue(criterion.Fan, out var tag))
                    {
                        if (tag.IsOccasional)
                            continue;
                    }

                    // Check if criterion is satisfied
                    if (criterion.Check(decomposition))
                    {
                        fans.Add(criterion.Fan);
                    }
                }

                results.Add(fans);
            }

            return results;
        }

        /// <summary>
        /// Checks if the hand has any valid fans.
        /// Matches C++ has_fan function.
        /// </summary>
        public bool HasFan(QingqueHand hand)
        {
            bool first = true;
            foreach (var criterion in criteria)
            {
                if (first)
                {
                    first = false;
                    continue; // Skip trivial
                }

                // Check against all decompositions
                foreach (var decomposition in hand.Decompositions)
                {
                    if (criterion.Check(decomposition))
                        return true;
                }
            }

            return false;
        }
    }
}
