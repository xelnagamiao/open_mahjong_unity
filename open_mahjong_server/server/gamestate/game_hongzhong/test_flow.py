"""红中政策、动作执行与支付集成测试。形状/番种由 test_rules 独立覆盖。"""
import asyncio
from collections import Counter
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from .HongzhongGameState import HongzhongGameState
from ...game_calculation.hongzhong import rules as book
from ..public.game_record_manager import init_game_record, init_game_round


HAND = [11, 12, 13, 14, 15, 16, 21, 22, 23, 34, 35, 36, 28]


def make_state(**overrides):
    server = SimpleNamespace(user_id_to_connection={}, gamestate_manager=SimpleNamespace(), room_manager=SimpleNamespace())
    room = dict(room_id="hz-unit", player_list=[101, 102, 103, 104], round_timer=20,
        step_timer=5, game_round=1, tips=True, room_type="custom", allow_spectator=False)
    state = HongzhongGameState(server, room | overrides, None, None, "unit-hongzhong")
    for i, player in enumerate(state.player_list):
        player.player_index = player.original_player_index = i
    state.tiles_list = [11, 12, 13, 14, 15, 16]
    init_game_record(state)
    init_game_round(state)
    return state


def hand(state, index, tiles, melds=()):
    player = state.player_list[index]
    player.hand_tiles = list(tiles)
    player.combination_tiles = list(melds)
    player.combination_mask = [[0, int(code[1:])] * len(book.meld_tiles(code)) for code in melds]
    return player


def draw(player, tile):
    player.hand_tiles.append(tile)
    player.has_draw_slot = True
    player.last_drawn_tile = tile


def ticks(state):
    return state.game_record["game_round"]["round_index_1"]["action_ticks"]


def test_deal_and_fixed_policy():
    state = make_state()
    state.master_seed = 2024
    state.init_tiles()
    inventory = Counter(state.tiles_list + [t for p in state.player_list for t in p.hand_tiles])
    assert sum(inventory.values()) == 112 and set(inventory.values()) == {4}
    assert set(inventory) == set(book.TILES)
    assert [len(p.hand_tiles) for p in state.player_list] == [14, 13, 13, 13]
    assert state.playable_wall_count() == 59
    assert not state.rules.public_ready_enabled and not state.rules.allow_rob_added_kong
    assert state.ready_candidate_cuts(0) == {} and not state.enter_water(0)
    assert state.build_record_round_fields() == {"detailed_config": book.CONFIG}
    with pytest.raises(ValueError):
        make_state(sub_rule="hongzhong/local")


def test_wall_back_is_upper_then_lower_independent_of_front_draws():
    state = make_state()
    state.tiles_list = [11, 12, 13, 14, 15, 16]
    assert state._take_supplement_tile() == 15
    assert state.tiles_list.pop(0) == 11
    assert state._take_supplement_tile() == 16
    assert state._take_supplement_tile() == 13
    assert state._take_supplement_tile() == 14
    assert state._take_supplement_tile() == 12
    assert not state.can_establish_kong()


@pytest.mark.parametrize("remaining", [0, 1, 2])
def test_wall_exhaustion_and_last_discard(remaining):
    state = make_state()
    state.tiles_list = [21] * remaining
    hand(state, 1, [11] * 3 + HAND[3:])
    assert state.can_take_normal_tile() == bool(remaining)
    assert state.can_establish_kong() == bool(remaining)
    assert bool(state.check_discard_actions(11)[1]) == bool(remaining)


def test_no_claim_on_red_no_chow_no_ron_no_rob():
    state = make_state()
    hand(state, 1, [45] * 3 + HAND[3:])
    hand(state, 2, [12, 13] + HAND[2:])
    assert not any(state.check_discard_actions(45).values())
    assert not any(state.check_discard_actions(11).values())
    assert not any(state.check_added_kong_actions(28).values())
    assert state.score_candidate(1, "discard", 45) is None
    assert state.score_candidate(1, "robbing_kong", 28) is None
    assert state.score_candidate(1, "self_draw") is None
    player = state.player_list[1]
    draw(player, 28); player.tag_list.append("peida")
    assert state.score_candidate(1, "self_draw") is None


@pytest.mark.parametrize("kind,drawn,expected", [
    ("concealed", True, [6, -2, -2, -2]),
    ("added", True, [3, -1, -1, -1]),
    ("added", False, [0, 0, 0, 0]),
])
def test_kong_payment_origin_and_duplicate(kind, drawn, expected):
    state = make_state()
    if kind == "concealed":
        player = hand(state, 0, [11] * 3 + [22, 23, 24, 31, 32, 33, 27, 28, 29, 45])
        draw(player, 11)
        asyncio.run(state.execute_angang(0, 11))
        asyncio.run(state.execute_angang(0, 11))
        assert player.combination_mask == [[2, 11, 0, 11, 0, 11, 2, 11]]
    else:
        player = hand(state, 0, [22, 23, 24, 31, 32, 33, 27, 28, 29, 11 if not drawn else 45], ["k11"])
        draw(player, 11 if drawn else 45)
        asyncio.run(state.execute_jiagang(0, 11))
        asyncio.run(state.finalize_jiagang())
    assert [p.score for p in state.player_list] == expected
    assert len(state.kong_ledger) == 1
    assert state.game_status == "deal_card_after_gang"
    assert not any(state.check_added_kong_actions(11).values())
    assert ["hongzhong", "kong_score", expected, kind] in ticks(state)


def test_direct_kong_one_payer_and_draw_refund_once():
    state = make_state()
    state.player_list[0].discard_tiles = [11]
    hand(state, 2, [11] * 3 + HAND[3:])
    asyncio.run(state.execute_claim(2, "gang"))
    assert [p.score for p in state.player_list] == [-3, 0, 3, 0]
    assert state.player_list[2].combination_tiles == ["g11"]
    state.current_round = 4
    with patch("server.gamestate.game_hongzhong.HongzhongGameState.asyncio.sleep", new=AsyncMock()):
        asyncio.run(state._settle_hand({i: 0 for i in range(4)}))
        asyncio.run(state._settle_hand({i: 0 for i in range(4)}))
    assert [p.score for p in state.player_list] == [0] * 4
    assert ["hongzhong", "kong_refund", [3, 0, -3, 0], "refund"] in ticks(state)
    assert state.next_dealer() == 0
    assert len(state.player_list[0].score_history) == 1


def test_pung_and_forbidden_immediate_kong():
    state = make_state()
    state.player_list[0].discard_tiles = [11]
    player = hand(state, 1, [11] * 3 + HAND[3:])
    asyncio.run(state.execute_claim(1, "peng"))
    assert state.current_player_index == 1 and player.combination_tiles == ["k11"]
    assert state.after_claim_actions()[1] == ["cut"]
    assert not state.kong_allowed(1, 11, "added")
    assert not state.kong_allowed(1, 11, "concealed")
    assert [p.score for p in state.player_list] == [0] * 4
    assert state.player_list[0].discard_origin_tiles == [11]


@pytest.mark.parametrize("index,tile,kind", [(-1, 11, "added"), (True, 11, "added"),
    (0, 45, "concealed"), (0, True, "concealed"), (0, 11, "other"), (0, 11, "added")])
def test_invalid_kongs(index, tile, kind):
    state = make_state()
    draw(hand(state, 0, HAND), 11)
    assert not state.kong_allowed(index, tile, kind)


@pytest.mark.parametrize("birds,expected", [([], [12, -4, -4, -4]),
    ([45], [15, -5, -5, -5]), ([21, 39], [18, -6, -6, -6]),
    ([22, 23], [12, -4, -4, -4])])
def test_birds_front_only_outside_fan_and_exact_record(birds, expected):
    state = make_state()
    player = hand(state, 0, HAND); draw(player, 28)
    detail = {"fan": 2, "fan_names": ["无红中", "一条龙"], "logical_hand": list(player.hand_tiles),
              "joker_substitutions": [], "winning_logical_tile": 28}
    state.pending_winners = [{"index": 0, "source": "self_draw", "detail": detail, "tile": 28}]
    state.tiles_list = list(birds)
    state.current_round = 4
    asyncio.run(state._settle_hand({i: 0 for i in range(4)}))
    assert [p.score for p in state.player_list] == expected
    assert state.tiles_list == []
    birds_tick = next(t for t in ticks(state) if t[:2] == ["hongzhong", "birds"])
    assert birds_tick[2]["tiles"] == birds
    assert birds_tick[2]["detail"] == detail
    assert ticks(state).index(birds_tick) < next(i for i,t in enumerate(ticks(state)) if t[0] == "hu_self")
    assert state._terminal_result["show_result_info"]["revealed_hands"][0] == HAND + [28]
    assert state.next_dealer() == 0


@pytest.mark.parametrize("winner", [0, 1, 2, 3])
def test_winner_takes_dealer(winner):
    state = make_state()
    state.pending_winners = [{"index": winner}]
    assert state.next_dealer() == winner


def test_claim_clock_reconnect_expiry():
    state = make_state(); player = state.player_list[0]
    state.game_status = "waiting_action_after_cut"
    clock = [100.0]
    state.action_clock.now = lambda: clock[0]
    state.on_action_window_broadcast(); state.on_action_window_delivered(0)
    assert state.claim_clock(player) == (20, 5)
    clock[0] = 104.0
    assert state.claim_clock(player, reconnecting=True) == (20, 1)
    clock[0] = 125.0
    assert state.claim_clock(player, reconnecting=True) == (0, 0)
    zero = make_state(round_timer=0, step_timer=1)
    zero.game_status = "waiting_action_after_cut"
    assert zero.claim_clock(zero.player_list[0]) == (0, 1)
