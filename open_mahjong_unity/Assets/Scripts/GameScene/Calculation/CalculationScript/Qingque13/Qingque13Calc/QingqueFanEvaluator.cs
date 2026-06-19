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
            // Register all criteria in enum order (matching QingqueFan.cs)
            criteria = new List<IQingqueCriterion>
            {
                new TrivialCriterion(),
                new HeavenlyHandCriterion(),
                new EarthlyHandCriterion(),
                new OutWithReplacementTileCriterion(),
                new LastTileDrawCriterion(),
                new LastTileClaimCriterion(),
                new RobbingTheKongCriterion(),
                new ThirteenOrphansCriterion(),
                new SevenPairsCriterion(),
                new ConcealedHandCriterion(),
                new FourConcealedKongsCriterion(),
                new ThreeConcealedKongsCriterion(),
                new TwoConcealedKongsCriterion(),
                new ConcealedKongCriterion(),
                new FourKongsCriterion(),
                new ThreeKongsCriterion(),
                new TwoKongsCriterion(),
                new FourConcealedTripletsCriterion(),
                new ThreeConcealedTripletsCriterion(),
                new AllTripletsCriterion(),
                new TwelveHogCriterion(),
                new EightHogCriterion(),
                new ThreeDoublePairsCriterion(),
                new TwoDoublePairsCriterion(),
                new DoublePairCriterion(),
                new AllHonoursCriterion(),
                new BigFourWindsCriterion(),
                new LittleFourWindsCriterion(),
                new FourWindPairsCriterion(),
                new ThreeWindTripletsCriterion(),
                new SevenWindPairsCriterion(),
                new SixWindPairsCriterion(),
                new BigThreeDragonsCriterion(),
                new LittleThreeDragonsCriterion(),
                new SixDragonPairsCriterion(),
                new ThreeDragonPairsCriterion(),
                new FanTile4TCriterion(),
                new FanTile3TCriterion(),
                new FanTile2TCriterion(),
                new FanTile1TCriterion(),
                new FanTile7PCriterion(),
                new FanTile6PCriterion(),
                new FanTile5PCriterion(),
                new FanTile4PCriterion(),
                new FanTile3PCriterion(),
                new FanTile2PCriterion(),
                new FanTile1PCriterion(),
                new AllTerminalsCriterion(),
                new AllTerminalsAndHonoursCriterion(),
                new PureOutsideHandCriterion(),
                new MixedOutsideHandCriterion(),
                new NineGatesCriterion(),
                new FullFlushCriterion(),
                new HalfFlushCriterion(),
                new AllTypesCriterion(),
                new MixedOneNumberCriterion(),
                new TwoNumbersCriterion(),
                new TwoConsecutiveNumbersCriterion(),
                new ThreeConsecutiveNumbersCriterion(),
                new FourConsecutiveNumbersCriterion(),
                new ConnectedNumbersCriterion(),
                new GappedNumbersCriterion(),
                new ReflectedHandCriterion(),
                new ReflectedHand2Criterion(),
                new CommonNumberCriterion(),
                new QuadrupleSequenceCriterion(),
                new TripleSequenceCriterion(),
                new TwoDoubleSequencesCriterion(),
                new DoubleSequenceCriterion(),
                new FourShiftedTripletsCriterion(),
                new ThreeShiftedTripletsCriterion(),
                new FourShiftedSequencesCriterion(),
                new ThreeShiftedSequencesCriterion(),
                new FourChainedSequencesCriterion(),
                new ThreeChainedSequencesCriterion(),
                new PureStraightCriterion(),
                new SevenShiftedPairsCriterion(),
                new SixShiftedPairsCriterion(),
                new FiveShiftedPairsCriterion(),
                new FourShiftedPairsCriterion(),
                new MixedTripleTripletCriterion(),
                new MixedTripleSequenceCriterion(),
                new TwoTriplePairsCriterion(),
                new MixedTriplePairCriterion(),
                new MixedShiftedTripletsCriterion(),
                new MixedStraightCriterion(),
                new MirroredHandCriterion(),
                new ThreeMirroredPairsCriterion(),
                new TwoMirroredPairsCriterion(),
                new TwoShortStraightsCriterion(),
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
