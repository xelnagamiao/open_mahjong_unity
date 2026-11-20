"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const { withRobertComparison } = require("./robertComparison");

function result(feedingCount, feedingTotal, regularCount, regularTotal) {
  return withRobertComparison({
    result: {
      feeding: { numerator: feedingCount, denominator: feedingTotal },
      normal: { numerator: regularTotal - regularCount, denominator: regularTotal },
    },
  }).result;
}

test("review uses comparable discards and complements only the routine numerator", () => {
  const review = result(76, 153, 510, 1873);
  assert.deepEqual(review.regular_deviation, { numerator: 510, denominator: 1873, rate: 510 / 1873 });
  assert.ok(Math.abs(review.comparison.difference_points - 22.44415830044422) < 1e-12);
  assert.ok(Math.abs(review.comparison.p_greater / 1.42059651706084e-8 - 1) < 1e-12);
});

function choose(n, k) {
  let value = 1;
  for (let i = 1; i <= k; i++) value = value * (n - i + 1) / i;
  return value;
}

test("inclusive upper tail matches direct counts for all small tables in either direction", () => {
  for (let a = 1; a <= 6; a++) {
    for (let b = 1; b <= 6; b++) {
      for (let x = 0; x <= a; x++) {
        for (let y = 0; y <= b; y++) {
          let favorable = 0;
          for (let k = x; k <= Math.min(a, x + y); k++) {
            favorable += choose(a, k) * choose(b, x + y - k);
          }
          const expected = favorable / choose(a + b, x + y);
          assert.ok(Math.abs(result(x, a, y, b).comparison.p_greater - expected) < 1e-14);
        }
      }
    }
  }
});

test("empty denominators leave the comparison unavailable and jobs without results pass through", () => {
  for (const counts of [[0, 0, 0, 0], [0, 0, 4, 10], [1, 4, 0, 0]]) {
    assert.deepEqual(result(...counts).comparison, { difference_points: null, p_greater: null });
  }
  assert.equal(result(1, 4, 0, 0).regular_deviation.rate, null);
  const queued = { status: "queued" };
  assert.equal(withRobertComparison(queued), queued);
});
