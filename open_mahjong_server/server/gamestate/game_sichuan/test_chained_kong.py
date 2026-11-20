"""SBR 杠后再加杠被和：真实动作、退税、双打副末结算及牌谱回归。"""
import asyncio
from contextlib import asynccontextmanager
from copy import deepcopy
from unittest.mock import AsyncMock, patch

import pytest

from .action_check import check_action_after_cut, check_action_hand_action, refresh_waiting_tiles
from .test_action_window import submit
from .test_duplicate import run_async
from .test_duplicate_active_huazhu import make_state as make_duplicate
from .test_post_peng_jiagang import make_state
from .wait_action import wait_action
from ..public.game_record_manager import init_game_round, player_action_record_deal
from ..public.lifecycle import cancel_auxiliary_tasks
from ..public.outbound_pipe import drain_viewer
from ..verifier.record_sim.goto_action import RecordPlayer, RecordSim, stringify_tick


BATTLE = ('standard', 'doubles', 'duplicate_open', 'duplicate_closed')
FLOW = ('xueliu', 'xueliu_exchange', 'xueliu_doubles')
WAIT_21 = [22, 23, 14, 15, 16, 17, 18, 19, 26, 27, 28, 25, 25]
OTHER_WAITS_21 = {
    2: [22, 23, 12, 13, 14, 15, 16, 17, 26, 27, 28, 29, 29],
    3: [22, 23, 12, 13, 14, 15, 16, 17, 27, 28, 29, 24, 24],
}


@asynccontextmanager
async def scenario(mode, *, first='jiagang', timely_second=True, claimants=(1,)):
    if mode.startswith('duplicate_'):
        state, messages, _ = make_duplicate(mode.split('_')[1])
        state.game_server.gamestate_manager.gamestate_id_to_game_state[state.gamestate_id] = state
    else:
        state, messages = make_state(mode)
    state.room_id = '911116'
    state.claim_protection = state.tactical_call = False
    state.current_player_index = 0
    state.blood_battle = True
    state._record_rule_profile()
    for p in state.player_list:
        p.hand_tiles = [12, 14, 16, 18, 22, 24, 26, 28, 29, 32, 34, 36, 38]
        p.combination_tiles = []
        p.combination_mask = []
        p.discard_tiles = []
        p.waiting_tiles = set()
        p.dingque_suit = 0 if state.is_doubles else 3
    actor = state.player_list[0]
    actor.hand_tiles = [12, 13, 14, 16, 17, 18, 24]
    if mode == 'xueliu':
        actor.hand_tiles = actor.hand_tiles[:3] + [24]
    if not timely_second:
        actor.hand_tiles[-1] = 21
    actor.combination_tiles = ['k21']
    if first in ('jiagang', 'non_timely'):
        actor.combination_tiles.insert(0, 'k11')
        actor.hand_tiles.append(11)
        if first == 'non_timely':
            actor.hand_tiles[0], actor.hand_tiles[-1] = actor.hand_tiles[-1], actor.hand_tiles[0]
    else:
        actor.hand_tiles = [11, 11, 11] + actor.hand_tiles
        if first == 'angang':
            actor.hand_tiles.append(11)
    actor.combination_mask = [[1, int(m[1:]), 0, int(m[1:]), 0, int(m[1:])] for m in actor.combination_tiles]
    actor.has_draw_slot = first not in ('gang', 'partner_gang')
    for seat in claimants:
        p = state.player_list[seat]
        p.hand_tiles = list(OTHER_WAITS_21.get(seat, WAIT_21) if mode != 'xueliu' else WAIT_21[:5] + WAIT_21[8:])
        refresh_waiting_tiles(state, seat)
        assert 21 in p.waiting_tiles and 11 not in p.waiting_tiles
    replacement = 21 if timely_second else 24
    if hasattr(state.tiles_list, 'parts'):
        state.tiles_list.parts = [[replacement, 25, 29], [19], [19], [19]]
        state.tiles_list.clear()
        state.tiles_list.extend(t for part in state.tiles_list.parts for t in part)
    else:
        state.tiles_list = [replacement, 25, 29, 19, 19, 19]
    init_game_round(state)
    try:
        yield state, messages
    finally:
        await cancel_auxiliary_tasks(state)


async def hand_action(state, kind, tile):
    state.game_status = 'waiting_hand_action'
    state.action_dict = check_action_hand_action(state, 0, is_get_gang_tile=state.last_action_was_gang)
    assert kind in state.action_dict[0]
    await state.broadcast_ask_hand_action()
    await drain_viewer(state, 0)
    fields = dict(TileId=tile, cutClass=state.player_list[0].hand_tiles[-1] == tile) if kind == 'cut' else dict(target_tile=tile)
    await submit(state, 0, kind, **fields)
    await asyncio.wait_for(wait_action(state), 2)


async def first_kong(state, kind):
    if kind in ('gang', 'partner_gang'):
        payer = 2 if kind == 'partner_gang' else 3
        state.current_player_index = payer
        state.player_list[payer].discard_tiles = [11]
        state.game_status = 'waiting_action_after_cut'
        state.action_dict = check_action_after_cut(state, 11)
        await claims(state, {0: 'gang'})
    else:
        await hand_action(state, 'jiagang' if kind == 'non_timely' else kind, 11)
    assert state.game_status == 'deal_card_after_gang'


def supplement(state):
    assert state.game_status == 'deal_card_after_gang'
    state.player_list[0].get_gang_tile(state.tiles_list, state)
    player_action_record_deal(state, deal_tile=state.player_list[0].hand_tiles[-1], deal_type='gd')


async def claims(state, responses):
    await state.broadcast_ask_other_action()
    await asyncio.gather(*(drain_viewer(state, i) for i in range(4)))
    for seat, actions in state.action_dict.items():
        if actions:
            await submit(state, seat, responses.get(seat, 'pass'))
    await asyncio.wait_for(wait_action(state), 2)


async def settle(state):
    with patch('asyncio.sleep', new_callable=AsyncMock):
        await state._settle_win()


def ticks(state):
    return state.game_record['game_round'][f'round_index_{state.round_index}']['action_ticks']


def replay_from(state):
    sim = RecordSim(state.game_record)
    sim.load_round(f'round_index_{state.round_index}')
    sim.players = {p.player_index: RecordPlayer(p.original_player_index, p.player_index,
        score=p.score, tile_list=list(p.hand_tiles), combination_tiles=list(p.combination_tiles),
        combination_masks=deepcopy(p.combination_mask), show_hand_draw_slot_active=p.has_draw_slot)
        for p in state.player_list}
    sim.current_player_index = 0
    sim.current_tiles_list = list(state.tiles_list)
    return sim


@pytest.mark.parametrize('mode', BATTLE)
@pytest.mark.parametrize('first', ['jiagang', 'angang', 'gang', 'non_timely'])
@pytest.mark.parametrize('timely_second', [True, False])
@run_async
async def test_chained_robbery_counts_both_fans_and_refunds_the_two_distinct_kongs(mode, first, timely_second):
    async with scenario(mode, first=first, timely_second=timely_second) as (state, messages):
        await first_kong(state, first)
        supplement(state)
        await hand_action(state, 'jiagang', 21)
        result = state.sichuan_hu_results[1]
        assert set(result['way']) == {'抢杠', '杠上炮'}
        assert result['fan'] == 2
        await claims(state, {1: 'hu'})
        await settle(state)
        actor = state.player_list[0]
        completed = 'G11' if first == 'angang' else 'g11'
        assert actor.combination_tiles == ([completed, 'k21'] if first in ('jiagang', 'non_timely') else ['k21', completed])
        assert len(actor.combination_mask[actor.combination_tiles.index('k21')]) == 6
        assert [p.score for p in state.player_list] == [0] * 4
        assert state.paofen_watch is None
        assert state.deferred_hu_settlements[0]['base'] == 4
        if state.is_doubles:
            assert all(r['cancelled'] for r in actor.gang_score_records)
            assert actor.gang_score_records[0]['void_reason'] == ('non_timely' if first == 'non_timely' else 'gang_shang_pao')
            assert actor.combination_mask[actor.combination_tiles.index(completed)] == [0, 11] * 4
            assert not any(t[0] == 'gr' for t in ticks(state))
        else:
            assert not actor.gang_score_records
            refunds = [t for t in ticks(state) if t[0] == 'gr']
            amount = (2 if first == 'angang' else 1 if first == 'jiagang' else 0)
            expected = [-3 * amount, amount, amount, amount]
            if first == 'gang':
                expected = [-2, 0, 0, 2]
            if timely_second:
                expected = [a + b for a, b in zip(expected, [-3, 1, 1, 1])]
            assert refunds == ([['gr', 'gs', *expected]] if any(expected) else [])
            packets = [m['message']['show_result_info'] for m in messages
                       if m['message'].get('show_result_info', {}).get('gang_refund_changes')]
            assert len(packets) == (4 if any(expected) else 0)
            for packet in packets:
                delta = {int(k): v for k, v in packet['gang_refund_changes'].items()}
                assert [delta.get(i, 0) for i in range(4)] == expected


@pytest.mark.parametrize('mode', BATTLE)
@pytest.mark.parametrize('winners', [(1,), (2,), (1, 3), (1, 2, 3)])
@run_async
async def test_partner_multi_ron_board_settlement_and_replay_remain_consistent(mode, winners):
    async with scenario(mode, claimants=winners) as (state, messages):
        sim = replay_from(state)
        await first_kong(state, 'jiagang')
        supplement(state)
        await hand_action(state, 'jiagang', 21)
        await claims(state, {seat: 'hu' for seat in winners})
        await settle(state)
        # Finish the board through the authoritative settlement path if the wins did not end it.
        if state.game_status != 'END':
            with patch('asyncio.sleep', new_callable=AsyncMock):
                await state._settle_liuju()
        for tick in ticks(state):
            sim.apply_tick(stringify_tick(tick))
        assert [sim.players[i].score for i in range(4)] == [p.score for p in state.player_list]
        assert sim.players[0].combination_tiles == state.player_list[0].combination_tiles
        assert sim.players[0].combination_masks == state.player_list[0].combination_mask
        assert sum(p.score for p in state.player_list) == 0
        if state.is_doubles:
            summary = next(m['message']['show_result_info']['sichuan_settlement'] for m in messages
                           if m['message'].get('show_result_info', {}).get('sichuan_settlement'))
            kongs = [e for e in summary['events'] if e['kind'] == 'kong']
            assert len(kongs) == 2 and all(not any(e['changes'].values()) for e in kongs)
            assert kongs[0]['detail'] == '加杠 · 杠上炮退税'
            assert kongs[1]['detail'] == '加杠 · 被抢杠'
            for event in (e for e in summary['events'] if e['kind'] == 'ron'):
                assert event['fan'] == 2
                assert event['points'] == (0 if event['seat'] == 2 else 4)
                if event['seat'] == 2:
                    assert not any(event['changes'].values())


@pytest.mark.parametrize('mode', BATTLE)
@run_async
async def test_successful_second_kong_discard_refunds_only_the_second(mode):
    async with scenario(mode) as (state, _):
        await first_kong(state, 'jiagang')
        first = state.player_list[0].gang_score_records[0]
        supplement(state)
        await hand_action(state, 'jiagang', 21)
        await claims(state, {})
        assert state.game_status == 'deal_card_after_gang'
        supplement(state)
        # 第1家刚过了抢杠，普通血战受顺和限制；由未过和的第2家接炮。
        state.player_list[2].hand_tiles = [12, 13, 14, 16, 17, 18, 22, 23, 24, 26, 27, 28, 25]
        refresh_waiting_tiles(state, 2)
        await hand_action(state, 'cut', 25)
        assert state.sichuan_hu_results[2]['way'] == ['杠上炮']
        await claims(state, {2: 'hu'})
        await settle(state)
        assert first in state.player_list[0].gang_score_records
        assert not first.get('cancelled', False)
        assert [p.score for p in state.player_list] == ([0] * 4 if state.is_doubles else [3, -1, -1, -1])


@pytest.mark.parametrize('mode', FLOW)
@run_async
async def test_bloodflow_keeps_its_existing_rob_kong_rules(mode):
    async with scenario(mode) as (state, _):
        await first_kong(state, 'jiagang')
        first = state.player_list[0].gang_score_records[0]
        scores = [p.score for p in state.player_list]
        supplement(state)
        await hand_action(state, 'jiagang', 21)
        assert state.sichuan_hu_results[1]['way'] == ['抢杠']
        await claims(state, {1: 'hu'})
        assert first in state.player_list[0].gang_score_records
        assert [p.score for p in state.player_list] == scores


@pytest.mark.parametrize('mode', BATTLE)
@run_async
async def test_first_added_kong_is_only_rob_kong(mode):
    async with scenario(mode) as (state, _):
        state.player_list[0].hand_tiles[-1] = 21
        await hand_action(state, 'jiagang', 21)
        assert state.sichuan_hu_results[1]['way'] == ['抢杠']
        await claims(state, {1: 'hu'})
        await settle(state)
        assert [p.score for p in state.player_list] == [0] * 4
        assert state.paofen_watch is None
        assert not state.jiagang_after_gang and state.jiagang_previous_gang is None


@pytest.mark.parametrize('mode', BATTLE)
@run_async
async def test_discard_breaks_chain_and_a_later_robbery_keeps_old_kong_payment(mode):
    async with scenario(mode) as (state, _):
        await first_kong(state, 'jiagang')
        first = state.player_list[0].gang_score_records[0]
        supplement(state)
        await hand_action(state, 'cut', 12)
        if state.game_status == 'waiting_action_after_cut':
            await claims(state, {})
        assert not state.last_action_was_gang and state.paofen_watch is None
        # 后续普通摸牌轮到本家，使用先前留下的21加杠。
        state.current_player_index = 0
        state.player_list[0].get_tile(state.tiles_list)
        await hand_action(state, 'jiagang', 21)
        assert state.sichuan_hu_results[1]['way'] == ['抢杠']
        await claims(state, {1: 'hu'})
        await settle(state)
        assert first in state.player_list[0].gang_score_records and not first.get('cancelled', False)
        assert [p.score for p in state.player_list] == ([0] * 4 if state.is_doubles else [3, -1, -1, -1])


@pytest.mark.parametrize('mode', BATTLE)
@pytest.mark.parametrize('unpaid_middle', [False, True])
@run_async
async def test_three_kongs_refund_only_the_immediate_predecessor_even_if_unpaid(mode, unpaid_middle):
    async with scenario(mode) as (state, _):
        actor = state.player_list[0]
        actor.combination_tiles.append('k29')
        actor.combination_mask.append([1, 29, 0, 29, 0, 29])
        actor.hand_tiles = [12, 13, 14, 21 if unpaid_middle else 24, 11]
        draws = [24 if unpaid_middle else 21, 29, 25]
        if hasattr(state.tiles_list, 'parts'):
            state.tiles_list.parts[0] = list(draws)
        state.tiles_list[:3] = draws
        state.player_list[1].hand_tiles = [27, 28, 12, 13, 14, 15, 16, 17, 22, 23, 24, 25, 25]
        refresh_waiting_tiles(state, 1)
        assert 29 in state.player_list[1].waiting_tiles and 21 not in state.player_list[1].waiting_tiles
        await first_kong(state, 'jiagang')
        first = actor.gang_score_records[0]
        supplement(state)
        await hand_action(state, 'jiagang', 21)
        assert state.game_status == 'deal_card_after_gang'
        assert not state.jiagang_after_gang and state.jiagang_previous_gang is None
        supplement(state)
        await hand_action(state, 'jiagang', 29)
        assert set(state.sichuan_hu_results[1]['way']) == {'抢杠', '杠上炮'}
        await claims(state, {1: 'hu'})
        await settle(state)
        assert first in actor.gang_score_records and not first.get('cancelled', False)
        assert actor.combination_tiles == ['g11', 'g21', 'k29']
        if state.is_doubles:
            assert [r['cancelled'] for r in actor.gang_score_records] == [False, True, True]
            assert actor.combination_mask[1] == [0, 21] * 4
        else:
            assert actor.gang_score_records == [first]
        assert [p.score for p in state.player_list] == ([0] * 4 if state.is_doubles else [3, -1, -1, -1])
        state._reset_round_state()
        assert not state.jiagang_after_gang and state.jiagang_previous_gang is None and state.paofen_watch is None


@pytest.mark.parametrize('mode', BATTLE[1:])
@run_async
async def test_partner_direct_kong_has_no_tax_but_still_gives_gang_shang_pao(mode):
    async with scenario(mode, first='partner_gang') as (state, _):
        await first_kong(state, 'partner_gang')
        first = state.player_list[0].gang_score_records[0]
        assert first['payers'] == {} and first['void_reason'] == 'no_payer'
        supplement(state)
        await hand_action(state, 'jiagang', 21)
        assert set(state.sichuan_hu_results[1]['way']) == {'抢杠', '杠上炮'}
        await claims(state, {1: 'hu'})
        await settle(state)
        assert first['void_reason'] == 'no_payer'
        assert [p.score for p in state.player_list] == [0] * 4


@pytest.mark.parametrize('flush', [False, True])
@run_async
async def test_extra_fan_applies_before_shunhe_threshold_and_three_fan_cap(flush):
    async with scenario('standard') as (state, _):
        if flush:
            state.player_list[1].hand_tiles = [22, 23, 24, 25, 26, 26, 27, 28, 27, 28, 29, 25, 25]
            refresh_waiting_tiles(state, 1)
        await first_kong(state, 'jiagang')
        supplement(state)
        state.hepai_limit = 2
        state.player_list[1].shunhe_passed_max_fan = 1
        await hand_action(state, 'jiagang', 21)
        assert 'hu' in state.action_dict[1]
        result = state.sichuan_hu_results[1]
        assert result['fan'] == (4 if flush else 2)
        await claims(state, {1: 'hu'})
        await settle(state)
        assert state.deferred_hu_settlements[0]['base'] == (8 if flush else 4)
