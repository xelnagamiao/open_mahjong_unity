"""Deferred blood-battle scores must survive a replay round change."""
import json

import pytest

from .record_sim.decoder import accumulate_score_changes_from_tick


@pytest.mark.parametrize("encoded", [False, True])
def test_deferred_scores_follow_original_players_across_rotated_rounds(encoded):
    def tick(values):
        if not encoded:
            return values
        return [json.dumps(value, ensure_ascii=False) if isinstance(value, list)
                else str(value) for value in values]

    # Original players now sit in seats 2, 3, 0, 1. Mid-round wins retire
    # players without exposing or applying their deferred payment yet.
    seats = [2, 3, 0, 1]
    delta = accumulate_score_changes_from_tick(
        None, tick(["hu_first", 1, 0, [], [0, 0, 0, 0], 24]), seats)
    assert delta == [0, 0, 0, 0]

    delta = accumulate_score_changes_from_tick(
        delta, tick(["blood", "settle_hu", "hu_first", 1, 1,
                     ["断幺九"], [0, 6, 0, -6], 1]), seats)
    delta = accumulate_score_changes_from_tick(
        delta, tick(["blood", "settle_hu", "hu_self", 3, 1,
                     ["自摸"], [-3, 0, -3, 6], 2]), seats)
    assert delta == [-3, 0, -3, 6]

    # final contains absolute table scores. Counting it as another payment
    # would make the next round start at the wrong cumulative total.
    delta = accumulate_score_changes_from_tick(
        delta, tick(["blood", "final", [-10, 6, 7, -3]]), seats)
    assert delta == [-3, 0, -3, 6]
    previous = [10, -3, -7, 0]
    next_start = [a + b for a, b in zip(previous, delta)]
    assert next_start == [7, -3, -10, 6]

    # Another dealer rotation must retain player identity, not seat identity.
    next_delta = accumulate_score_changes_from_tick(
        None, tick(["blood", "settle_hu", "hu_self", 0, 1,
                    ["自摸"], [6, -2, -2, -2], 1]), [3, 0, 1, 2])
    total = [a + b for a, b in zip(next_start, next_delta)]
    assert total == [5, 3, -12, 4]
    assert sum(total) == 0
