"""血流开局缺门、查花猪收支与牌效机器人回归。"""
import asyncio
from unittest.mock import AsyncMock, patch

import pytest

from .test_xueliu_integration import full_state, fast_sleep, WAIT_HAND
from .action_check import refresh_waiting_tiles, check_action_hand_action
from .xueliu_bot import best_cut, choose_claim, choose_opening, shanten


def state_for(exchange):
    state, messages = full_state()
    if exchange:
        state.sub_rule = 'sichuan/xueliu_exchange'
        state._configure_rule()
    return state, messages


@pytest.mark.parametrize('exchange', [False, True])
def test_only_exchange_asks_dingque_after_receiving_cards(exchange):
    state, _ = state_for(exchange)
    state._xueliu_throw_three_phase = AsyncMock()
    async def choose():
        for p in state.player_list:
            p.dingque_suit = 3
    state._dingque_phase = AsyncMock(side_effect=choose)
    state.broadcast_game_start = AsyncMock()
    state.xueliu_exchange_direction = 1
    with patch(__package__ + '.XueliuGameState.broadcast_dingque_done', new_callable=AsyncMock) as done:
        asyncio.run(state._opening_phase())
        assert state._dingque_phase.await_count == int(exchange)
        assert done.await_count == int(exchange)
    record = state.game_record['game_round'][f'round_index_{state.round_index}']
    assert record.get('dingque_suits') == ({i: 3 for i in range(4)} if exchange else None)


@pytest.mark.parametrize('exchange', [False, True])
def test_flower_pig_pays_twice_normal_noten_to_every_ready_player(exchange):
    state, messages = state_for(exchange)
    extra = [16, 17, 18] if exchange else []
    state.player_list[0].hand_tiles = list(WAIT_HAND) + extra
    state.player_list[1].hand_tiles = [12, 13, 14, 16, 17, 18, 22, 23, 24, 26] + ([22, 23, 24] if exchange else [])
    state.player_list[2].hand_tiles = [11, 13, 15, 17, 19, 22, 24, 26, 28, 29] + ([12, 24, 29] if exchange else [])
    state.player_list[3].hand_tiles = [11, 12, 13, 21, 22, 23, 31, 32, 33, 35] + ([15, 16, 17] if exchange else [])
    assert [state._evaluate_xueliu_wall_end(p)[0] for p in state.player_list] == ['ting', 'ting', 'no_ting', 'hua_zhu']
    with patch(__package__ + '.XueliuGameState.asyncio.sleep', fast_sleep):
        asyncio.run(state._settle_liuju())
    assert [p.score for p in state.player_list] == ([9, 12, -7, -14] if exchange else [6, 6, -4, -8])
    results = [m['message']['show_result_info'] for m in messages if m['user_id'] == 101 and m['message']['show_result_info'].get('liuju_step') == 'chajiao']
    flower = next(r for r in results if r.get('hepai_player_index') == 3)
    expected = {0: 6, 1: 8, 2: 0, 3: -14} if exchange else {0: 4, 1: 4, 2: 0, 3: -8}
    assert {int(k): v for k, v in flower['score_changes'].items()} == expected
    assert sum(p.score for p in state.player_list) == 0
    assert results[-1]['liuju_status_final']


def test_exchange_remaining_designated_suit_is_flower_pig_even_with_two_suits():
    state, _ = state_for(True)
    player = state.player_list[0]
    player.dingque_suit = 1
    player.hand_tiles = list(WAIT_HAND) + [16, 17, 18]
    assert state._evaluate_xueliu_wall_end(player)[0] == 'hua_zhu'
    refresh_waiting_tiles(state, 0)
    assert not player.waiting_tiles
    player.hand_tiles.append(25)
    assert 'hu_self' not in check_action_hand_action(state, 0)[0]


def test_discard_three_counts_exposed_meld_suits_as_flower_pig():
    state, _ = state_for(False)
    player = state.player_list[0]
    player.hand_tiles = [11, 12, 13, 21, 22, 23, 25]
    player.combination_tiles = ['k31']
    assert state._evaluate_xueliu_wall_end(player)[0] == 'hua_zhu'


@pytest.mark.parametrize('exchange', [False, True])
def test_bot_discards_third_suit_instead_of_preserving_invalid_ready_hand(exchange):
    hand = list(WAIT_HAND) + [31] + ([16, 17, 18] if exchange else [])
    meld_count = 4 if exchange else 3
    tile, _ = best_cut(hand, [], [0]*34, meld_count, 3 if exchange else 0)
    assert tile == 31
    assert shanten(list(WAIT_HAND) + ([16, 17, 18] if exchange else []), [], meld_count) == 0


def test_bot_never_claims_third_exposed_suit():
    assert choose_claim([31, 31, 32, 33], ['k11', 'k21'], [0]*34, ['peng', 'pass'], 31) == 'pass'


def test_opening_throws_weak_suit_not_always_first_suit():
    hand = [11, 12, 13, 14, 15, 16, 25, 25, 25, 26, 26, 31, 34, 38]
    chosen = choose_opening(hand)
    assert chosen == [31, 34, 38]


@pytest.mark.parametrize('exchange', [False, True])
def test_flower_pigs_without_ready_recipients_show_reason_and_pay_nobody(exchange):
    state, messages = state_for(exchange)
    for p in state.player_list:
        p.hand_tiles = [11, 12, 13, 21, 22, 23, 31, 32, 33, 35] + ([15, 16, 17] if exchange else [])
    with patch(__package__ + '.XueliuGameState.asyncio.sleep', fast_sleep):
        asyncio.run(state._settle_liuju())
    assert [p.score for p in state.player_list] == [0, 0, 0, 0]
    results = [m['message']['show_result_info'] for m in messages if m['user_id'] == 101 and m['message']['show_result_info'].get('liuju_step') == 'chajiao']
    assert len(results) == 4
    assert all(list(r['liuju_status'].values()) == ['hua_zhu_no_payee'] for r in results)


@pytest.mark.parametrize('kind,payer,expected', [
    ('guafeng', 1, {0: 3, 1: -3, 2: 0, 3: 0}),
    ('xiayu1', None, {0: 3, 1: -1, 2: -1, 3: -1}),
    ('xiayu2', None, {0: 6, 1: -2, 2: -2, 3: -2}),
])
def test_exchange_kongs_pay_immediately_without_blood_battle_transfer(kind, payer, expected):
    state, _ = state_for(True)
    changes = state._record_gang_score(0, 11, kind, payer)
    assert changes == expected
    assert [p.score for p in state.player_list] == list(expected.values())
    assert state.paofen_watch is None
    assert 'xueliu_fan' not in state.player_list[0].gang_score_records[-1]


def test_exchange_robbed_added_kong_refunds_only_that_kong():
    state, _ = state_for(True)
    state._record_gang_score(0, 11, 'xiayu2')
    state._record_gang_score(0, 22, 'xiayu1')
    assert state._refund_last_gang(0, 22) == {0: -3, 1: 1, 2: 1, 3: 1}
    assert [p.score for p in state.player_list] == [6, -2, -2, -2]
    assert len(state.player_list[0].gang_score_records) == 1
    assert state._refund_last_gang(0, 22) == {}


def test_exchange_last_tile_bonus_is_self_draw_only():
    from .action_check import _build_way_tokens
    state, _ = state_for(True)
    state.tiles_list = []
    assert _build_way_tokens(state, 0, 'handgot') == ['海底']
    assert _build_way_tokens(state, 0, 'dianhe') == []
    state.last_action_was_gang = True
    assert _build_way_tokens(state, 0, 'dianhe') == ['杠上炮']


@pytest.mark.parametrize('exchange', [False, True])
def test_opening_countdown_has_no_extra_normal_step_time(exchange):
    from .boardcast import broadcast_xueliu_throw_three_ask
    state, messages = state_for(exchange)
    state.step_time = 5
    state.action_dict = {i: [state.xueliu_opening_action] for i in range(4)}
    asyncio.run(broadcast_xueliu_throw_three_ask(state))
    asks = [m['message']['ask_hand_action_info'] for m in messages if m['type'].endswith('broadcast_hand_action')]
    assert len(asks) == 4
    assert all(a['remaining_time'] == 10 and a['step_remaining'] == 0 for a in asks)
