"""Independent table progression, synchronous comparison and privacy boundaries."""
import asyncio
from copy import deepcopy
import json

import pytest

from .test_duplicate import pair, run_async
from ..public.sichuan_duplicate import PairedMatch, new_match
from ...game_calculation.sichuan.duplicate_rules import SBR_DUPLICATE, XUELIU_DUPLICATE, make_board


PRIVATE = {"scores", "boards", "small_scores", "imp_scores"}


def test_new_matches_default_async_and_legacy_snapshots_keep_sync():
    match = pair(count=4)
    assert not match.synchronous
    assert match.public_info()["sync_mode"] is False
    legacy = deepcopy(match.data)
    del legacy["sync_mode"]
    assert PairedMatch(legacy).synchronous


@pytest.mark.parametrize("invalid", [0, 1, None, "false", "sync", [], {}])
def test_mode_requires_a_boolean(invalid):
    with pytest.raises(ValueError):
        new_match("m", SBR_DUPLICATE, {"open": "1", "closed": "2"}, [make_board("s", 1)], "s", sync_mode=invalid)


@run_async
@pytest.mark.parametrize("sub", [SBR_DUPLICATE, XUELIU_DUPLICATE])
@pytest.mark.parametrize("fast", ["open", "closed"])
async def test_async_fast_table_can_finish_whole_match_before_peer(sub, fast):
    match = pair(sub, count=4)
    slow = "closed" if fast == "open" else "open"
    for number in (1, 2, 3):
        await asyncio.wait_for(match.submit(fast, number, [3, -3], [0, 0], {}), .5)
        assert match.board(number + 1, fast)["number"] == number + 1
        assert match.board(1, slow)["number"] == 1
        assert not PRIVATE & match.public_info(fast).keys()
        assert match.public_info(fast)["completed_boards"] == 0
        assert not match.public_info(fast)["waiting_tables"]
        # Same result retries do not add scores or advance the fast room twice.
        revision = match.data["revision"]
        await match.submit(fast, number, [3, -3], [0, 0], {})
        assert match.data["revision"] == revision
    assert match.table_progress(fast) == 3 and match.table_progress(slow) == 0
    finished = asyncio.create_task(match.submit(fast, 4, [3, -3], [0, 0], {}))
    await asyncio.sleep(0)
    assert not finished.done() and not match.completed
    for number in (1, 2, 3):
        await match.submit(slow, number, [0, 0], [0, 0], {})
        assert not finished.done()
        assert not PRIVATE & match.public_info(slow).keys()
    await match.submit(slow, 4, [0, 0], [0, 0], {})
    result = await finished
    assert match.completed and len(result["boards"]) == 4
    assert result["imp_scores"] == ([8, 0] if sub == SBR_DUPLICATE else [4, 0])
    assert result["small_scores"] == [12, 0]
    assert result["scores"] == (result["imp_scores"] if sub == SBR_DUPLICATE else result["small_scores"])


@run_async
@pytest.mark.parametrize("sub", [SBR_DUPLICATE, XUELIU_DUPLICATE])
async def test_sync_publishes_only_finished_boards_and_waits_for_both_ready(sub):
    match = pair(sub, count=4, sync_mode=True)
    first = asyncio.create_task(match.submit("open", 1, [3, -3], [0, 0], {"private_hand": [11, 11]}))
    await asyncio.sleep(0)
    assert not first.done() and not PRIVATE & match.public_info().keys()
    with pytest.raises(ValueError):
        match.board(2, "open")
    with pytest.raises(ValueError):
        await match.submit("open", 2, [3, -3], [0, 0], {})
    await match.submit("closed", 1, [0, 0], [0, 0], {"private_hand": [21, 21]})
    result = await first
    assert not match.completed
    assert result["completed_boards"] == 1 and len(result["boards"]) == 1
    assert result["imp_scores"][0] > 0
    assert "private_hand" not in json.dumps(result) and "checkpoint" not in json.dumps(result)
    ready = asyncio.create_task(match.wait_ready("open", 1))
    await asyncio.sleep(0)
    assert not ready.done()
    await match.wait_ready("closed", 1)
    await ready
    assert match.board(2, "open") == match.board(2, "closed")




@run_async
async def test_interrupted_asynchronous_match_rejects_further_boards():
    match = pair(count=8)
    for number in (1, 2, 3):
        await match.submit("open", number, [3, -3], [0, 0], {})
    await match.submit("closed", 1, [0, 0], [0, 0], {})
    match.interrupt("vote_end")
    with pytest.raises(ValueError):
        await match.submit("closed", 2, [0, 0], [0, 0], {})
    assert match.data["completed_boards"] == 1
    assert "checkpoints" not in match.data




@run_async
async def test_async_bloodflow_extra_board_opens_only_after_both_final_results():
    match = pair(XUELIU_DUPLICATE, count=1)
    waiter = asyncio.create_task(match.submit("open", 1, [20, -20], [0, 0], {}))
    await asyncio.sleep(0)
    with pytest.raises(ValueError):
        match.board(2, "open")
    await match.submit("closed", 1, [-20, 20], [0, 0], {})
    await waiter
    assert not match.completed and not PRIVATE & match.public_info().keys()
    assert match.board(2, "open") == match.board(2, "closed")
