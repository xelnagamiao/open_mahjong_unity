"""2026-10-17 规则修订：敲锣、额外番、抢杠振听和暗杠展示。"""
import asyncio
from collections import Counter
from copy import deepcopy
from unittest.mock import AsyncMock, patch

import pytest

from .test_lajihe_state import make_state, ticks
from ...game_calculation.shanghai import lajihe as rule
from ...game_calculation.shanghai.test_lajihe import PLAIN, HONORS
from ..game_taiwan.boardcast import _combination_fields_for_rule
from ..verifier.record_sim import RecordSim, stringify_tick
from ..public.game_record_manager import init_game_round


@pytest.mark.parametrize("extra_base,flowers,raw,points", [
    (False, 5, 20, 20), (False, 7, 25, 25), (False, 8, 30, 30),
    (True, 5, 25, 25), (True, 7, 30, 30), (True, 8, 35, 30),
])
@pytest.mark.parametrize("source", ["self_draw", "robbing_kong"])
def test_gong_threshold_and_optional_extra_base(extra_base, flowers, raw, points, source):
    detail = rule.score(PLAIN, flowers=list(range(51, 51 + flowers)),
                        self_draw=source == "self_draw", rob_kong=source == "robbing_kong",
                        self_draw_extra_base=extra_base)
    assert detail["base_score"] == points
    assert detail["uncapped_score"] == raw
    assert ("敲锣" in detail["fan_names"]) == (raw >= 30)


@pytest.mark.parametrize("source,points,changes", [
    ("discard", 40, [-40, 40, 0, 0]),
    ("self_draw", 30, [-30, 90, -30, -30]),
    ("robbing_kong", 30, [-90, 90, 0, 0]),
])
def test_gong_settlement_records_and_replay(source, points, changes):
    async def run():
        state = make_state(detailed_config={rule.SELF_DRAW_EXTRA_BASE: True})
        player = state.player_list[1]
        player.hand_tiles = HONORS.copy() if source == "self_draw" else HONORS[:-1]
        detail = state.score_candidate(1, source, 47)
        assert detail["base_score"] == points
        assert detail["fan"] == 1
        state.run_hu_result_ready_phase = AsyncMock()
        state.pending_winners = [{"index": 1, "payer": None if source == "self_draw" else 0,
                                 "source": source, "hu_class": "hu_self" if source == "self_draw" else "hu_first",
                                 "tile": 47, "detail": detail}]
        await state._settle_hand({p.original_player_index: p.score for p in state.player_list})
        assert [p.score for p in state.player_list] == changes
        assert player.record_counter.win_score == points
        assert player.record_counter.zimo_times + player.record_counter.dianhe_times == 1
        hu = next(t for t in ticks(state) if t[0].startswith("hu_"))
        assert hu[2] == points and hu[4] == changes
        sim = RecordSim(deepcopy(state.game_record))
        sim.load_round("round_index_1")
        sim.apply_tick(stringify_tick(hu))
        assert [p.score for p in sim.players.values()] == changes
    asyncio.run(run())


def test_gong_after_eight_flowers_keeps_the_independent_award():
    async def run():
        state = make_state()
        player = state.player_list[0]
        player.hand_tiles = HONORS.copy()
        player.huapai_list = list(rule.FLOWERS)
        response = ({0: {"action_type": "lajihe_flower_continue"}}, {})
        with patch("server.gamestate.game_shanghai.LajiheGameState._collect_responses", new=AsyncMock(return_value=response)):
            await state._award_eight_flowers(0)
        assert player.score == 90 and player.record_counter.zimo_times == 0
        detail = state.score_candidate(0, "self_draw")
        assert detail["base_score"] == 30 and "敲锣" in detail["fan_names"]
        state.pending_winners = [{"index": 0, "payer": None, "source": "self_draw", "hu_class": "hu_self", "tile": 47, "detail": detail}]
        state.run_hu_result_ready_phase = AsyncMock()
        await state._settle_hand({p.original_player_index: 0 for p in state.player_list})
        assert [p.score for p in state.player_list] == [180, -60, -60, -60]
        assert player.record_counter.zimo_times == 1
        assert [t[0] for t in ticks(state) if t[0] in ("lajihe_eight_flowers", "hu_self")] == ["lajihe_eight_flowers", "hu_self"]
    asyncio.run(run())


@pytest.mark.parametrize("flowers", [0, 1])
@pytest.mark.parametrize("melds", [["s12", "s22", "s32", "k19"], ["s12", "s22", "s32", "g19"]])
def test_big_single_wait_is_an_extra_pattern_and_can_ron(flowers, melds):
    detail = rule.score([41, 41], melds, list(range(51, 51 + flowers)))
    assert detail is not None and detail["base_score"] == 10
    assert "大吊车" in detail["fan_names"]
    state = make_state()
    player = state.player_list[1]
    player.hand_tiles = [41]
    player.combination_tiles = melds
    player.huapai_list = list(range(51, 51 + flowers))
    assert "hu_first" in state.check_discard_actions(41)[1]


@pytest.mark.parametrize("missed_by", ["own_discard", "discard", "robbing_kong"])
def test_rob_kong_ignores_furiten_without_clearing_it(missed_by):
    async def run():
        state = make_state()
        player = state.player_list[1]
        if missed_by == "own_discard":
            state.current_player_index = 1
            player.hand_tiles = HONORS.copy()
            await state.execute_cut(1, {"TileId": 47, "TileIndex": 13, "CutClass": False})
        else:
            player.hand_tiles = HONORS[:-1]
            state.game_status = "waiting_action_after_cut" if missed_by == "discard" else "waiting_action_qianggang"
            state.enter_water(1)
        assert player.lajihe_passed_win
        state.current_player_index = 0
        assert state.score_candidate(1, "discard", 47) is None
        assert "hu_first" in state.check_added_kong_actions(47)[1]
        assert player.lajihe_passed_win
    asyncio.run(run())


@pytest.mark.parametrize("tile,flower_count", [(11, 2), (45, 3)])
def test_concealed_kong_is_back_face_face_back_for_all_viewers_and_replay(tile, flower_count):
    state = make_state()
    expected = [2, tile, 0, tile, 0, tile, 2, tile]
    assert state.build_concealed_kong_mask([tile] * 4) == expected
    player = state.player_list[0]
    player.combination_tiles = [f"G{tile}"]
    player.combination_mask = [expected]
    for viewer in range(-1, 4):
        assert _combination_fields_for_rule(state, player, viewer) == ([f"G{tile}"], [expected])
    assert rule.physical_flowers([], player.combination_tiles, []) == flower_count
    record = deepcopy(state.game_record)
    record["game_round"]["round_index_1"]["p0_tiles"] = [tile] * 4 + [21] * 3 + [31] * 3 + [41] * 3 + [47]
    sim = RecordSim(record)
    sim.load_round("round_index_1")
    sim.apply_tick(stringify_tick(["ag", tile, "F"]))
    assert sim.players[0].combination_masks == [expected]


@pytest.mark.parametrize("discarder", range(4))
@pytest.mark.parametrize("action,hand,tile,tail,flowers", [
    ("chi_right", [12,13,51,21,22,23,31,32,33,17,18,19,41], 11, 41, []),
    ("peng", [11,11,51,21,22,23,31,32,33,17,18,19,41], 11, 41, []),
    ("gang", [11,11,11,51,21,22,23,31,32,33,17,18,19], 11, 41, []),
    ("hu_first", PLAIN[:-2] + [51], 41, 41, [52]),
])
def test_next_seat_replaces_initial_flowers_before_claims_at_every_rotation(discarder, action, hand, tile, tail, flowers):
    async def run():
        state = make_state()
        state.current_player_index = discarder
        index = (discarder + 1) % 4
        player = state.player_list[index]
        player.hand_tiles = hand.copy()
        player.huapai_list = flowers.copy()
        state.player_list[discarder].discard_tiles = [tile]
        state.tiles_list = [25, 26, tail]
        state.game_status = "waiting_action_after_cut"
        allowed = state.check_discard_actions(tile)
        assert allowed[index] == []
        if action != "hu_first":
            await state.execute_claim(index, action)
            assert player.hand_tiles == hand and not player.combination_tiles
        await state.resolve_discard_responses({}, allowed)
        assert not player.passed_pungs and not player.lajihe_passed_win
        await state._deal_normal()
        assert state.action_dict[index] == ["buhua"] and player.normal_draw_count == 0
        assert state.score_candidate(index, "self_draw") is None
        await state.execute_buhua(index)
        assert player.huapai_list == flowers + [51]
        assert player.normal_draw_count == 0 and not player.has_draw_slot
        assert action in state.action_dict[index]
        assert all(not state.action_dict[i] for i in range(4) if i != index)
        if action == "hu_first":
            await state.resolve_discard_responses({index: {"action_type": action}}, state.action_dict)
            assert state.pending_winners[0]["index"] == index
        else:
            await state.execute_claim(index, action)
            assert len(player.combination_tiles) == 1
            assert not any(t in rule.FLOWERS for t in player.hand_tiles)
    asyncio.run(run())


def test_other_seat_claim_preempts_next_seat_initial_replacement():
    async def run():
        state = make_state()
        next_player = state.player_list[1]
        next_player.hand_tiles = [11, 11, 51, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41]
        other = state.player_list[2]
        other.hand_tiles = [11,11,52,21,22,23,31,32,33,17,18,19,41]
        state.player_list[0].discard_tiles = [11]
        state.game_status = "waiting_action_after_cut"
        allowed = state.check_discard_actions(11)
        assert allowed[1] == [] and "peng" in allowed[2]
        await state.resolve_discard_responses({2: {"action_type": "peng"}}, allowed)
        assert state.current_player_index == 2 and other.combination_tiles == ["k11"]
        assert not next_player.huapai_list and 51 in next_player.hand_tiles
        assert state.after_claim_actions()[2] == ["buhua"]
    asyncio.run(run())


OPENING_RECORD_CASES = [
    ("pass", 41, [11,12,13,14,15,16,17,18,19,45,45,41,51], 45),
    ("chi_right", 11, [12,14,15,16,17,18,19,41,41,45,45,47,51], 13),
    ("peng", 11, [11,14,15,16,17,18,19,41,41,45,45,47,51], 11),
    ("gang", 11, [11,11,14,15,16,17,18,19,41,41,45,47,51], 11),
    ("hu_first", 41, [11,12,13,14,15,16,17,18,19,45,45,41,51], 45),
]


async def opening_record_case(action, tile, hand, replacement):
    """真实144张局面产生生产牌谱，供 Python 与 Unity 回放共同验证。"""
    state = make_state()
    complete = [t for t in rule.TILES for _ in range(4)] + list(rule.FLOWERS)
    remaining = complete.copy()
    state.player_list[0].hand_tiles = [21,22,23,24,25,26,27,28,29,31,32,33,34,tile]
    state.player_list[1].hand_tiles = list(hand)
    for p in state.player_list[:2]:
        for used in p.hand_tiles:
            remaining.remove(used)
    remaining.remove(replacement)
    if action == "gang":
        remaining.remove(24)
    for p in state.player_list[2:]:
        p.hand_tiles = [t for t in remaining if t // 10 in (2,3)][:13]
        for used in p.hand_tiles:
            remaining.remove(used)
    state.tiles_list = remaining + ([24] if action == "gang" else []) + [replacement]
    assert Counter(state.tiles_list + [t for p in state.player_list for t in p.hand_tiles]) == Counter(complete)
    state.opening_dealer_action = True
    state.player_list[0].has_draw_slot = True
    state.player_list[0].last_drawn_tile = tile
    init_game_round(state)
    await state.execute_cut(0, {"TileId": tile, "cutIndex": 13, "cutClass": True})
    assert state.game_status == "deal_card"
    await state._deal_normal()
    while any(t in rule.FLOWERS for t in state.player_list[1].hand_tiles):
        await state.execute_buhua(1)
    assert action in state.action_dict[1]
    await state.resolve_discard_responses({1: {"action_type": action}}, state.action_dict)
    if action == "pass":
        await state._deal_normal()
    elif action == "gang":
        await state._deal_supplement()
    elif action == "hu_first":
        state.run_hu_result_ready_phase = AsyncMock()
        await state._settle_hand({p.original_player_index: p.score for p in state.player_list})
    return state


@pytest.mark.parametrize("action,tile,hand,replacement", OPENING_RECORD_CASES)
def test_physical_opening_record_sequence_and_seek_match_server(action, tile, hand, replacement):
    state = asyncio.run(opening_record_case(action, tile, hand, replacement))
    replay = RecordSim(state.game_record)
    key = f"round_index_{state.round_index}"
    frames = replay.apply_all(key)
    assert RecordSim(state.game_record).goto_action(key, len(ticks(state))) == frames[-1]
    for i, player in enumerate(state.player_list):
        expected = player.hand_tiles + ([tile] if i == 1 and action == "hu_first" else [])
        actual = replay.players[i]
        assert Counter(actual.tile_list) == Counter(expected)
        assert actual.discard_tiles == player.discard_tiles
        assert actual.combination_tiles == player.combination_tiles
        assert actual.combination_masks == player.combination_mask
        assert actual.huapai_list == player.huapai_list
        assert actual.score == player.score
    assert replay.current_tiles_list == state.tiles_list
    assert [t[0] for t in ticks(state)[:3]] == ["c", "bh", "bd"]
