import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .GuangdongGameState import GuangdongGameState
from ...game_calculation.guangdong.config import TILES, GHOSTS, SUB_RULE
from ..public.game_record_manager import init_game_record, init_game_round

HAND = [11, 11, 11, 22, 22, 22, 33, 33, 33, 41, 41, 41, 45]
MODULE = "server.gamestate.game_guangdong.lifecycle"


def make_state(**overrides):
    server = SimpleNamespace(user_id_to_connection={}, gamestate_manager=SimpleNamespace(), room_manager=SimpleNamespace())
    room = dict(room_id="123456", player_list=[101, 102, 103, 104], round_timer=20,
        step_timer=5, game_round=1, tips=False, room_type="custom", allow_spectator=False, sub_rule=SUB_RULE)
    state = GuangdongGameState(server, room | overrides, None, None, "unit-guangdong-state")
    for i, p in enumerate(state.player_list):
        p.player_index = p.original_player_index = i
    state.tiles_list = [19] * 30
    init_game_record(state)
    init_game_round(state)
    return state


def hand(state, index, tiles, melds=()):
    player = state.player_list[index]
    player.hand_tiles = list(tiles)
    player.combination_tiles = list(melds)
    player.combination_mask = [[0, int(c[1:])] * (4 if c[0] in "gG" else 3) for c in melds]
    return player


def draw(player, tile):
    player.hand_tiles.append(tile)
    player.has_draw_slot = True
    player.last_drawn_tile = tile


def ticks(state):
    return state.game_record["game_round"]["round_index_1"]["action_ticks"]


def test_physical_wall_140_and_zero_dead_wall():
    state = make_state(); state.master_seed = 2023; state.init_tiles()
    supply = Counter(state.tiles_list + [t for p in state.player_list for t in p.hand_tiles])
    assert all(supply[t] == 4 for t in TILES) and all(supply[t] == 1 for t in GHOSTS)
    assert [len(p.hand_tiles) for p in state.player_list] == [14, 13, 13, 13]
    assert len(state.tiles_list) == state.playable_wall_count() == 87
    assert not state.flower_tiles
    state.tiles_list = []
    assert not state.can_take_normal_tile() and not state.can_establish_kong()
    state.tiles_list = [55]
    assert state.can_take_normal_tile() and state.can_take_supplement_tile()


@pytest.mark.parametrize("ghost", GHOSTS)
def test_ghost_is_kept_and_can_be_discarded_but_never_claimed(ghost):
    state = make_state(); player = hand(state, 0, HAND); draw(player, ghost)
    asyncio.run(state._process_drawn_flowers(0, "normal"))
    assert ghost in player.hand_tiles and not player.huapai_list
    assert "buhua" not in state.check_hand_actions(0)[0]
    asyncio.run(state.execute_cut(0, {"TileId": ghost, "cutIndex": 13, "cutClass": True}))
    assert player.discarded_ghosts == 1 and ghost in player.discard_tiles
    assert ["guangdong", "ghost_discard", 0, 1] in ticks(state)
    assert not any(state.check_discard_actions(ghost).values())
    assert not state.kong_allowed(0, ghost, "concealed")


def test_no_chow_and_mandatory_cut_after_pung():
    state = make_state(); source = hand(state, 0, []); source.discard_tiles = [11]
    hand(state, 1, [12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41, 42, 42])
    hand(state, 2, [11, 11, 12, 13, 21, 22, 23, 31, 32, 33, 41, 41, 41])
    actions = state.check_discard_actions(11)
    assert not actions[1] and "peng" in actions[2]
    asyncio.run(state.resolve_discard_responses({2: {"action_type": "peng"}}, actions))
    assert state.current_player_index == 2 and state.after_claim_actions()[2] == ["cut"]
    assert not state.kong_allowed(2, 41, "concealed") and not state.ready_candidate_cuts(2)


def test_pass_uses_base_score_and_resets_only_on_draw():
    state = make_state(); p = hand(state, 1, HAND)
    detail = state.score_candidate(1, "discard", 45)
    state.result_dict["hu_first"] = detail
    state.enter_water(1)
    assert p.passed_base_score == detail["base_score"] and not p.water
    assert state.score_candidate(1, "discard", 45) is None
    p.discarded_ghosts = 1
    assert state.score_candidate(1, "discard", 45)["base_score"] == detail["base_score"] * 2
    before = p.passed_base_score
    state.current_player_index = 1; state.enter_water(1)
    assert p.passed_base_score == before
    draw(p, 45); asyncio.run(state._process_drawn_flowers(1, "normal"))
    assert p.passed_base_score == -1


def test_discard_winning_tile_records_pass_threshold():
    state = make_state(); p = hand(state, 0, HAND); draw(p, 45)
    asyncio.run(state.execute_cut(0, {"TileId": 45, "cutIndex": 13, "cutClass": True}))
    assert p.passed_base_score > 0 and state.score_candidate(0, "discard", 45) is None


@pytest.mark.parametrize("kind,drawn,expected", [("concealed", True, [6, -2, -2, -2]),
    ("added", True, [3, -1, -1, -1]), ("added", False, [0, 0, 0, 0])])
def test_kong_scores_once_and_old_hand_added_is_free(kind, drawn, expected):
    state = make_state()
    if kind == "concealed":
        p = hand(state, 0, HAND); draw(p, 11)
        asyncio.run(state.execute_angang(0, 11)); asyncio.run(state.execute_angang(0, 11))
    else:
        p = hand(state, 0, [11, 22, 23, 24, 31, 32, 33, 41, 41, 45], ["k11"])
        if drawn:
            p.hand_tiles.remove(11); p.hand_tiles.append(45); draw(p, 11)
        else:
            draw(p, 45)
        asyncio.run(state.execute_jiagang(0, 11)); asyncio.run(state.finalize_jiagang())
    assert [p.score for p in state.player_list] == expected
    assert state.game_status == "deal_card_after_gang"


def test_robbed_kong_is_rolled_back_and_not_paid():
    state = make_state(); p = hand(state, 0, [22, 23, 24, 31, 32, 33, 42, 42, 47, 47], ["k45"])
    draw(p, 45); hand(state, 1, HAND)
    asyncio.run(state.execute_jiagang(0, 45))
    assert state.game_status == "waiting_action_qianggang"
    asyncio.run(state.resolve_rob_kong_responses({1: {"action_type": "hu_first"}}, state.action_dict))
    assert state.game_status == "END" and p.combination_tiles == ["k45"]
    assert not state.kong_ledger and [p.score for p in state.player_list] == [0] * 4
    state.current_round = 4
    asyncio.run(state._settle_hand({i: 0 for i in range(4)}))
    base = state.pending_winners[0]["detail"]["base_score"]
    assert [p.score for p in state.player_list] == [-3 * base, 3 * base, 0, 0]
    assert not any(t[:2] == ["guangdong", "horses"] for t in ticks(state))


def test_direct_kong_responsibility_and_draw_refund():
    state = make_state(); hand(state, 0, []).discard_tiles = [11]
    hand(state, 1, HAND)
    asyncio.run(state.execute_claim(1, "gang"))
    assert [p.score for p in state.player_list] == [-3, 3, 0, 0]
    assert state.direct_kong_payer == 0
    state.current_round = 4
    with patch(MODULE + ".asyncio.sleep", new=AsyncMock()):
        asyncio.run(state._settle_hand({i: 0 for i in range(4)}))
    assert [p.score for p in state.player_list] == [0] * 4
    assert ["guangdong", "refund_kongs", [3, -3, 0, 0]] in ticks(state)


def test_self_draw_horses_removed_from_front_and_paid_once():
    state = make_state(); p = hand(state, 0, HAND); draw(p, 45)
    state.tiles_list = [11, 55, 41, 32, 19]
    state.accept_self_draw(0); base = state.pending_winners[0]["detail"]["base_score"]
    state.current_round = 4
    asyncio.run(state._settle_hand({i: 0 for i in range(4)}))
    assert state.tiles_list == [19]
    assert [p.score for p in state.player_list] == [3 * (base + 2)] + [-base - 2] * 3
    horse = next(t[2] for t in ticks(state) if t[:2] == ["guangdong", "horses"])
    assert horse["tiles"] == [11, 55, 41, 32] and horse["hits"] == [2, 0, 0, 0]


def test_last_draw_self_and_ron_have_haidi_and_cannot_kong():
    state = make_state(); state.tiles_list = []
    p = hand(state, 1, HAND)
    assert "last_tile" in state.score_candidate(1, "discard", 45)["fan_ids"]
    draw(p, 45)
    assert "last_tile" in state.score_candidate(1, "self_draw")["fan_ids"]
    assert all(a.startswith("hu_") or a == "pass" for row in state.check_discard_actions(45).values() for a in row)
    assert not state.kong_allowed(1, 11, "concealed")


def test_winner_becomes_dealer_and_reset_drops_round_ledgers():
    state = make_state()
    assert state.next_dealer() == 0
    for i in range(4):
        state.pending_winners = [{"index": i}]
        assert state.next_dealer() == i
    state.player_list[0].discarded_ghosts = 2
    state.player_list[0].passed_base_score = 8
    state._reset_hand_runtime()
    assert not state.pending_winners and not state.kong_ledger
    assert state.player_list[0].discarded_ghosts == 0 and state.player_list[0].passed_base_score == -1


def test_head_bump_and_high_priority_resolution():
    state = make_state()
    assert state._selected_winners([(3, "hu_third"), (2, "hu_second"), (1, "hu_first")]) == [(1, "hu_first")]
