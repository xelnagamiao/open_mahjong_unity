"""真实癞子核心的提示、快照隔离和异步过期计算回归。"""
import asyncio
from copy import deepcopy
from unittest.mock import patch

import pytest

from . import hints
from .test_flow import HAND, draw, hand, make_state, ticks
from ...game_calculation.hongzhong import rules as book
from ..public.game_record_manager import init_game_round

STATE = "server.gamestate.game_hongzhong.HongzhongGameState"


@pytest.mark.parametrize("tiles,melds", [
    (HAND, ()), (HAND + [45], ()),
    ([11, 12, 13, 21, 22, 23, 31, 32, 33, 45], ("k19",)),
    ([45], ()),
])
def test_hint_outputs_equal_authoritative_core(tiles, melds):
    payload, detail = hints.compute_hand(tuple(tiles), melds, tiles[-1], False)
    assert payload["source_hand_tiles"] == tiles
    assert payload["source_melds"] == list(melds)
    if len(tiles) + len(melds) * 3 == 13:
        assert set(payload["waiting_tiles"]) == book.waits(tiles, melds)
        assert detail is None
    elif len(tiles) + len(melds) * 3 == 14:
        for tile, waits in payload["waiting_by_discard"].items():
            remaining = list(tiles); remaining.remove(tile)
            assert set(waits) == book.waits(remaining, melds)
        assert detail == book.score(tiles, melds, winning_tile=tiles[-1])
    else:
        assert payload["waiting_tiles"] == [] and payload["waiting_by_discard"] == {}
        assert detail is None


def test_matching_discard_snapshot_and_mismatch():
    original = HAND + [45]
    payload, _ = hints.compute_hand(tuple(original), (), 45, False)
    assert hints.match_hint(payload, list(reversed(original)), ()) is payload
    cut = list(original); cut.remove(28)
    after = hints.match_hint(payload, cut, ())
    assert after["source_hand_tiles"] == cut
    assert set(after["waiting_tiles"]) == book.waits(cut)
    assert hints.match_hint(payload, HAND + [29], ()) is None
    assert hints.match_hint(payload, HAND[:-1], ()) is None
    assert hints.match_hint(payload, original, ("k11",)) is None
    assert hints.match_hint(None, original, ()) is None
    waiting_payload, _ = hints.compute_hand(tuple(HAND), (), None, False)
    assert hints.match_hint(waiting_payload, HAND[:-1], ()) is None


def test_private_fields_and_cut_snapshot_only_reveal_own_hand():
    state = make_state()
    for i in range(4):
        player = hand(state, i, HAND)
        state._hint_cache[i] = hints.compute_hand(tuple(player.hand_tiles), (), None, False)[0]
    own = state.build_private_game_info_fields(0)
    assert own["hongzhong_hints"]["source_hand_tiles"] == HAND
    assert state.build_private_do_action_info(0, 1) == {}
    assert state.build_private_do_action_info(0, 0) == own
    before = deepcopy(own)
    state.player_list[1].hand_tiles = [45] * 4
    state.tiles_list = [39]
    assert state.build_private_game_info_fields(0) == before
    assert state.refresh_waits(0) == book.waits(HAND)
    state._hint_cache.clear()
    assert state.refresh_waits(0) == book.waits(HAND)
    assert state.build_private_game_info_fields(0) == {"hongzhong_info": {
        "self_has_draw_slot": False, "last_drawn_tile": None}}
    state._hint_cache[0] = own["hongzhong_hints"]
    state.tips = False
    assert state.build_private_game_info_fields(0) == {"hongzhong_info": {
        "self_has_draw_slot": False, "last_drawn_tile": None}}


def test_real_cpu_hint_warm_and_single_record():
    async def scenario():
        state = make_state()
        draw(hand(state, 0, HAND), 45)
        progress = []
        async def pulse():
            for _ in range(3):
                await asyncio.sleep(0)
                progress.append(True)
        await asyncio.gather(state._warm_hints(0), pulse())
        assert len(progress) == 3
        assert state._score_cache[0][1] == book.score(HAND + [45], winning_tile=45)
        assert state.score_candidate(0, "self_draw") == state._score_cache[0][1]
        mutated = state.score_candidate(0, "self_draw")
        mutated["fan_names"].append("MUTATED")
        assert "MUTATED" not in state.score_candidate(0, "self_draw")["fan_names"]
        state._record_hints(0)
        assert len([t for t in ticks(state) if t[:2] == ["hongzhong", "hints"]]) == 1
        state._hint_cache.pop(0)
        state._record_hints(0)
    asyncio.run(scenario())


@pytest.mark.parametrize("mutation", ["reset", "round", "seat", "hand", "meld", "draw", "replacement"])
def test_worker_completion_cannot_record_stale_hand_or_previous_round(mutation):
    async def scenario():
        state = make_state(); draw(hand(state, 0, HAND), 45)
        entered, release = asyncio.Event(), asyncio.Event()
        async def delayed(_state, compute, *args):
            entered.set(); await release.wait()
            return compute(*args)
        with patch(STATE + ".run_room_bot_cpu", side_effect=delayed):
            task = asyncio.create_task(state._warm_hints(0)); await entered.wait()
            if mutation == "reset": state._reset_hand_runtime()
            elif mutation == "round": state.round_index += 1; init_game_round(state)
            elif mutation == "seat": state.player_list[0], state.player_list[1] = state.player_list[1], state.player_list[0]
            elif mutation == "hand": state.player_list[0].hand_tiles[0] = 19
            elif mutation == "meld": state.player_list[0].combination_tiles.append("k39")
            elif mutation == "draw": state.player_list[0].last_drawn_tile = 28
            else: state.last_draw_after_kong = True
            release.set(); await task
        assert 0 not in state._hint_cache and 0 not in state._score_cache
        assert all(not r["action_ticks"] for r in state.game_record["game_round"].values())
    asyncio.run(scenario())


def test_warm_before_round_header_then_record_to_correct_round():
    async def scenario():
        state = make_state(); state.round_index = 2
        hand(state, 0, HAND)
        await state._warm_hints(0)
        assert ticks(state) == []
        init_game_round(state); state._record_hints(0)
        assert ticks(state) == []
        event = state.game_record["game_round"]["round_index_2"]["action_ticks"][0]
        assert event[:3] == ["hongzhong", "hints", 0]
    asyncio.run(scenario())
