"use strict";

// Under equal deviation odds and fixed margins, the number of deviations
// among feeding discards is hypergeometric. Sum weights relative to the mode
// to avoid factorial overflow, and sum the inclusive upper tail directly.
function feedingUpperTail(observed, draws, deviations, population) {
  const remaining = population - draws;
  const low = Math.max(0, deviations - remaining);
  const high = Math.min(deviations, draws);
  if (observed === low) return 1;
  const mode = Math.min(high, Math.floor((draws + 1) * (deviations + 1) / (population + 2)));
  let sum = 1;
  let tail = mode >= observed ? 1 : 0;
  let weight = 1;
  for (let k = mode; k < high; k++) {
    weight *= (draws - k) / (k + 1) * ((deviations - k) / (remaining - deviations + k + 1));
    if (weight === 0) break;
    sum += weight;
    if (k + 1 >= observed) tail += weight;
  }
  weight = 1;
  for (let k = mode; k > low; k--) {
    weight *= k / (draws - k + 1) * ((remaining - deviations + k) / (deviations - k + 1));
    if (weight === 0) break;
    sum += weight;
    if (k - 1 >= observed) tail += weight;
  }
  return Math.min(1, tail / sum);
}

function withRobertComparison(task) {
  if (!task.result) return task;
  const { feeding, normal } = task.result;
  // The existing review counter records non-deviations in the control group.
  const regularCount = normal.denominator - normal.numerator;
  const regular = {
    numerator: regularCount,
    denominator: normal.denominator,
    rate: normal.denominator ? regularCount / normal.denominator : null,
  };
  const comparison = { difference_points: null, p_greater: null };
  if (feeding.denominator && regular.denominator) {
    comparison.difference_points = 100 * (feeding.numerator / feeding.denominator - regular.rate);
    comparison.p_greater = feedingUpperTail(
      feeding.numerator, feeding.denominator, feeding.numerator + regularCount,
      feeding.denominator + regular.denominator,
    );
  }
  return { ...task, result: { ...task.result, regular_deviation: regular, comparison } };
}

module.exports = { withRobertComparison };
