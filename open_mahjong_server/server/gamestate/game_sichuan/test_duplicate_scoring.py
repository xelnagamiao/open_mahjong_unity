"""JJ small/large scoring, boundaries, durable totals and cross-table privacy."""
import asyncio
from copy import deepcopy
import json

import pytest

from ...game_calculation.sichuan.duplicate_rules import XUELIU_DUPLICATE, imp, score_board
from ..public.sichuan_duplicate import PairedMatch
from .test_duplicate import pair, run_async


BOUNDARIES = [(0, 0), (1, 1), (9, 1), (10, 2), (29, 2), (30, 3),
              (59, 3), (60, 4), (79, 4), (80, 4), (81, 4), (90, 4), (10000, 4)]


@pytest.mark.parametrize("points,large", BOUNDARIES)
@pytest.mark.parametrize("team", [0, 1])
@pytest.mark.parametrize("scoring", ["raw_difference", "imp"])
def test_jj_boundaries_both_teams_and_scoring_modes(points, large, team, scoring):
    delta = [points, -points] if team == 0 else [-points, points]
    result = score_board(delta, [0, 0], scoring=scoring, imp_scale="jj", board_score_cap=80)
    small_scores, imp_scores = [0, 0], [0, 0]
    small_scores[team], imp_scores[team] = min(points, 80), large
    assert result["raw_difference"] == delta
    assert result["small_scores"] == small_scores
    assert result["imp_scores"] == imp_scores
    assert result["scores"] == (imp_scores if scoring == "imp" else small_scores)


@pytest.mark.parametrize("bad", [-1, 1.5, True, None, "10"])
def test_jj_imp_rejects_invalid_points(bad):
    with pytest.raises(ValueError):
        imp(bad, scale="jj")


@pytest.mark.parametrize("cap", [0, -1, True, 80.5, "80"])
def test_invalid_cap_rejected(cap):
    with pytest.raises(ValueError):
        score_board([1, -1], [0, 0], scoring="raw_difference", imp_scale="jj", board_score_cap=cap)


def test_unknown_imp_scale_rejected():
    with pytest.raises(ValueError):
        score_board([1, -1], [0, 0], scoring="raw_difference", imp_scale="unknown")


@run_async
@pytest.mark.parametrize("closed_blue,small", [(10, 80), (40, 60)])
@pytest.mark.parametrize("first", ["open", "closed"])
async def test_user_examples_cap_after_combining_tables(closed_blue, small, first):
    match, saved = pair(XUELIU_DUPLICATE)
    inputs = {"open": [-100, 100], "closed": [closed_blue, -closed_blue]}
    second = "closed" if first == "open" else "open"
    waiter = asyncio.create_task(match.submit(first, 1, inputs[first], [0, 0], {}))
    await asyncio.sleep(0)
    assert not waiter.done()
    assert not {"scores", "small_scores", "imp_scores", "boards"} & match.public_info().keys()
    final = await match.submit(second, 1, inputs[second], [0, 0], {})
    assert await waiter == final
    assert final["scores"] == final["small_scores"] == [0, small]
    assert final["imp_scores"] == [0, 4]
    assert final["boards"][0]["raw_difference"] == [closed_blue - 100, 100 - closed_blue]
    assert final["boards"][0]["small_scores"] == [0, small]
    assert final["boards"][0]["imp_scores"] == [0, 4]
    before, revisions = deepcopy(match.data), len(saved)
    await match.submit(first, 1, inputs[first], [0, 0], {})
    assert match.data == before and len(saved) == revisions
    recovered = PairedMatch(json.loads(json.dumps(match.data)), lambda *_: None)
    assert recovered.public_info() == final


@run_async
async def test_capped_small_and_imp_accumulate_per_board_across_reload():
    match, _ = pair(XUELIU_DUPLICATE, count=4)
    for number, difference in enumerate([60, -79, 90, -9], 1):
        await asyncio.gather(match.submit("open", number, [difference, -difference], [0, 0], {}),
                             match.submit("closed", number, [0, 0], [0, 0], {}))
        if number < 4:
            assert not match.completed
            assert not {"scores", "small_scores", "imp_scores", "boards"} & match.public_info().keys()
            match = PairedMatch(json.loads(json.dumps(match.data)), lambda *_: None)
    assert match.public_info()["small_scores"] == [140, 88]
    assert match.public_info()["imp_scores"] == [8, 5]
    assert match.public_info()["scores"] == [140, 88]


@run_async
@pytest.mark.parametrize("difference", [29, 30])
async def test_recording_imp_does_not_switch_small_score_winner_or_tiebreak(difference):
    match, _ = pair(XUELIU_DUPLICATE, count=3)
    for number, value in enumerate([80, -difference, -difference], 1):
        await asyncio.gather(match.submit("open", number, [value, -value], [0, 0], {}),
                             match.submit("closed", number, [0, 0], [0, 0], {}))
    assert match.completed and len(match.data["boards"]) == 3
    assert match.data["scores"] == [80, difference * 2]
    assert match.data["imp_scores"] == ([4, 4] if difference == 29 else [4, 6])


@run_async
async def test_sbr_uses_own_table_and_no_80_point_cap():
    match, _ = pair()
    await asyncio.gather(match.submit("open", 1, [100, -100], [0, 0], {}),
                         match.submit("closed", 1, [-10, 10], [0, 0], {}))
    assert match.public_info()["scores"] == match.public_info()["imp_scores"] == [10, 0]
    assert match.public_info()["small_scores"] == [90, 0]


@run_async
async def test_legacy_profile_preserves_saved_small_scoring():
    match, _ = pair(XUELIU_DUPLICATE, count=2)
    match.data["profile"].update(version="xueliu-duplicate-v2")
    del match.data["profile"]["imp_scale"]
    del match.data["profile"]["board_score_cap"]
    await asyncio.gather(match.submit("open", 1, [90, -90], [0, 0], {}),
                         match.submit("closed", 1, [0, 0], [0, 0], {}))
    for key in ("small_scores", "imp_scores"):
        del match.data[key]
        del match.data["results"][0][key]
    match = PairedMatch(json.loads(json.dumps(match.data)), lambda *_: None)
    await asyncio.gather(match.submit("open", 2, [0, 0], [0, 0], {}),
                         match.submit("closed", 2, [-10, 10], [0, 0], {}))
    assert match.public_info()["scores"] == match.public_info()["small_scores"] == [90, 10]
    assert match.public_info()["imp_scores"] == [4, 2]
