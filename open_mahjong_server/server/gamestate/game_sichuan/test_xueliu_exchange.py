"""换三张开局、独立牌型选择、和后锁手与牌谱回归。"""
import asyncio
from collections import Counter
import random
from unittest.mock import patch

import pytest

from .action_check import refresh_waiting_tiles, _xueliu_angang_preserves_waiting
from .test_xueliu_integration import full_state, settle_win
from ...game_calculation.sichuan.xueliu_rules import validate_sichuan_sub_rule


EXCHANGE_WIN = [11, 12, 13, 14, 15, 16, 21, 22, 23, 24, 25, 26, 28, 28]


def exchange_state():
    state, messages = full_state()
    state.sub_rule = validate_sichuan_sub_rule("sichuan/xueliu_exchange")
    state._configure_rule()
    return state, messages


@pytest.mark.parametrize("seed", range(8))
def test_exchange_conserves_tiles_and_hand_sizes_for_every_direction(seed):
    state, _ = exchange_state()
    deck = [suit * 10 + rank for suit in (1, 2, 3) for rank in range(1, 10) for _ in range(4)]
    random.Random(seed).shuffle(deck)
    state.round_random_seed = seed
    state.player_list[0].hand_tiles = deck[:14]
    for i in range(1, 4):
        state.player_list[i].hand_tiles = deck[14 + 13 * (i - 1):14 + 13 * i]
    before = [list(player.hand_tiles) for player in state.player_list]
    all_before = Counter(tile for hand in before for tile in hand)
    for i in range(4):
        chosen = state._default_xueliu_throw_tiles(i)
        assert len({tile // 10 for tile in chosen}) == 1
        state._consume_xueliu_throw_tiles(i, chosen)
    state._exchange_selected_tiles()
    assert [len(p.hand_tiles) for p in state.player_list] == [14, 13, 13, 13]
    assert Counter(tile for p in state.player_list for tile in p.hand_tiles) == all_before
    assert state.xueliu_exchange_direction in (1, 2, 3)
    for i in range(4):
        received = state.player_list[(i - state.xueliu_exchange_direction) % 4].xueliu_throw_tiles
        expected = Counter(before[i]) - Counter(state.player_list[i].xueliu_throw_tiles) + Counter(received)
        assert Counter(state.player_list[i].hand_tiles) == expected


def test_exchange_asks_distinct_action_and_records_post_exchange_hands():
    state, _ = exchange_state()
    for i, player in enumerate(state.player_list):
        player.hand_tiles = list(EXCHANGE_WIN if i == 0 else EXCHANGE_WIN[:-1])

    async def respond(current):
        assert all(actions == ["xueliu_exchange_three"] for actions in current.action_dict.values())
        for i in range(4):
            await current.action_queues[i].put({"action_type": "xueliu_exchange_three", "selected_tiles": [11, 12, 13], "_action_tick": current.server_action_tick})
            current.action_events[i].set()

    with patch(__package__ + ".boardcast.broadcast_xueliu_throw_three_ask", respond):
        asyncio.run(state._opening_phase())
    assert [len(p.hand_tiles) for p in state.player_list] == [14, 13, 13, 13]
    assert state.xueliu_rule_profile["exchange_three"] is True
    assert state.xueliu_rule_profile["discard_three"] is False
    record = state.game_record["game_round"][f"round_index_{state.round_index}"]
    assert record["xueliu_exchange_direction"] in (1, 2, 3)
    assert len(record["p0_tiles"]) == 14


def test_exchange_uses_four_groups_and_preserves_thirteen_tile_wait_after_repeated_wins():
    state, _ = exchange_state()
    calc = state.calculation_service
    assert calc.Sichuan_xueliu_hepai_check(EXCHANGE_WIN, [], ["自摸"], 28) == (0, [])
    assert calc.Sichuan_xueliu_hepai_check(EXCHANGE_WIN, [], ["自摸"], 28, meld_count=4)[0] == 3
    for count in (1, 2):
        player = state.player_list[0]
        player.hand_tiles = list(EXCHANGE_WIN[:-1])
        refresh_waiting_tiles(state, 0)
        assert 28 in player.waiting_tiles
        player.hand_tiles.append(28)
        player.has_draw_slot = True
        state.current_player_index = 0
        state.pending_win = {"type": "zimo"}
        state.sichuan_hu_results = {0: {"fan": 3, "fan_list": ["基本胡", "门清"], "hepai_tile": 28}}
        settle_win(state)
        assert player.hand_tiles == EXCHANGE_WIN[:-1]
        assert player.win_count == count and len(player.huapai_list) == count
    assert [p.score for p in state.player_list] == [18, -6, -6, -6]


def test_exchange_post_win_kong_keeps_all_waits():
    state, _ = exchange_state()
    player = state.player_list[0]
    player.hand_tiles = [11, 11, 11, 12, 13, 14, 21, 22, 23, 24, 25, 26, 28]
    refresh_waiting_tiles(state, 0)
    player.locked_waiting_tiles = set(player.waiting_tiles)
    player.hand_tiles.append(11)
    assert _xueliu_angang_preserves_waiting(state, player, 11)
