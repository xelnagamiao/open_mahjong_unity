"""换三张真实动作分支：选牌、定缺、和后杠、抢杠、多响与末张结算。"""
import asyncio
from collections import Counter
import random
import time
from unittest.mock import patch

import pytest

from .test_xueliu_exchange import exchange_state, EXCHANGE_WIN
from .test_xueliu_integration import enqueue, fast_sleep
from .action_check import (refresh_waiting_tiles, check_action_after_cut, check_action_hand_action,
    check_action_jiagang, _xueliu_jiagang_preserves_waiting)
from .action_check import check_hepai
from .wait_action import wait_action


def prepare(state):
    for i, p in enumerate(state.player_list):
        p.hand_tiles = list(EXCHANGE_WIN if i == 0 else EXCHANGE_WIN[:-1])
        p.dingque_suit = 3
        refresh_waiting_tiles(state, i, is_first_action=i == 0)


def test_exchange_dealer_must_discard_before_any_win():
    state, _ = exchange_state()
    prepare(state)
    actions = check_action_hand_action(state, 0, is_first_action=True)[0]
    assert 'cut' in actions and 'hu_self' not in actions


def test_flow_state_rejects_a_standard_subrule_configuration():
    state, _ = exchange_state()
    state.sub_rule = 'sichuan/standard'
    with pytest.raises(ValueError, match='不支持的血流子规则'):
        state._configure_rule()


@pytest.mark.parametrize('kind,empty_wall,gang_draw,last_gang,expected,token', [
    ('handgot',True,False,False,6,'海底'),
    ('handgot',False,True,False,6,'杠上花'),
    ('dianhe',True,False,False,3,None),
    ('dianhe',False,False,True,6,'杠上炮'),
    ('qianggang',False,False,False,6,'抢杠'),
])
def test_real_win_check_builds_only_the_applicable_root_context(kind,empty_wall,gang_draw,last_gang,expected,token):
    state, _ = exchange_state()
    prepare(state)
    state.player_list[1].hand_tiles = list(EXCHANGE_WIN if kind == 'handgot' else EXCHANGE_WIN[:-1])
    if empty_wall:
        state.tiles_list = []
    state.last_action_was_gang = last_gang
    actions = {i: [] for i in range(4)}
    check_hepai(state, actions, 28, 1, kind, gang_draw)
    result = state.sichuan_hu_results[1]
    assert result['fan'] == expected
    assert result['way'] == ([token] if token else [])


def test_passed_ron_blocks_equal_value_but_allows_higher_value_and_self_draw():
    state, _ = exchange_state()
    prepare(state)
    player = state.player_list[1]
    player.shunhe_passed_max_fan = 3
    actions = {i: [] for i in range(4)}
    check_hepai(state, actions, 28, 1, 'dianhe')
    assert not actions[1]
    state.last_action_was_gang = True
    check_hepai(state, actions, 28, 1, 'dianhe')
    assert actions[1] == ['hu'] and state.sichuan_hu_results[1]['fan'] == 6
    player.hand_tiles = list(EXCHANGE_WIN)
    actions[1] = []
    check_hepai(state, actions, 28, 1, 'handgot')
    assert actions[1] == ['hu_self'] and state.sichuan_hu_results[1]['fan'] == 3


@pytest.mark.parametrize('phase,action', [('waiting_xueliu_throw','xueliu_exchange_three'),('waiting_dingque','dingque')])
def test_reconnect_repeats_only_the_current_opening_request_with_private_hand(phase,action):
    async def run():
        state, messages = exchange_state()
        prepare(state)
        state.game_status = phase
        state.action_dict = {i: [action] for i in range(4)}
        state.server_action_tick = 17
        state.xueliu_throw_deadline = state.dingque_deadline = time.time()+5
        user = state.player_list[1].user_id
        await state.player_reconnect(user)
        assert all(m['user_id'] == user for m in messages)
        info = messages[-1]['message']['ask_hand_action_info']
        assert info['action_list'] == [action] and info['action_tick'] == 17
        game = next(m['message']['game_info'] for m in messages if m['type'].endswith('/game_start'))
        assert all(bool(p.get('hand_tiles')) == (p['user_id'] == user) for p in game['players_info'])
    asyncio.run(run())


@pytest.mark.parametrize('direction', [1, 2, 3])
def test_three_exchange_directions_preserve_every_physical_tile(direction):
    state, _ = exchange_state()
    deck = [suit * 10 + rank for suit in (1, 2, 3) for rank in range(1, 10) for _ in range(4)]
    random.Random(912).shuffle(deck)
    offset = 0
    original = []
    for i, p in enumerate(state.player_list):
        size = 14 if i == 0 else 13
        p.hand_tiles = deck[offset:offset + size]
        offset += size
        original.append(Counter(p.hand_tiles))
    before = Counter(t for p in state.player_list for t in p.hand_tiles)
    for i in range(4):
        state._consume_xueliu_throw_tiles(i, state._default_xueliu_throw_tiles(i))
    with patch('server.gamestate.game_sichuan.XueliuGameState.random.Random') as rng:
        rng.return_value.choice.return_value = direction
        state._exchange_selected_tiles()
    assert state.xueliu_exchange_direction == direction
    assert Counter(t for p in state.player_list for t in p.hand_tiles) == before
    assert [len(p.hand_tiles) for p in state.player_list] == [14, 13, 13, 13]
    for i, p in enumerate(state.player_list):
        received = state.player_list[(i - direction) % 4].xueliu_throw_tiles
        assert Counter(p.hand_tiles) == original[i] - Counter(p.xueliu_throw_tiles) + Counter(received)


@pytest.mark.parametrize('responders', [(), (0,), (0, 1, 2)])
def test_timeout_completes_exchange_when_some_players_do_not_submit(responders):
    async def run():
        state, _ = exchange_state()
        prepare(state)
        # 两门各少于三张的手牌仍按当前强制换牌策略处理。
        state.player_list[3].hand_tiles = [11,11,12,12,13,13,14,14,15,15,16,21,31]
        before = Counter(t for p in state.player_list for t in p.hand_tiles)
        now = [100.0]
        async def ask(current):
            for i in responders:
                await enqueue(current, i, current.xueliu_opening_action, selected_tiles=[11,12,13])
            # 首轮读取有效提交，第二轮进入自动选牌。
            asyncio.get_running_loop().call_soon(lambda: now.__setitem__(0, 111.0))
        with patch(__package__+'.boardcast.broadcast_xueliu_throw_three_ask', ask), \
             patch(__package__+'.XueliuGameState.time.time', lambda: now[0]):
            await state._xueliu_throw_three_phase()
        assert [len(p.hand_tiles) for p in state.player_list] == [14,13,13,13]
        assert all(len(p.xueliu_throw_tiles) == 3 and len({t//10 for t in p.xueliu_throw_tiles}) == 1 for p in state.player_list)
        assert Counter(t for p in state.player_list for t in p.hand_tiles) == before
        assert not state.waiting_players_list
    asyncio.run(run())


def test_exchange_opening_drains_old_requests_and_rejects_invalid_batch_before_valid_choice():
    async def run():
        state, _ = exchange_state()
        prepare(state)
        await enqueue(state, 0, 'cut', TileId=11)
        async def ask(current):
            for i in range(4):
                await enqueue(current, i, current.xueliu_opening_action,
                    selected_tiles=[11,12,13], _action_tick=current.server_action_tick - 1)
                await enqueue(current, i, 'xueliu_throw_three', selected_tiles=[11,12,13])
                await enqueue(current, i, current.xueliu_opening_action, selected_tiles=[11,12,21])
                await enqueue(current, i, current.xueliu_opening_action,
                    selected_tiles=[21,22,23], _action_tick=current.server_action_tick)
        with patch(__package__+'.boardcast.broadcast_xueliu_throw_three_ask', ask):
            await state._xueliu_throw_three_phase()
        assert all(p.xueliu_throw_tiles == [21,22,23] for p in state.player_list)
        assert state.waiting_players_list == [] and all(not actions for actions in state.action_dict.values())
    asyncio.run(run())


def test_invalid_exchange_choice_keeps_the_player_pending_until_a_later_valid_retry():
    async def run():
        state, _ = exchange_state()
        prepare(state)
        original = list(state.player_list[0].hand_tiles)
        retry = None
        async def ask(current):
            nonlocal retry
            await enqueue(current, 0, current.xueliu_opening_action, selected_tiles=[11,12,21])
            for i in (1,2,3):
                await enqueue(current, i, current.xueliu_opening_action, selected_tiles=[11,12,13])
            async def correct():
                while current.action_events[0].is_set():
                    await asyncio.sleep(0)
                assert current.player_list[0].hand_tiles == original
                assert current.action_dict[0] == [current.xueliu_opening_action]
                await enqueue(current, 0, current.xueliu_opening_action, selected_tiles=[21,22,23])
            retry = asyncio.create_task(correct())
        with patch(__package__+'.boardcast.broadcast_xueliu_throw_three_ask', ask):
            await asyncio.wait_for(state._xueliu_throw_three_phase(), 2)
            await retry
        assert state.player_list[0].xueliu_throw_tiles == [21,22,23]
        assert not state.waiting_players_list
    asyncio.run(run())


def test_dingque_advances_tick_and_rejects_stale_and_malformed_choices():
    async def run():
        state, _ = exchange_state()
        prepare(state)
        old_tick = state.server_action_tick
        async def ask(current):
            for i in range(4):
                await enqueue(current, i, 'dingque', target_tile=1, _action_tick=old_tick)
                await enqueue(current, i, 'dingque', target_tile=True, _action_tick=current.server_action_tick)
                await enqueue(current, i, 'dingque', target_tile=3, _action_tick=current.server_action_tick)
        with patch(__package__+'.SichuanGameState.broadcast_dingque_ask', ask):
            await state._dingque_phase()
        assert state.server_action_tick == old_tick + 1
        assert [p.dingque_suit for p in state.player_list] == [3]*4
    asyncio.run(run())


@pytest.mark.parametrize('allowed', [True, False])
def test_added_kong_after_win_must_preserve_the_complete_wait_set(allowed):
    state, _ = exchange_state()
    p = state.player_list[0]
    p.hand_tiles = [12,13,14,21,22,23,24,25,26,28,11]
    p.combination_tiles = ['k11']
    p.dingque_suit = 3
    p.has_won = p.post_hu_lock = p.has_draw_slot = True
    p.waiting_tiles = p.locked_waiting_tiles = {28} if allowed else {28,29}
    assert _xueliu_jiagang_preserves_waiting(state, p, 11) is allowed
    assert ('jiagang' in check_action_hand_action(state, 0)[0]) is allowed


@pytest.mark.parametrize('kind,tile,hand,melds,mask,changes', [
    ('angang',11,[11]*4+[12,13,14,21,22,23,24,25,26,28],[],[],[6,-2,-2,-2]),
    ('jiagang',11,[12,13,14,21,22,23,24,25,26,28,11],['k11'],[[1,11,0,11,0,11]],[3,-1,-1,-1]),
])
def test_real_hand_kong_actions_record_immediate_scores(kind,tile,hand,melds,mask,changes):
    async def run():
        state, messages = exchange_state()
        p = state.player_list[0]
        p.hand_tiles, p.combination_tiles, p.combination_mask = hand, melds, mask
        p.dingque_suit = 3
        p.has_draw_slot = True
        for other in state.player_list[1:]:
            other.hand_tiles = [21,23,25,27,29,22,24,26,28,29,21,23,25]
            other.waiting_tiles = set()
        state.current_player_index = 0
        state.game_status = 'waiting_hand_action'
        state.action_dict = check_action_hand_action(state, 0)
        assert kind in state.action_dict[0]
        await enqueue(state, 0, kind, target_tile=tile)
        await wait_action(state)
        assert [p.score for p in state.player_list] == changes
        assert state.game_status == 'deal_card_after_gang'
        assert p.combination_tiles == [('G' if kind == 'angang' else 'g')+str(tile)]
        assert len(p.gang_score_records) == 1
        assert any(m['message'].get('do_action_info',{}).get('gang_score_changes') for m in messages)
    asyncio.run(run())


@pytest.mark.parametrize('accepted', [(1,), (1,2), (1,2,3)])
def test_actual_multi_ron_settles_only_accepted_winners_and_all_remain_active(accepted):
    async def run():
        state, messages = exchange_state()
        prepare(state)
        source = state.player_list[0]
        source.hand_tiles = EXCHANGE_WIN[:-1]
        source.discard_tiles = [28]
        state.current_player_index = 0
        state.game_status = 'waiting_action_after_cut'
        state.action_dict = check_action_after_cut(state,28)
        for i in range(1,4):
            await enqueue(state,i,'hu' if i in accepted else 'pass')
        await wait_action(state)
        assert set(state.sichuan_hu_results) == set(accepted)
        with patch(__package__+'.XueliuGameState.asyncio.sleep',fast_sleep):
            await state._settle_win()
        assert [p.score for p in state.player_list] == [-3*len(accepted)]+[3 if i in accepted else 0 for i in range(1,4)]
        assert all(not p.is_hu for p in state.player_list)
        assert state.current_player_index == accepted[-1] and state.game_status == 'deal_card'
        assert state._decide_next_dealer() == (0 if len(accepted)>1 else accepted[0])
        result = state.game_record['game_round'][f'round_index_{state.round_index}']
        win_ticks = [tick for tick in result['action_ticks'] if tick[0] in ('hu_first', 'hu_second', 'hu_third')]
        assert [tick[1] for tick in win_ticks] == list(accepted)
    asyncio.run(run())


def test_actual_robbed_added_kong_refunds_that_kong_once_and_reconnects_as_pung():
    async def run():
        state, messages = exchange_state()
        prepare(state)
        source = state.player_list[0]
        source.hand_tiles = [11,12,13,14,15,16,21,22,23,29]
        source.combination_tiles = ['G19','g28']
        source.combination_mask = [[0,19]*4,[0,28,0,28,3,28,1,28]]
        state._record_gang_score(0,19,'xiayu2')
        state._record_gang_score(0,28,'xiayu1')
        state.current_player_index = 0
        state.jiagang_tile = 28
        state.game_status = 'waiting_action_qianggang'
        state.action_dict = check_action_jiagang(state,28)
        for i in (1,2,3):
            await enqueue(state,i,'hu' if i in (1,2) else 'pass')
        await wait_action(state)
        assert source.combination_tiles == ['G19','k28']
        assert source.combination_mask[1] == [0,28,0,28,1,28]
        assert [p.score for p in state.player_list] == [6,-2,-2,-2]
        assert len(source.gang_score_records) == 1
        with patch(__package__+'.XueliuGameState.asyncio.sleep',fast_sleep):
            await state._settle_win()
        assert [p.score for p in state.player_list] == [-6,4,4,-2]
        await state.player_reconnect(source.user_id)
        assert state._refund_last_gang(0,28) == {}
        refunds=[tick for tick in state.game_record['game_round'][f'round_index_{state.round_index}']['action_ticks'] if tick[:2] == ['gr', 'gs']]
        assert len(refunds) == 1
    asyncio.run(run())


def test_wall_end_rechecks_previously_won_players_and_finishes_sixteenth_hand():
    state, messages = exchange_state()
    prepare(state)
    state.current_round = state.max_round * 4
    state.tiles_list = []
    for p in state.player_list:
        p.hand_tiles = list(EXCHANGE_WIN[:-1])
    state.player_list[3].hand_tiles = [11,13,15,17,19,22,24,26,28,29,12,24,29]
    state.player_list[3].has_won = state.player_list[3].post_hu_lock = True
    state.player_list[3].locked_waiting_tiles = {28}
    with patch(__package__+'.XueliuGameState.asyncio.sleep',fast_sleep):
        asyncio.run(state._settle_liuju())
    assert [p.score for p in state.player_list] == [3,3,3,-9]
    assert messages[-1]['message']['show_result_info']['next_status'] == 'match_end'
    assert sum(p.score for p in state.player_list) == 0


def test_round_reset_removes_locks_passed_wins_and_kong_records_without_resetting_total_score():
    state, _ = exchange_state()
    for p in state.player_list:
        p.score = 7
        p.has_won = p.post_hu_lock = True
        p.locked_waiting_tiles = {28}
        p.shunhe_passed_max_fan = 3
        p.gang_score_records = [{'type':'xiayu1'}]
        p.huapai_list = [28,28]
    state.first_win_event = {'winners':[1],'discarder':0}
    state._reset_round_state()
    assert all(p.score == 7 and not p.has_won and not p.post_hu_lock and not p.locked_waiting_tiles
               and not p.gang_score_records and not p.huapai_list and p.shunhe_passed_max_fan is None for p in state.player_list)
    assert state.first_win_event is None
